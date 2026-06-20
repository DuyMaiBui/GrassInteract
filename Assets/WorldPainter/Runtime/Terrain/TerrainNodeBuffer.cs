#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Uploads selected CdlodNode[] and per-node AABB arrays into GPU buffers.
    /// Mirrors ChunkedBladeBuffer upload + ValidatePartition discipline.
    ///
    /// NodeBuffer  : StructuredBuffer&lt;RenderNode&gt;  stride = CdlodNode.STRIDE (32 B)
    /// AabbBuffer  : StructuredBuffer&lt;NodeAabb&gt;    stride = NODE_AABB_STRIDE  (24 B)
    ///
    /// NodeAabb layout (matches TerrainNodeCull.compute struct NodeAabb):
    ///   float3 mn (12 B) + float3 mx (12 B) = 24 B.
    /// </summary>
    public sealed class TerrainNodeBuffer : IDisposable
    {
        // ── Stride constants ──────────────────────────────────────────────────
        private const int NODE_AABB_STRIDE = 24; // float3(12) + float3(12)

        // ── Blittable GPU struct ──────────────────────────────────────────────
        private struct NodeAabb
        {
            public Vector3 mn;
            public Vector3 mx;
        }

        // ── GPU buffers (capacity-tracked, grow-only — reused across frames) ───
        private GraphicsBuffer? nodeBuf;
        private GraphicsBuffer? aabbBuf;
        private int capacity;

        // ── CPU scratch arrays (length == capacity; reused across frames) ──────
        private CdlodNode[]? cpuNodes;
        private NodeAabb[]?  cpuAabbs;

        // ── Public accessors ──────────────────────────────────────────────────
        public GraphicsBuffer? NodeBuffer => this.nodeBuf;
        public GraphicsBuffer? AabbBuffer => this.aabbBuf;
        public int NodeCount { get; private set; }

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// Upload selected nodes and their AABBs to GPU.
        /// Per-node AABB Y extent = [tileMinY, tileMaxY] (conservative).
        /// Phase 3 can supply per-node min/max from height mip data.
        /// </summary>
        public void Upload(
            IReadOnlyList<CdlodNode> nodes,
            float tileMinY,
            float tileMaxY,
            float nodeCullMargin = 0f)
        {
            int count = nodes.Count;

            if (count == 0)
            {
                // Keep the GPU buffers + scratch allocated for reuse; just report zero nodes.
                // The live Submit path early-outs on nodes.Count == 0 before calling here, so
                // this branch is only hit by tests / non-render callers.
                this.NodeCount = 0;
                return;
            }

            this.EnsureCapacity(count);
            this.NodeCount = count;

            CdlodNode[] nodeArr = this.cpuNodes!;
            NodeAabb[]  aabbArr = this.cpuAabbs!;

            for (int i = 0; i < count; ++i)
            {
                CdlodNode n = nodes[i];
                nodeArr[i] = n;

                aabbArr[i] = new NodeAabb
                {
                    mn = new Vector3(
                        n.worldOffset.x - nodeCullMargin,
                        tileMinY,
                        n.worldOffset.z - nodeCullMargin),
                    mx = new Vector3(
                        n.worldOffset.x + n.scale + nodeCullMargin,
                        tileMaxY        + nodeCullMargin,
                        n.worldOffset.z + n.scale + nodeCullMargin),
                };
            }

            // Upload only the live prefix [0, count). The buffers may be larger than count;
            // the cull compute only ever indexes [0, count) (dispatched with nodes.Count).
            this.nodeBuf!.SetData(nodeArr, 0, 0, count);
            this.aabbBuf!.SetData(aabbArr, 0, 0, count);
        }

        /// <summary>
        /// Grow-only allocation: (re)creates the GPU buffers + CPU scratch only when the
        /// requested node count exceeds the current capacity. Steady-state frames reuse the
        /// existing buffers and allocate nothing — restoring the zero-per-frame-alloc
        /// discipline the rest of the renderer upholds. Released only in <see cref="Dispose"/>.
        /// </summary>
        private void EnsureCapacity(int count)
        {
            if (this.nodeBuf != null && this.aabbBuf != null &&
                this.cpuNodes != null && this.cpuAabbs != null && count <= this.capacity)
                return;

            int newCap = Mathf.Max(count, this.capacity > 0 ? this.capacity * 2 : count);

            this.ReleaseBuffers();

            this.capacity = newCap;
            this.cpuNodes = new CdlodNode[newCap];
            this.cpuAabbs = new NodeAabb[newCap];
            this.nodeBuf  = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, newCap, CdlodNode.STRIDE);
            this.aabbBuf  = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, newCap, NODE_AABB_STRIDE);
        }

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that the uploaded node AABBs union covers the tile XZ region.
        /// Mirrors ChunkedBladeBuffer.ValidatePartition.
        /// </summary>
        public bool ValidatePartition(
            float tileOriginX, float tileOriginZ, float tileSize,
            out string report)
        {
            var sb = new StringBuilder();

            if (this.cpuNodes == null || this.NodeCount == 0)
            {
                report = "SKIP: No nodes uploaded.";
                return true;
            }

            float uMinX = float.MaxValue, uMaxX = float.MinValue;
            float uMinZ = float.MaxValue, uMaxZ = float.MinValue;

            // Iterate only the live prefix [0, NodeCount); cpuNodes may be larger (capacity).
            for (int i = 0; i < this.NodeCount; ++i)
            {
                CdlodNode n = this.cpuNodes[i];
                if (n.worldOffset.x < uMinX) uMinX = n.worldOffset.x;
                if (n.worldOffset.z < uMinZ) uMinZ = n.worldOffset.z;
                float mx = n.worldOffset.x + n.scale;
                float mz = n.worldOffset.z + n.scale;
                if (mx > uMaxX) uMaxX = mx;
                if (mz > uMaxZ) uMaxZ = mz;
            }

            const float EPS = 0.01f;
            bool coversX = uMinX <= tileOriginX + EPS && uMaxX >= tileOriginX + tileSize - EPS;
            bool coversZ = uMinZ <= tileOriginZ + EPS && uMaxZ >= tileOriginZ + tileSize - EPS;
            bool pass    = coversX && coversZ;

            if (!pass)
                sb.AppendLine($"FAIL: Node AABB union [{uMinX:F1},{uMaxX:F1}]x[{uMinZ:F1},{uMaxZ:F1}]" +
                              $" does not cover tile [{tileOriginX:F1},{tileOriginX+tileSize:F1}]x" +
                              $"[{tileOriginZ:F1},{tileOriginZ+tileSize:F1}]");
            else
                sb.AppendLine($"  [PASS] Node AABB union covers tile XZ (nodes={this.NodeCount})");

            sb.Insert(0, pass ? "PASS: " : "FAIL: ");
            report = sb.ToString().TrimEnd();
            return pass;
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose() => this.ReleaseBuffers();

        private void ReleaseBuffers()
        {
            if (this.nodeBuf != null) { this.nodeBuf.Release(); this.nodeBuf = null; }
            if (this.aabbBuf != null) { this.aabbBuf.Release(); this.aabbBuf = null; }
            this.cpuNodes  = null;
            this.cpuAabbs  = null;
            this.capacity  = 0;
            this.NodeCount = 0;
        }
    }
}
