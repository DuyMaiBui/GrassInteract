#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    // ──────────────────────────────────────────────────────────────────────────
    // InstanceData — blittable GPU record for static mesh prop instances.
    // Total size: 20 bytes.  Layout MUST be byte-identical to the BladeInstance
    // struct in GrassCull.compute so the reused cull kernel can read posWS for
    // frustum / distance / LOD without any shader modification.
    //
    //   field           offset  size
    //   float3 posWS    0       12 B   ← cull kernel reads ONLY this field
    //   uint packedYawScale  12  4 B   ← hi16=yaw, lo16=scale (same pack as BladeInstance)
    //   uint lodHash     16      4 B   ← per-instance XorShift hash (future jitter)
    //                          ─────
    //                          20 B
    //
    // packedYawScale packing (mirrors ChunkedBladeBuffer exactly):
    //   high 16 bits = yaw quantised to [0..65535] over [0..360) degrees.
    //   low  16 bits = scale quantised to [0..65535] over [0 .. scaleMax].
    // Unpack in the vertex shader:
    //   float yaw   = (packedYawScale >> 16) / 65535.0 * 360.0;
    //   float scale = (packedYawScale & 0xFFFF) / 65535.0 * _ScaleMax2;
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-instance GPU record for static mesh props.  20 bytes, byte-compatible
    /// with <c>BladeInstance</c> in GrassCull.compute so the reused cull kernel
    /// reads posWS correctly without modification.
    /// </summary>
    public struct InstanceData
    {
        /// <summary>World-space base position of the instance (ground level).</summary>
        public Vector3 posWS;

        /// <summary>
        /// Packed yaw (hi16) and scale (lo16).
        /// hi16 = round(yaw / 360 * 65535), lo16 = round(scale / scaleMax * 65535).
        /// </summary>
        public uint packedYawScale;

        /// <summary>Per-instance deterministic random hash (XorShift32 of flat index + 1).</summary>
        public uint lodHash;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Baker + GPU buffer holder for static mesh props.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bakes a <see cref="GrassScatterResult"/> (produced for a Mesh-kind layer by the shared
    /// <see cref="GrassScatter.Build"/> path) into GPU-ready chunked buffers:
    /// a compact <see cref="InstanceData"/> array sorted by grid cell, one <see cref="ChunkAabb"/>
    /// per cell inflated by the prop mesh bounds extent, and a <see cref="ChunkRange"/> per cell.
    ///
    /// Sibling of <see cref="ChunkedBladeBuffer"/> — grass keeps using its own buffer; props use this.
    /// Upload is done once; the per-frame GPU cull pipeline reads the buffers read-only.
    ///
    /// Call <see cref="Bake"/> from edit-mode build steps.
    /// Call <see cref="Dispose"/> to release the three <see cref="GraphicsBuffer"/>s.
    /// </summary>
    public sealed class ChunkedInstanceBuffer : IDisposable
    {
        // ── GPU struct strides (bytes) ────────────────────────────────────────
        // InstanceData: float3(12) + uint(4) + uint(4) = 20 B.
        // ChunkAabb:    float3(12) + float3(12)        = 24 B.
        // ChunkRange:   uint(4)    + uint(4)           =  8 B.
        private const int INSTANCE_STRIDE = 20;
        private const int AABB_STRIDE     = 24;
        private const int RANGE_STRIDE    =  8;

        // ── Packing constants ─────────────────────────────────────────────────
        private const float YAW_ENCODE_SCALE = 65535f / 360f;

        // ── CPU-side baked arrays ─────────────────────────────────────────────
        private InstanceData[]? instances;
        private ChunkAabb[]?    chunkAabbs;
        private ChunkRange[]?   chunkRanges;

        // ── GPU buffers ───────────────────────────────────────────────────────
        private GraphicsBuffer? instanceBuf;
        private GraphicsBuffer? aabbBuf;
        private GraphicsBuffer? rangeBuf;

        // ── Grid metadata ─────────────────────────────────────────────────────
        /// <summary>Number of grid cells along X.</summary>
        public int GridX { get; private set; }

        /// <summary>Number of grid cells along Z.</summary>
        public int GridZ { get; private set; }

        /// <summary>World-space XZ cell size in metres.</summary>
        public int ChunkSize { get; private set; }

        /// <summary>Total grid cells = GridX × GridZ.</summary>
        public int TotalChunks { get; private set; }

        /// <summary>Total instances baked (== scatter.TotalCount at bake time).</summary>
        public int TotalInstances { get; private set; }

        /// <summary>Scale encode upper-bound (ScaleRange.y). Needed by the VS to decode packedYawScale.</summary>
        public float ScaleMax { get; private set; }

        // ── GPU buffer accessors ──────────────────────────────────────────────

        /// <summary>Structured <see cref="GraphicsBuffer"/> of <see cref="InstanceData"/> (stride 20 B).</summary>
        public GraphicsBuffer? InstanceBuffer => this.instanceBuf;

        /// <summary>Structured <see cref="GraphicsBuffer"/> of <see cref="ChunkAabb"/> (stride 24 B).</summary>
        public GraphicsBuffer? AabbBuffer => this.aabbBuf;

        /// <summary>Structured <see cref="GraphicsBuffer"/> of <see cref="ChunkRange"/> (stride 8 B).</summary>
        public GraphicsBuffer? RangeBuffer => this.rangeBuf;

        // ── CPU accessors (for validation / harness) ──────────────────────────

        /// <summary>CPU-side copy of the baked instance array. Null before <see cref="Bake"/>.</summary>
        public InstanceData[]? Instances => this.instances;

        /// <summary>CPU-side copy of per-chunk AABBs. Null before bake.</summary>
        public ChunkAabb[]? ChunkAabbs => this.chunkAabbs;

        /// <summary>CPU-side copy of per-chunk ranges. Null before bake.</summary>
        public ChunkRange[]? ChunkRanges => this.chunkRanges;

        // ─────────────────────────────────────────────────────────────────────
        // Bake
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes a scatter result into sorted, chunk-partitioned GPU buffers.
        /// AABB Y and lateral inflation uses <paramref name="meshBounds"/> extent instead
        /// of blade reach, so the per-chunk cull headroom is prop-mesh appropriate.
        /// </summary>
        /// <param name="scatter">Scatter output from GrassScatter.Build (for the Mesh-kind layer).</param>
        /// <param name="origin">Field world-space origin.</param>
        /// <param name="fieldBoundsXZ">Layer FieldBounds XZ extents.</param>
        /// <param name="scaleRangeMax">Layer ScaleRange.y — encode upper-bound.</param>
        /// <param name="meshBounds">
        /// World-space bounds of the prop mesh (max over all LODs). The bounds EXTENT (half-diagonal)
        /// is used as the AABB inflation in Y and laterally, mirroring the blade-reach role.
        /// </param>
        /// <param name="oriented">
        /// When true (layer.IsOriented), slot2 (lodHash field) encodes octNormal(hi16) | pitch8 | roll8
        /// from the baked surface normal and per-instance rotation.
        /// When false (default), slot2 = decorrelation hash (UNCHANGED — byte-stable).
        /// Delegates to <see cref="ChunkedBladeBuffer.PackOrientedSlot2"/> — SSOT for the packing format.
        /// </param>
        /// <param name="chunkSize">Cell size in metres. Use layer's config or a sensible default (e.g. 8).</param>
        public void Bake(
            GrassScatterResult scatter,
            Vector3            origin,
            Vector2            fieldBoundsXZ,
            float              scaleRangeMax,
            Bounds             meshBounds,
            bool               oriented  = false,
            int                chunkSize = 8)
        {
            this.Dispose();

            int cellSize = Mathf.Max(1, chunkSize);

            // Field min corner (matches GrassFieldSpace convention).
            var minXZ = new Vector2(
                origin.x - fieldBoundsXZ.x * 0.5f,
                origin.z - fieldBoundsXZ.y * 0.5f);

            int gridX       = Mathf.Max(1, Mathf.CeilToInt(fieldBoundsXZ.x / cellSize));
            int gridZ       = Mathf.Max(1, Mathf.CeilToInt(fieldBoundsXZ.y / cellSize));
            int totalChunks = gridX * gridZ;
            int totalInst   = scatter.TotalCount;

            this.GridX          = gridX;
            this.GridZ          = gridZ;
            this.ChunkSize      = cellSize;
            this.TotalChunks    = totalChunks;
            this.TotalInstances = totalInst;

            float maxScale = scaleRangeMax > 0f ? scaleRangeMax : 1f;
            this.ScaleMax = maxScale;
            float encodeScaleScale = 65535f / maxScale;

            // AABB inflation: use the mesh bounds EXTENT (half-diagonal) scaled by maxScale.
            // This is the analog of bladeReachY in ChunkedBladeBuffer — it keeps the cull
            // headroom correct regardless of prop mesh size/height.
            float meshExtent    = meshBounds.extents.magnitude * maxScale;
            float aabbReachY    = meshExtent;   // upward inflation (prop can only go up from root)
            float lateralPad    = meshExtent;   // lateral inflation for XZ sway footprint

            // ── Step 1: count instances per cell (counting sort, pass 1) ─────
            int[] cellCounts = new int[totalChunks];
            int[] instCell   = new int[totalInst];

            int flatIdx = 0;
            for (int b = 0; b < scatter.BaseSlabs.Length; ++b)
            {
                int count       = scatter.SlabCounts[b];
                Vector3[] posSlab = scatter.BasePositionSlabs[b];
                for (int k = 0; k < count; ++k)
                {
                    Vector3 pos = posSlab[k];
                    int cx = Mathf.Clamp((int)((pos.x - minXZ.x) / cellSize), 0, gridX - 1);
                    int cz = Mathf.Clamp((int)((pos.z - minXZ.y) / cellSize), 0, gridZ - 1);
                    int cell = cz * gridX + cx;
                    instCell[flatIdx] = cell;
                    cellCounts[cell]++;
                    flatIdx++;
                }
            }

            // ── Step 2: prefix-sum → per-cell start offsets ──────────────────
            int[] cellStart = new int[totalChunks];
            int   running   = 0;
            for (int c = 0; c < totalChunks; ++c)
            {
                cellStart[c] = running;
                running += cellCounts[c];
            }

            // ── Step 3: scatter instances into sorted output, build AABBs ────
            var instOut  = new InstanceData[totalInst];
            var aabbOut  = new ChunkAabb[totalChunks];
            var rangeOut = new ChunkRange[totalChunks];

            // Sentinel AABBs (min > max → ChunkCull trivially rejects empty cells).
            for (int c = 0; c < totalChunks; ++c)
            {
                aabbOut[c] = new ChunkAabb
                {
                    min = new Vector3(float.MaxValue,  float.MaxValue,  float.MaxValue),
                    max = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                };
            }

            int[] writeCursor = new int[totalChunks];
            Array.Copy(cellStart, writeCursor, totalChunks);

            flatIdx = 0;
            for (int b = 0; b < scatter.BaseSlabs.Length; ++b)
            {
                int count        = scatter.SlabCounts[b];
                Matrix4x4[]  matSlab = scatter.BaseSlabs[b];
                Vector3[]    posSlab = scatter.BasePositionSlabs[b];
                Vector3[]    nrmSlab = scatter.BaseNormalSlabs[b];

                for (int k = 0; k < count; ++k)
                {
                    int       cell = instCell[flatIdx];
                    int       outI = writeCursor[cell]++;
                    Matrix4x4 m    = matSlab[k];
                    Vector3   pos  = posSlab[k];
                    Vector3   nrm  = nrmSlab[k]; // surface normal (Vector3.up for non-oriented)

                    // Decompose base matrix (mirrors ChunkedBladeBuffer exactly).
                    float yaw   = m.rotation.eulerAngles.y;
                    float scale = m.lossyScale.x;

                    uint yawQ   = (uint)Mathf.RoundToInt(Mathf.Clamp(yaw,   0f,    360f)  * YAW_ENCODE_SCALE);
                    uint scaleQ = (uint)Mathf.RoundToInt(Mathf.Clamp(scale, 0f, maxScale) * encodeScaleScale);
                    uint packed = ((yawQ & 0xFFFFu) << 16) | (scaleQ & 0xFFFFu);

                    // Slot2: oriented → octNormal(hi16) | pitch8 | roll8 (delegates to ChunkedBladeBuffer SSOT).
                    //        legacy  → deterministic XorShift32 hash (byte-stable, unchanged).
                    uint slot2;
                    if (oriented)
                    {
                        Vector3 euler  = m.rotation.eulerAngles;
                        float pitchDeg = euler.x > 180f ? euler.x - 360f : euler.x;
                        float rollDeg  = euler.z > 180f ? euler.z - 360f : euler.z;
                        slot2 = ChunkedBladeBuffer.PackOrientedSlot2(nrm, pitchDeg, rollDeg);
                    }
                    else
                    {
                        slot2 = XorShift32((uint)(flatIdx + 1));
                    }

                    instOut[outI] = new InstanceData
                    {
                        posWS          = pos,
                        packedYawScale = packed,
                        lodHash        = slot2,
                    };

                    // Expand per-cell AABB to include this instance's base position.
                    ref ChunkAabb aabb = ref aabbOut[cell];
                    if (pos.x < aabb.min.x) aabb.min.x = pos.x;
                    if (pos.y < aabb.min.y) aabb.min.y = pos.y;
                    if (pos.z < aabb.min.z) aabb.min.z = pos.z;
                    if (pos.x > aabb.max.x) aabb.max.x = pos.x;
                    if (pos.y > aabb.max.y) aabb.max.y = pos.y;
                    if (pos.z > aabb.max.z) aabb.max.z = pos.z;

                    flatIdx++;
                }
            }

            // Expand AABBs by prop-mesh headroom; fill ranges.
            for (int c = 0; c < totalChunks; ++c)
            {
                int cnt = cellCounts[c];
                if (cnt == 0)
                {
                    rangeOut[c] = new ChunkRange { start = 0, count = 0 };
                    continue;
                }

                ref ChunkAabb aabb = ref aabbOut[c];
                aabb.max.y += aabbReachY;
                aabb.min.x -= lateralPad;
                aabb.max.x += lateralPad;
                aabb.min.z -= lateralPad;
                aabb.max.z += lateralPad;

                rangeOut[c] = new ChunkRange
                {
                    start = (uint)cellStart[c],
                    count = (uint)cnt,
                };
            }

            // ── Step 4: store CPU arrays ──────────────────────────────────────
            this.instances  = instOut;
            this.chunkAabbs = aabbOut;
            this.chunkRanges = rangeOut;

            // ── Step 5: upload to GPU ─────────────────────────────────────────
            if (totalInst > 0)
            {
                this.instanceBuf = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, totalInst, INSTANCE_STRIDE);
                this.instanceBuf.SetData(instOut);
            }

            if (totalChunks > 0)
            {
                this.aabbBuf = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, totalChunks, AABB_STRIDE);
                this.aabbBuf.SetData(aabbOut);

                this.rangeBuf = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured, totalChunks, RANGE_STRIDE);
                this.rangeBuf.SetData(rangeOut);
            }

            Debug.Log($"[ChunkedInstanceBuffer] Baked: TotalInstances={totalInst}, " +
                      $"Grid={gridX}×{gridZ} ({totalChunks} chunks), CellSize={cellSize}m, " +
                      $"ScaleMax={maxScale:F3}, MeshExtent={meshExtent:F3}m");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Dispose
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Releases the three GPU <see cref="GraphicsBuffer"/>s. Unity-null-safe.</summary>
        public void Dispose()
        {
            if (this.instanceBuf != null) { this.instanceBuf.Release(); this.instanceBuf = null; }
            if (this.aabbBuf     != null) { this.aabbBuf.Release();     this.aabbBuf     = null; }
            if (this.rangeBuf    != null) { this.rangeBuf.Release();    this.rangeBuf    = null; }
            this.instances   = null;
            this.chunkAabbs  = null;
            this.chunkRanges = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Simple Xorshift32 — deterministic, non-zero output for any non-zero seed.</summary>
        private static uint XorShift32(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }
    }
}
