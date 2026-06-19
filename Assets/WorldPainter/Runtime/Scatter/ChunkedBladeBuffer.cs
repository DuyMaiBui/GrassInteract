#nullable enable
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    // ──────────────────────────────────────────────────────────────────────────
    // Blittable GPU structs — only float/uint fields; no managed refs, no bool.
    // Safe for GraphicsBuffer.SetData under allowUnsafeCode:false.
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-blade GPU record uploaded once into the BladeInstance structured buffer.
    /// Total size: 20 bytes (float3 posWS = 12B, uint packedYawScale = 4B, uint hash = 4B).
    ///
    /// <b>packedYawScale packing:</b><br/>
    ///   high 16 bits = yaw quantised to [0..65535] over [0..360) degrees (step ≈ 0.0055°).<br/>
    ///   low  16 bits = uniform scale quantised to [0..65535] over [0 .. ScaleRange.y]
    ///                  (SSOT: GrassScatter clamps blade scale to ScaleRange.y, so this is exact).<br/>
    /// Inverse unpack (mirror this in the HLSL vertex shader — Phase 5):<br/>
    ///   <c>float yaw   = (packedYawScale >> 16) / 65535.0 * 360.0;</c><br/>
    ///   <c>float scale = (packedYawScale &amp; 0xFFFF) / 65535.0 * scaleMax2;</c><br/>
    ///   where scaleMax2 = ScaleRange.y (passed as the _ScaleMax2 shader uniform).
    ///
    /// <b>hash:</b> deterministic per-blade random uint derived from the blade's flat sort index.
    /// Used for per-blade decorrelation (LOD2 jitter, color variation) in Phase 5+.<br/>
    /// NOTE: the GPU vertex shader DOES NOT store wind phase here. Phase is RECONSTRUCTED on the GPU
    /// from posWS.xz using the same spatial-frequency vector the C# simulator uses:<br/>
    ///   <c>phase = (posWS.x * 0.37 + posWS.z * 0.21) * windNoiseScale * TWO_PI</c><br/>
    ///   (mirrors GrassBendSimulator.PHASE_FREQ = (0.37, 0.21) — cite: GrassBendSimulator.cs line 26).
    ///   Storing the baked phase would be a derived-field violation (SSOT: posWS + windNoiseScale uniform).
    /// </summary>
    public struct BladeInstance
    {
        public Vector3 posWS;
        public uint packedYawScale;
        public uint hash;
    }

    /// <summary>
    /// Per-chunk conservative AABB (one per grid cell) uploaded once into the ChunkAabb structured buffer.
    /// Expanded by blade-height × maxScale + BendHeadroom in Y and maxScale + BendHeadroom laterally,
    /// mirroring GrassScatter.BuildFieldBounds so no bent blade is wrongly frustum-culled.
    /// Empty cell sentinel: min > max (Compute A rejects trivially by testing min.x > max.x).
    /// </summary>
    public struct ChunkAabb
    {
        public Vector3 min;
        public Vector3 max;
    }

    /// <summary>
    /// Contiguous slice [start, start+count) into the sorted BladeInstance array for one grid cell.
    /// Empty cell: start = 0, count = 0.
    /// </summary>
    public struct ChunkRange
    {
        public uint start;
        public uint count;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Baker + GPU buffer holder
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bakes a <see cref="GrassScatterResult"/> into GPU-ready chunked buffers: a compact
    /// <see cref="BladeInstance"/> array sorted by grid cell, one <see cref="ChunkAabb"/> per cell,
    /// and a <see cref="ChunkRange"/> per cell mapping into the blade array. Uploaded ONCE via
    /// <c>GraphicsBuffer.SetData</c>; the per-frame GPU pipeline only reads these.
    ///
    /// Call <see cref="Bake"/> from edit-mode build steps (deterministic, no Play-mode dependency).
    /// Call <see cref="Dispose"/> to release the three <see cref="GraphicsBuffer"/>s when done.
    /// </summary>
    public sealed class ChunkedBladeBuffer : IDisposable
    {
        // ── GPU struct strides (bytes) ────────────────────────────────────────
        // Explicit constants rather than Marshal.SizeOf to avoid managed-side padding surprises.
        // BladeInstance: float3(12) + uint(4) + uint(4) = 20 B (matches HLSL layout).
        // ChunkAabb:     float3(12) + float3(12)         = 24 B.
        // ChunkRange:    uint(4)    + uint(4)             =  8 B.
        private const int BLADE_STRIDE = 20;
        private const int AABB_STRIDE  = 24;
        private const int RANGE_STRIDE =  8;

        // ── Packing constants ─────────────────────────────────────────────────
        private const float YAW_ENCODE_SCALE = 65535f / 360f;
        // Scale is encoded over [0, ScaleRange.y]. SSOT: the scatter clamps blade scale to ScaleRange.y
        // so the encode range top exactly equals the maximum possible blade scale — full 16-bit precision.
        // The shader uniform _ScaleMax2 is set to this value (name retained for backward compat).
        private float scaleMax2; // = ScaleRange.y (NOT ×2 — the ×2 wording in older comments was wrong)

        // ── CPU-side baked arrays (kept for CPU accessors + ValidatePartition) ─
        private BladeInstance[]? bladeInstances;
        private ChunkAabb[]? chunkAabbs;
        private ChunkRange[]? chunkRanges;

        // ── GPU buffers ────────────────────────────────────────────────────────
        private GraphicsBuffer? bladeBuf;
        private GraphicsBuffer? aabbBuf;
        private GraphicsBuffer? rangeBuf;

        // ── Grid metadata ──────────────────────────────────────────────────────
        /// <summary>Number of grid cells along the X axis.</summary>
        public int GridX { get; private set; }

        /// <summary>Number of grid cells along the Z axis.</summary>
        public int GridZ { get; private set; }

        /// <summary>World-space XZ cell size in metres.</summary>
        public int ChunkSize { get; private set; }

        /// <summary>Total grid cells = GridX × GridZ.</summary>
        public int TotalChunks { get; private set; }

        /// <summary>Total blades uploaded (== GrassScatterResult.TotalCount at bake).</summary>
        public int TotalBlades { get; private set; }

        /// <summary>Scale encode upper-bound = ScaleRange.y. Needed by the GPU VS to decode packedYawScale.
        /// Range is [0, ScaleRange.y] — no ×2 multiplier; blades are clamped to ScaleRange.y by scatter.</summary>
        public float ScaleMax2 => this.scaleMax2;

        // ── GPU buffer accessors ───────────────────────────────────────────────
        /// <summary>Structured <see cref="GraphicsBuffer"/> of <see cref="BladeInstance"/> (stride 20 B).</summary>
        public GraphicsBuffer? BladeBuffer => this.bladeBuf;

        /// <summary>Structured <see cref="GraphicsBuffer"/> of <see cref="ChunkAabb"/> (stride 24 B).</summary>
        public GraphicsBuffer? AabbBuffer => this.aabbBuf;

        /// <summary>Structured <see cref="GraphicsBuffer"/> of <see cref="ChunkRange"/> (stride 8 B).</summary>
        public GraphicsBuffer? RangeBuffer => this.rangeBuf;

        // ── CPU array accessors (read-only views for validation + CPU consumers) ─
        /// <summary>CPU-side copy of the baked blade array. Null before <see cref="Bake"/> is called.</summary>
        public BladeInstance[]? BladeInstances => this.bladeInstances;

        /// <summary>CPU-side copy of per-chunk AABBs. Null before bake.</summary>
        public ChunkAabb[]? ChunkAabbs => this.chunkAabbs;

        /// <summary>CPU-side copy of per-chunk ranges. Null before bake.</summary>
        public ChunkRange[]? ChunkRanges => this.chunkRanges;

        // ─────────────────────────────────────────────────────────────────────
        // Bake
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes a scatter result into sorted, chunk-partitioned GPU buffers. Callable in edit mode.
        /// Disposes any previously baked buffers before re-baking.
        /// </summary>
        /// <param name="scatter">Scatter output from GrassScatter.Build.</param>
        /// <param name="layer">Scatter layer — reads MaxBladeHeight, BendHeadroom, and ChunkSize.</param>
        /// <param name="origin">Field world-space origin (ScatterField transform position).</param>
        /// <param name="fieldBoundsXZ">ScatterLayer.FieldBounds (world-space XZ extents).</param>
        /// <param name="scaleRangeMax">
        /// SSOT scale encode upper-bound = ScatterLayer.ScaleRange.y. GrassScatter clamps blade scale to
        /// this value, so encoding over [0, scaleRangeMax] uses the full 16-bit range with no headroom
        /// waste. Pass 0 or negative to fall back to 1f.
        /// </param>
        /// <param name="oriented">
        /// When true (layer.IsOriented), slot2 encodes octNormal(hi16) | pitch8 | roll8 from the baked
        /// surface normal and the per-instance rotation stored in the scatter matrix.
        /// When false (default), slot2 = decorrelation hash (UNCHANGED — byte-stable).
        /// </param>
        /// <param name="chunkSizeOverride">
        /// Override the cell size in metres; pass 0 to use <paramref name="config"/>.ChunkSize.
        /// </param>
        public void Bake(GrassScatterResult scatter, ScatterLayer layer,
            Vector3 origin, Vector2 fieldBoundsXZ, float scaleRangeMax,
            bool oriented = false, int chunkSizeOverride = 0)
        {
            // Release any prior buffers.
            this.Dispose();

            int cellSize = chunkSizeOverride > 0 ? chunkSizeOverride : layer.Bounds.ChunkSize;
            if (cellSize < 1) cellSize = 1;

            // Field min corner — SSOT: GrassFieldSpace uses this identical formula.
            var minXZ = new Vector2(origin.x - fieldBoundsXZ.x * 0.5f,
                                    origin.z - fieldBoundsXZ.y * 0.5f);

            // Grid dimensions (ceil so the last partial strip is covered).
            int gridX = Mathf.Max(1, Mathf.CeilToInt(fieldBoundsXZ.x / cellSize));
            int gridZ = Mathf.Max(1, Mathf.CeilToInt(fieldBoundsXZ.y / cellSize));
            int totalChunks = gridX * gridZ;
            int totalBlades = scatter.TotalCount;

            this.GridX = gridX;
            this.GridZ = gridZ;
            this.ChunkSize = cellSize;
            this.TotalChunks = totalChunks;
            this.TotalBlades = totalBlades;

            // FIX 6: SSOT scale encode upper-bound = scaleRangeMax (= ScatterLayer.ScaleRange.y).
            // GrassScatter clamps blade scale to ScaleRange.y so encoding over [0, ScaleRange.y]
            // uses the full 16-bit precision. No ×2 multiplier — the old ×2 wasted half the range.
            float maxScale = scaleRangeMax > 0f ? scaleRangeMax : 1f;
            this.scaleMax2 = maxScale; // = ScaleRange.y; the _ScaleMax2 uniform name is kept for shader compat
            float encodeScaleScale = 65535f / this.scaleMax2;

            // Headroom — mirror GrassScatter.BuildFieldBounds EXACTLY for AABB parity.
            float bladeReachY = layer.Bounds.MaxBladeHeight * maxScale + layer.Bounds.BendHeadroom;
            float lateralPad  = maxScale + layer.Bounds.BendHeadroom;

            // ── Step 1: count blades per cell (counting sort, pass 1) ────────
            int[] cellCounts = new int[totalChunks];
            int[] bladeCell  = new int[totalBlades];

            int flatIdx = 0;
            for (int b = 0; b < scatter.BaseSlabs.Length; ++b)
            {
                int count = scatter.SlabCounts[b];
                Vector3[] posSlab = scatter.BasePositionSlabs[b];
                for (int k = 0; k < count; ++k)
                {
                    Vector3 pos = posSlab[k];
                    // Cell coordinate — clamp to valid range so blades at the exact far edge stay in-bounds.
                    int cx = Mathf.Clamp((int)((pos.x - minXZ.x) / cellSize), 0, gridX - 1);
                    int cz = Mathf.Clamp((int)((pos.z - minXZ.y) / cellSize), 0, gridZ - 1);
                    int cell = cz * gridX + cx;
                    bladeCell[flatIdx] = cell;
                    cellCounts[cell]++;
                    flatIdx++;
                }
            }

            // ── Step 2: prefix-sum → per-cell start offsets (counting sort, pass 2) ──
            int[] cellStart  = new int[totalChunks];
            int   running    = 0;
            for (int c = 0; c < totalChunks; ++c)
            {
                cellStart[c] = running;
                running += cellCounts[c];
            }

            // ── Step 3: scatter blades into sorted output, build per-cell AABB ──────
            var bladeOut = new BladeInstance[totalBlades];
            var aabbOut  = new ChunkAabb[totalChunks];
            var rangeOut = new ChunkRange[totalChunks];

            // Initialise AABBs as collapsed sentinels (min > max → trivially rejected by Compute A).
            for (int c = 0; c < totalChunks; ++c)
            {
                aabbOut[c] = new ChunkAabb
                {
                    min = new Vector3(float.MaxValue,  float.MaxValue,  float.MaxValue),
                    max = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                };
            }

            // Write-cursors per cell (for in-order fill during the scatter pass).
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
                    int       cell = bladeCell[flatIdx];
                    int       outI = writeCursor[cell]++;
                    Matrix4x4 m    = matSlab[k];
                    Vector3   pos  = posSlab[k]; // ground-snapped base world position
                    Vector3   nrm  = nrmSlab[k]; // surface normal (Vector3.up for non-oriented)

                    // Decompose base matrix — mirror GrassBendSimulator EXACTLY for pose parity.
                    float yaw   = m.rotation.eulerAngles.y; // 0..360
                    float scale = m.lossyScale.x;           // uniform scale

                    // Pack yaw (high 16) + scale (low 16) into one uint.
                    uint yawQ   = (uint)Mathf.RoundToInt(Mathf.Clamp(yaw, 0f, 360f) * YAW_ENCODE_SCALE);
                    uint scaleQ = (uint)Mathf.RoundToInt(Mathf.Clamp(scale, 0f, this.scaleMax2) * encodeScaleScale);
                    uint packed = ((yawQ & 0xFFFFu) << 16) | (scaleQ & 0xFFFFu);

                    // Slot2: oriented mode → octNormal(hi16) | pitch8(b8) | roll8(b0).
                    //        legacy mode   → XorShift32 hash (byte-identical to prior behaviour).
                    uint slot2;
                    if (oriented)
                    {
                        // Decompose the per-instance rotation to get pitch and roll.
                        // The matrix stores align * Euler(pitch, yaw, roll) * offset.
                        // We extract the euler angles and re-derive pitch / roll.
                        Vector3 euler = m.rotation.eulerAngles;
                        // eulerAngles.x = pitch, .z = roll (Unity ZXY convention maps to x=pitch, z=roll).
                        float pitchDeg = euler.x > 180f ? euler.x - 360f : euler.x; // remap [0,360]→[-180,180]
                        float rollDeg  = euler.z > 180f ? euler.z - 360f : euler.z;

                        slot2 = PackOrientedSlot2(nrm, pitchDeg, rollDeg);
                    }
                    else
                    {
                        // Per-blade deterministic random hash from flat index (unchanged — byte-stable).
                        slot2 = XorShift32((uint)(flatIdx + 1));
                    }

                    bladeOut[outI] = new BladeInstance
                    {
                        posWS          = pos,
                        packedYawScale = packed,
                        hash           = slot2,
                    };

                    // Expand per-cell AABB to include this blade's base position.
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

            // Expand AABBs by headroom; fill ranges; leave empty cells as sentinels.
            for (int c = 0; c < totalChunks; ++c)
            {
                int cnt = cellCounts[c];
                if (cnt == 0)
                {
                    // Sentinel already set above; range = {0, 0}.
                    rangeOut[c] = new ChunkRange { start = 0, count = 0 };
                    continue;
                }

                ref ChunkAabb aabb = ref aabbOut[c];
                // Y: grow upward by bladeReachY (bent tips); lateralPad for XZ sway footprint.
                aabb.max.y += bladeReachY;
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
            this.bladeInstances = bladeOut;
            this.chunkAabbs     = aabbOut;
            this.chunkRanges    = rangeOut;

            // ── Step 5: upload to GPU ─────────────────────────────────────────
            if (totalBlades > 0)
            {
                // BladeInstance stride = 20 B (float3 posWS=12, uint packedYawScale=4, uint hash=4).
                this.bladeBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    totalBlades, BLADE_STRIDE);
                this.bladeBuf.SetData(bladeOut);
            }

            if (totalChunks > 0)
            {
                // ChunkAabb stride = 24 B (float3 min=12, float3 max=12).
                this.aabbBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    totalChunks, AABB_STRIDE);
                this.aabbBuf.SetData(aabbOut);

                // ChunkRange stride = 8 B (uint start=4, uint count=4).
                this.rangeBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    totalChunks, RANGE_STRIDE);
                this.rangeBuf.SetData(rangeOut);
            }

            WpLog.Log($"[ChunkedBladeBuffer] Baked: TotalBlades={totalBlades}, " +
                      $"Grid={gridX}×{gridZ} ({totalChunks} chunks), CellSize={cellSize}m, " +
                      $"ScaleRangeMax={maxScale:F3} (encode range [0,{this.scaleMax2:F3}])");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Validation
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates the baked partition: every blade is accounted for, ranges are contiguous and
        /// non-overlapping covering [0, TotalBlades), and the union of non-empty chunk AABBs
        /// contains the scatter's WorldBounds XZ. Call after <see cref="Bake"/>.
        /// </summary>
        /// <param name="report">Human-readable verdict (printed by the verification gate).</param>
        /// <returns>True iff all checks pass.</returns>
        public bool ValidatePartition(GrassScatterResult scatter, out string report)
        {
            var sb = new StringBuilder();
            bool pass = true;

            if (this.bladeInstances == null || this.chunkRanges == null || this.chunkAabbs == null)
            {
                report = "FAIL: Bake has not been called.";
                return false;
            }

            int total = this.TotalBlades;
            sb.AppendLine($"TotalBlades={total}, TotalChunks={this.TotalChunks}, " +
                          $"Grid={this.GridX}×{this.GridZ}, CellSize={this.ChunkSize}m");

            // Check 1: blade count matches scatter.
            if (total != scatter.TotalCount)
            {
                sb.AppendLine($"FAIL: TotalBlades={total} != scatter.TotalCount={scatter.TotalCount}");
                pass = false;
            }
            else
            {
                sb.AppendLine($"  [PASS] BladeCount == scatter.TotalCount ({total})");
            }

            // Check 2: sum of ranges == total AND ranges are contiguous covering [0, total).
            int sumCount = 0;
            int expectedStart = 0;
            bool contiguous = true;
            for (int c = 0; c < this.TotalChunks; ++c)
            {
                ChunkRange r = this.chunkRanges[c];
                if (r.count == 0) continue; // empty cell — skip

                if ((int)r.start != expectedStart)
                {
                    sb.AppendLine($"FAIL: chunk[{c}] start={r.start} expected {expectedStart} (gap/overlap)");
                    contiguous = false;
                    pass = false;
                }
                expectedStart = (int)(r.start + r.count);
                sumCount += (int)r.count;
            }
            if (sumCount != total)
            {
                sb.AppendLine($"FAIL: Σ ChunkRange.count={sumCount} != TotalBlades={total}");
                pass = false;
            }
            else if (contiguous)
            {
                sb.AppendLine($"  [PASS] Partition contiguous, non-overlapping, covers [0,{total})");
            }

            // Check 3: union of non-empty chunk AABBs contains scatter.WorldBounds XZ.
            Bounds wb = scatter.WorldBounds;
            float wbMinX = wb.min.x;
            float wbMaxX = wb.max.x;
            float wbMinZ = wb.min.z;
            float wbMaxZ = wb.max.z;

            float unionMinX = float.MaxValue;
            float unionMaxX = float.MinValue;
            float unionMinZ = float.MaxValue;
            float unionMaxZ = float.MinValue;

            for (int c = 0; c < this.TotalChunks; ++c)
            {
                if (this.chunkRanges[c].count == 0) continue;
                ChunkAabb a = this.chunkAabbs[c];
                if (a.min.x < unionMinX) unionMinX = a.min.x;
                if (a.max.x > unionMaxX) unionMaxX = a.max.x;
                if (a.min.z < unionMinZ) unionMinZ = a.min.z;
                if (a.max.z > unionMaxZ) unionMaxZ = a.max.z;
            }

            bool aabbCovers = unionMinX <= wbMinX && unionMaxX >= wbMaxX &&
                              unionMinZ <= wbMinZ && unionMaxZ >= wbMaxZ;
            if (!aabbCovers)
            {
                sb.AppendLine($"FAIL: chunk AABB union XZ [{unionMinX:F2},{unionMaxX:F2}] " +
                              $"[{unionMinZ:F2},{unionMaxZ:F2}] does NOT contain worldBounds XZ " +
                              $"[{wbMinX:F2},{wbMaxX:F2}] [{wbMinZ:F2},{wbMaxZ:F2}]");
                pass = false;
            }
            else
            {
                sb.AppendLine($"  [PASS] AABB union XZ covers scatter WorldBounds XZ");
            }

            // Occupancy stats.
            int minOccupancy = int.MaxValue;
            int maxOccupancy = 0;
            int nonEmptyCells = 0;
            for (int c = 0; c < this.TotalChunks; ++c)
            {
                int cnt = (int)this.chunkRanges[c].count;
                if (cnt == 0) continue;
                nonEmptyCells++;
                if (cnt < minOccupancy) minOccupancy = cnt;
                if (cnt > maxOccupancy) maxOccupancy = cnt;
            }
            sb.AppendLine($"  Non-empty cells={nonEmptyCells}, blades/cell min={minOccupancy} max={maxOccupancy}");

            sb.Insert(0, pass ? "PASS: " : "FAIL: ");
            report = sb.ToString().TrimEnd();
            return pass;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Dispose
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Releases the three GPU <see cref="GraphicsBuffer"/>s. Unity-null-safe.</summary>
        public void Dispose()
        {
            if (this.bladeBuf != null) { this.bladeBuf.Release(); this.bladeBuf = null; }
            if (this.aabbBuf  != null) { this.aabbBuf.Release();  this.aabbBuf  = null; }
            if (this.rangeBuf != null) { this.rangeBuf.Release();  this.rangeBuf = null; }
            this.bladeInstances = null;
            this.chunkAabbs     = null;
            this.chunkRanges    = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────
        // Oriented slot2 packing (Phase 4)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Packs oriented instance data into the 32-bit slot2 field.
        ///
        /// Format (SSOT — mirror this in HLSL for decode):
        ///   bits 31-16 : octahedral-encoded normal (oct-X in hi8, oct-Y in lo8)
        ///   bits 15-8  : pitch in [-90, 90] degrees mapped to [0, 255]
        ///   bits  7-0  : roll  in [-90, 90] degrees mapped to [0, 255]
        ///
        /// Octahedral encoding projects the unit normal onto the L1 unit octahedron then
        /// maps to [0..1] in each component. Each component is stored as an 8-bit unsigned.
        /// pitch/roll are clamped to [-90,90] before quantisation.
        /// </summary>
        internal static uint PackOrientedSlot2(Vector3 normal, float pitchDeg, float rollDeg)
        {
            // Octahedral encode (SNorm → UNorm 0..255 per component).
            Vector2 oct = OctEncode(normal);
            uint octX = (uint)Mathf.RoundToInt(Mathf.Clamp01(oct.x * 0.5f + 0.5f) * 255f);
            uint octY = (uint)Mathf.RoundToInt(Mathf.Clamp01(oct.y * 0.5f + 0.5f) * 255f);

            // Angle quantise: [-90,90] → [0,255].
            uint p8 = AngleTo8Bit(pitchDeg);
            uint r8 = AngleTo8Bit(rollDeg);

            return ((octX & 0xFFu) << 24) | ((octY & 0xFFu) << 16) | ((p8 & 0xFFu) << 8) | (r8 & 0xFFu);
        }

        /// <summary>
        /// Decodes the octahedral normal from the hi16 of an oriented slot2 value.
        /// (Provided for harness / unit-test use; decode lives in HLSL for GPU path.)
        /// </summary>
        internal static Vector3 UnpackOctNormalFromSlot2(uint slot2)
        {
            float octX = ((slot2 >> 24) & 0xFFu) / 255f * 2f - 1f;
            float octY = ((slot2 >> 16) & 0xFFu) / 255f * 2f - 1f;
            return OctDecode(new Vector2(octX, octY));
        }

        /// <summary>Encodes a unit vector to signed [-1,1] oct-map (2-component).</summary>
        private static Vector2 OctEncode(Vector3 n)
        {
            float l1 = Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            if (l1 < 1e-8f) return Vector2.zero;
            float invL1 = 1f / l1;
            float px = n.x * invL1;
            float py = n.z * invL1; // map Y-up → oct-Y from Z component
            if (n.y < 0f)
            {
                // Fold lower hemisphere.
                float oldPx = px;
                px = (1f - Mathf.Abs(py)) * (px >= 0f ? 1f : -1f);
                py = (1f - Mathf.Abs(oldPx)) * (py >= 0f ? 1f : -1f);
            }
            return new Vector2(px, py);
        }

        /// <summary>Decodes a 2-component signed oct-map back to a unit vector (Y-up).</summary>
        private static Vector3 OctDecode(Vector2 oct)
        {
            float px = oct.x;
            float py = oct.y;
            float pz = 1f - Mathf.Abs(px) - Mathf.Abs(py);
            if (pz < 0f)
            {
                float oldPx = px;
                px = (1f - Mathf.Abs(py)) * (px >= 0f ? 1f : -1f);
                py = (1f - Mathf.Abs(oldPx)) * (py >= 0f ? 1f : -1f);
            }
            // Inverse of OctEncode: px→x, pz→y, py→z.
            return new Vector3(px, pz, py).normalized;
        }

        /// <summary>
        /// Quantises an angle in degrees from [-90, 90] to [0, 255] (8-bit unsigned).
        /// Values outside [-90, 90] are clamped. Inverse: <c>angle = (val / 255.0 * 180.0) - 90.0</c>.
        /// </summary>
        private static uint AngleTo8Bit(float angleDeg)
        {
            float clamped = Mathf.Clamp(angleDeg, -90f, 90f);
            return (uint)Mathf.RoundToInt((clamped + 90f) / 180f * 255f);
        }

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
