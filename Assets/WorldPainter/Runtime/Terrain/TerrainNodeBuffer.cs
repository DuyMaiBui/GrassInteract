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

        // ── GPU buffers ────────────────────────────────────────────────────────
        private GraphicsBuffer? nodeBuf;
        private GraphicsBuffer? aabbBuf;

        // ── CPU arrays (for ValidatePartition) ────────────────────────────────
        private CdlodNode[]? cpuNodes;

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
                this.ReleaseBuffers();
                return;
            }

            this.ReleaseBuffers();
            this.NodeCount = count;

            // Build CPU arrays
            CdlodNode[] nodeArr = new CdlodNode[count];
            NodeAabb[]  aabbArr = new NodeAabb[count];

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

            this.cpuNodes = nodeArr;

            this.nodeBuf = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, count, CdlodNode.STRIDE);
            this.nodeBuf.SetData(nodeArr);

            this.aabbBuf = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, count, NODE_AABB_STRIDE);
            this.aabbBuf.SetData(aabbArr);
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

            if (this.cpuNodes == null || this.cpuNodes.Length == 0)
            {
                report = "SKIP: No nodes uploaded.";
                return true;
            }

            float uMinX = float.MaxValue, uMaxX = float.MinValue;
            float uMinZ = float.MaxValue, uMaxZ = float.MinValue;

            foreach (CdlodNode n in this.cpuNodes)
            {
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
                sb.AppendLine($"  [PASS] Node AABB union covers tile XZ (nodes={this.cpuNodes.Length})");

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
            this.NodeCount = 0;
        }
    }
}
