#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUGrass
{
    // ──────────────────────────────────────────────────────────────────────────
    // Blittable GPU structs — only float/uint fields; no managed refs, no bool.
    // Strides MUST match the HLSL structs in GrassCull.compute / GpuGrassIndirect.shader.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-blade GPU record. 20 B: float3 posWS (12) + uint packedYawScale (4) + uint hash (4).
    /// packedYawScale: hi16 = yaw over [0,360°), lo16 = scale over [0, ScaleMax]. The shader uniform
    /// <c>_ScaleMax2</c> carries ScaleMax for the inverse decode. <c>hash</c> is a deterministic
    /// per-blade decorrelation value (LOD2 jitter / density skip); the GPU reconstructs wind phase
    /// from posWS.xz, never from a stored field (SSOT — no derived columns).
    /// </summary>
    internal struct GpuGrassBladeInstance
    {
        public Vector3 posWS;
        public uint    packedYawScale;
        public uint    hash;
    }

    /// <summary>Per-chunk conservative AABB. 24 B. Empty-cell sentinel: min &gt; max.</summary>
    internal struct GpuGrassChunkAabb
    {
        public Vector3 min;
        public Vector3 max;
    }

    /// <summary>Contiguous slice [start, start+count) into the sorted blade array for one cell. 8 B.</summary>
    internal struct GpuGrassChunkRange
    {
        public uint start;
        public uint count;
    }

    /// <summary>
    /// Standalone chunked blade buffer for the GPUGrass GPU tier. Partitions a baked placement
    /// (<see cref="GpuGrassBakeData"/> parallel arrays) into a grid of cells via a counting sort, builds
    /// one conservative AABB + index range per cell, and uploads three <see cref="GraphicsBuffer"/>s the
    /// cull compute + indirect shader read.
    ///
    /// This is the WorldPainter-free analogue of <c>ChunkedBladeBuffer</c>: it consumes plain
    /// position/yaw/scale arrays (terrain-baked, world-space, blades upright) instead of a
    /// <c>GrassScatterResult</c>, and only emits the non-oriented (yaw-only) hash slot — terrain grass
    /// roots are world-up, so the oriented octahedral-normal path is not needed.
    /// </summary>
    internal sealed class GpuGrassChunkedBuffer : IDisposable
    {
        // GPU struct strides (bytes) — explicit constants (no Marshal.SizeOf padding surprises).
        private const int BLADE_STRIDE = 20;
        private const int AABB_STRIDE  = 24;
        private const int RANGE_STRIDE =  8;

        private const float YAW_ENCODE_SCALE = 65535f / 360f;

        private GraphicsBuffer? bladeBuf;
        private GraphicsBuffer? aabbBuf;
        private GraphicsBuffer? rangeBuf;

        /// <summary>Number of grid cells along X.</summary>
        public int GridX { get; private set; }

        /// <summary>Number of grid cells along Z.</summary>
        public int GridZ { get; private set; }

        /// <summary>Total grid cells = GridX × GridZ.</summary>
        public int TotalChunks { get; private set; }

        /// <summary>Total uploaded blades.</summary>
        public int TotalBlades { get; private set; }

        /// <summary>Scale-decode upper bound (= ScaleRange.y) bound to the shader as <c>_ScaleMax2</c>.</summary>
        public float ScaleMax2 { get; private set; } = 1f;

        public GraphicsBuffer? BladeBuffer => this.bladeBuf;
        public GraphicsBuffer? AabbBuffer  => this.aabbBuf;
        public GraphicsBuffer? RangeBuffer => this.rangeBuf;

        /// <summary>
        /// Partitions <paramref name="bake"/> into chunked GPU buffers. Releases any prior buffers first.
        /// </summary>
        /// <param name="bake">Baked placement (world-space positions, per-blade yaw + uniform scale).</param>
        /// <param name="scaleMax">Scale-encode upper bound (ScatterRange.y). ≤0 falls back to 1.</param>
        /// <param name="bladeReachY">Vertical AABB inflation = maxBladeHeight·scaleMax + bend headroom.</param>
        /// <param name="lateralPad">Lateral AABB inflation = scaleMax + bend headroom.</param>
        /// <param name="chunkSize">World-space XZ cell size in metres (≥1).</param>
        public void Build(GpuGrassBakeData bake, float scaleMax, float bladeReachY, float lateralPad, int chunkSize)
        {
            if (bake == null) throw new ArgumentNullException(nameof(bake));
            this.Dispose();

            PartitionResult r = Partition(
                bake.Positions, bake.Yaws, bake.Scales, bake.InstanceCount, bake.WorldBounds,
                scaleMax, bladeReachY, lateralPad, chunkSize);

            this.GridX = r.GridX; this.GridZ = r.GridZ;
            this.TotalChunks = r.TotalChunks; this.TotalBlades = r.TotalBlades;
            this.ScaleMax2 = r.ScaleMax2;

            if (r.TotalBlades <= 0)
                return; // empty field — no GPU buffers; renderer skips Submit when TotalChunks/blades are 0.

            // ── Upload ───────────────────────────────────────────────────────
            this.bladeBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, r.TotalBlades, BLADE_STRIDE);
            this.bladeBuf.SetData(r.Blades);
            this.aabbBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, r.TotalChunks, AABB_STRIDE);
            this.aabbBuf.SetData(r.Aabbs);
            this.rangeBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, r.TotalChunks, RANGE_STRIDE);
            this.rangeBuf.SetData(r.Ranges);
        }

        /// <summary>Pure CPU partition output — sorted blade/aabb/range arrays + grid metadata. No GPU.</summary>
        internal readonly struct PartitionResult
        {
            public readonly GpuGrassBladeInstance[] Blades;
            public readonly GpuGrassChunkAabb[]     Aabbs;
            public readonly GpuGrassChunkRange[]    Ranges;
            public readonly int   GridX, GridZ, TotalChunks, TotalBlades;
            public readonly float ScaleMax2;

            public PartitionResult(GpuGrassBladeInstance[] blades, GpuGrassChunkAabb[] aabbs,
                GpuGrassChunkRange[] ranges, int gridX, int gridZ, int totalChunks, int totalBlades, float scaleMax2)
            {
                this.Blades = blades; this.Aabbs = aabbs; this.Ranges = ranges;
                this.GridX = gridX; this.GridZ = gridZ; this.TotalChunks = totalChunks;
                this.TotalBlades = totalBlades; this.ScaleMax2 = scaleMax2;
            }
        }

        /// <summary>
        /// Counting-sort partition of <paramref name="positions"/> into a grid of cells: builds the sorted
        /// blade array (with packed yaw/scale + decorrelation hash), one conservative inflated AABB per
        /// cell, and one index range per cell. Pure CPU — allocates no <see cref="GraphicsBuffer"/>, so it
        /// is unit-testable without a GPU context. <see cref="Build"/> wraps this + the 3× upload.
        /// </summary>
        internal static PartitionResult Partition(
            Vector3[] positions, float[]? yaws, float[]? scales, int total, Bounds worldBounds,
            float scaleMax, float bladeReachY, float lateralPad, int chunkSize)
        {
            float scaleMax2 = scaleMax > 0f ? scaleMax : 1f;
            float encodeScaleScale = 65535f / scaleMax2;
            if (chunkSize < 1) chunkSize = 1;

            // Grid spans the baked field AABB (XZ). Min corner = bounds.min; ceil so the last strip is covered.
            Vector2 minXZ = new(worldBounds.min.x, worldBounds.min.z);
            float   sizeX = Mathf.Max(worldBounds.size.x, 1e-3f);
            float   sizeZ = Mathf.Max(worldBounds.size.z, 1e-3f);

            int gridX       = Mathf.Max(1, Mathf.CeilToInt(sizeX / chunkSize));
            int gridZ       = Mathf.Max(1, Mathf.CeilToInt(sizeZ / chunkSize));
            int totalChunks = gridX * gridZ;

            if (total <= 0)
                return new PartitionResult(
                    Array.Empty<GpuGrassBladeInstance>(), Array.Empty<GpuGrassChunkAabb>(),
                    Array.Empty<GpuGrassChunkRange>(), gridX, gridZ, totalChunks, 0, scaleMax2);

            // ── Counting sort pass 1: blade → cell, per-cell counts ──────────
            int[] cellCounts = new int[totalChunks];
            int[] bladeCell  = new int[total];
            for (int i = 0; i < total; ++i)
            {
                Vector3 p = positions[i];
                int cx = Mathf.Clamp((int)((p.x - minXZ.x) / chunkSize), 0, gridX - 1);
                int cz = Mathf.Clamp((int)((p.z - minXZ.y) / chunkSize), 0, gridZ - 1);
                int cell = cz * gridX + cx;
                bladeCell[i] = cell;
                cellCounts[cell]++;
            }

            // ── Prefix sum → per-cell start offsets ──────────────────────────
            int[] cellStart = new int[totalChunks];
            int   running   = 0;
            for (int c = 0; c < totalChunks; ++c) { cellStart[c] = running; running += cellCounts[c]; }

            // ── Pass 2: scatter into sorted output; accumulate per-cell AABB ──
            var bladeOut = new GpuGrassBladeInstance[total];
            var aabbOut  = new GpuGrassChunkAabb[totalChunks];
            var rangeOut = new GpuGrassChunkRange[totalChunks];

            for (int c = 0; c < totalChunks; ++c)
                aabbOut[c] = new GpuGrassChunkAabb
                {
                    min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                    max = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                };

            int[] writeCursor = new int[totalChunks];
            Array.Copy(cellStart, writeCursor, totalChunks);

            for (int i = 0; i < total; ++i)
            {
                int cell = bladeCell[i];
                int outI = writeCursor[cell]++;

                Vector3 pos   = positions[i];
                float   yaw   = (yaws   != null && i < yaws.Length)   ? yaws[i]   : 0f;
                float   scale = (scales != null && i < scales.Length) ? scales[i] : 1f;

                uint yawQ   = (uint)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) * YAW_ENCODE_SCALE);
                uint scaleQ = (uint)Mathf.RoundToInt(Mathf.Clamp(scale, 0f, scaleMax2) * encodeScaleScale);
                uint packed = ((yawQ & 0xFFFFu) << 16) | (scaleQ & 0xFFFFu);

                bladeOut[outI] = new GpuGrassBladeInstance
                {
                    posWS          = pos,
                    packedYawScale = packed,
                    hash           = XorShift32((uint)(i + 1)),
                };

                ref GpuGrassChunkAabb aabb = ref aabbOut[cell];
                if (pos.x < aabb.min.x) aabb.min.x = pos.x;
                if (pos.y < aabb.min.y) aabb.min.y = pos.y;
                if (pos.z < aabb.min.z) aabb.min.z = pos.z;
                if (pos.x > aabb.max.x) aabb.max.x = pos.x;
                if (pos.y > aabb.max.y) aabb.max.y = pos.y;
                if (pos.z > aabb.max.z) aabb.max.z = pos.z;
            }

            // ── Inflate non-empty AABBs by blade reach; fill ranges ──────────
            for (int c = 0; c < totalChunks; ++c)
            {
                int cnt = cellCounts[c];
                if (cnt == 0) { rangeOut[c] = new GpuGrassChunkRange { start = 0, count = 0 }; continue; }

                ref GpuGrassChunkAabb aabb = ref aabbOut[c];
                aabb.max.y += bladeReachY;
                aabb.min.x -= lateralPad; aabb.max.x += lateralPad;
                aabb.min.z -= lateralPad; aabb.max.z += lateralPad;

                rangeOut[c] = new GpuGrassChunkRange { start = (uint)cellStart[c], count = (uint)cnt };
            }

            return new PartitionResult(bladeOut, aabbOut, rangeOut, gridX, gridZ, totalChunks, total, scaleMax2);
        }

        /// <summary>Releases the three GPU buffers. Unity-null-safe.</summary>
        public void Dispose()
        {
            if (this.bladeBuf != null) { this.bladeBuf.Release(); this.bladeBuf = null; }
            if (this.aabbBuf  != null) { this.aabbBuf.Release();  this.aabbBuf  = null; }
            if (this.rangeBuf != null) { this.rangeBuf.Release(); this.rangeBuf = null; }
        }

        /// <summary>Deterministic non-zero Xorshift32 for per-blade decorrelation.</summary>
        private static uint XorShift32(uint x)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return x;
        }
    }
}
