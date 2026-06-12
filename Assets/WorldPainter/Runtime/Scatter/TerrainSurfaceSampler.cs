#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// <see cref="ISurfaceSampler"/> implementation that reads directly from a Unity
    /// <see cref="Terrain"/>'s <see cref="TerrainData"/> — height, normal, holes, and
    /// splat weights — without casting any physics rays.
    ///
    /// UV mapping (world → terrain-local normalized [0,1]):
    ///   u = (worldX - terrainPos.x) / size.x
    ///   v = (worldZ - terrainPos.z) / size.z
    /// Any UV outside [0,1] returns <c>false</c> (position is off this terrain tile).
    ///
    /// Hole check: maps the UV to hole-texture integer coords
    ///   holeX = clamp(floor(u * holesResolution), 0, holesResolution-1)
    ///   holeZ = clamp(floor(v * holesResolution), 0, holesResolution-1)
    /// <c>TerrainData.IsHole(holeX, holeZ)</c> returns true when the texel IS a hole;
    /// <c>TrySample</c> returns <c>false</c> in that case (scatter caller skips the candidate).
    ///
    /// Alphamaps (splat weights) are read via <c>TerrainData.GetAlphamaps(x, z, 1, 1)</c>
    /// per sample. Caching the full alphamap array in the constructor is intentionally avoided
    /// here because it may be large (N × W × H floats); individual reads via GetAlphamaps(1,1)
    /// are fast enough for scatter build-time use. Phase 4 may introduce a bulk-cache path.
    ///
    /// NOTE: Terrain holes and splat weights are populated now (Phase 1), but the slope filter
    /// is the only mask applied during scatter. Splat-mask-based filtering is Phase 4.
    /// </summary>
    public sealed class TerrainSurfaceSampler : ISurfaceSampler
    {
        private readonly Terrain terrain;
        private readonly TerrainData terrainData;
        private readonly Vector3 terrainPos;
        private readonly Vector3 terrainSize;
        private readonly int holesResolution;
        private readonly int alphamapWidth;
        private readonly int alphamapHeight;
        private readonly int alphamapLayers;
        private readonly bool hasAlphamaps;
        private readonly bool hasHoles;

        /// <summary>
        /// World-space XZ size of the bound terrain (terrainData.size.xz). The scatter field uses this
        /// as the field rect size when a terrain is bound — placement covers the whole terrain tile and
        /// the layer's manual <c>FieldBounds</c> is ignored.
        /// </summary>
        public Vector2 TerrainSizeXZ => new Vector2(this.terrainSize.x, this.terrainSize.z);

        /// <summary>
        /// Creates a terrain-based surface sampler.
        /// </summary>
        /// <param name="terrain">
        /// The Unity Terrain to sample. Must not be null and must have a valid
        /// <see cref="TerrainData"/> assigned.
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when <paramref name="terrain"/> is null or its
        /// <see cref="TerrainData"/> is null.
        /// </exception>
        public TerrainSurfaceSampler(Terrain terrain)
        {
            if (terrain == null) throw new System.ArgumentNullException(nameof(terrain));

            TerrainData? data = terrain.terrainData;
            if (data == null)
                throw new System.ArgumentNullException(nameof(terrain),
                    "Terrain.terrainData is null — assign a TerrainData asset to the Terrain component.");

            this.terrain = terrain;
            this.terrainData = data;
            this.terrainPos = terrain.transform.position;
            this.terrainSize = data.size;

            // Holes resolution (Unity 2021+ holesResolution; falls back to heightmapResolution).
            this.holesResolution = data.holesResolution;
            this.hasHoles = this.holesResolution > 0;

            // Alphamap dimensions.
            this.alphamapWidth = data.alphamapWidth;
            this.alphamapHeight = data.alphamapHeight;
            this.alphamapLayers = data.alphamapLayers;
            this.hasAlphamaps = this.alphamapLayers > 0 &&
                                this.alphamapWidth > 0 &&
                                this.alphamapHeight > 0;
        }

        /// <inheritdoc/>
        public bool TrySample(float worldX, float worldZ, out SurfaceHit hit)
        {
            // ── 1. UV mapping ────────────────────────────────────────────────
            float u = (worldX - this.terrainPos.x) / this.terrainSize.x;
            float v = (worldZ - this.terrainPos.z) / this.terrainSize.z;

            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                hit = default;
                return false;
            }

            // ── 2. Hole check ────────────────────────────────────────────────
            if (this.hasHoles)
            {
                int holeX = Mathf.Clamp(Mathf.FloorToInt(u * this.holesResolution), 0, this.holesResolution - 1);
                int holeZ = Mathf.Clamp(Mathf.FloorToInt(v * this.holesResolution), 0, this.holesResolution - 1);
                if (this.terrainData.IsHole(holeX, holeZ))
                {
                    hit = default;
                    return false;
                }
            }

            // ── 3. Height + Normal ────────────────────────────────────────────
            // GetInterpolatedHeight/Normal use UV in [0,1]; Y is terrain-data-local (add terrain.pos.y).
            float heightLocal = this.terrainData.GetInterpolatedHeight(u, v);
            float worldY = this.terrainPos.y + heightLocal;

            Vector3 normal = this.terrainData.GetInterpolatedNormal(u, v);
            float slopeDeg = Vector3.Angle(normal, Vector3.up);

            // ── 4. Splat weights ──────────────────────────────────────────────
            // GetAlphamaps(x, z, width, height) where x/z are integer alphamap coords.
            // Returns a [height, width, layers] float array; [0, 0, layer] holds the weight per layer.
            float[]? splatWeights = null;
            if (this.hasAlphamaps)
            {
                // Map UV → alphamap integer coords; clamp to valid range.
                // alphamapWidth covers [0, size.x] in the X axis (u direction),
                // alphamapHeight covers [0, size.z] in the Z axis (v direction).
                int ax = Mathf.Clamp(Mathf.FloorToInt(u * this.alphamapWidth), 0, this.alphamapWidth - 1);
                int az = Mathf.Clamp(Mathf.FloorToInt(v * this.alphamapHeight), 0, this.alphamapHeight - 1);

                // GetAlphamaps returns [sampleHeight, sampleWidth, layers] — for a 1×1 read: [0,0,layer].
                float[,,] maps = this.terrainData.GetAlphamaps(ax, az, 1, 1);
                splatWeights = new float[this.alphamapLayers];
                for (int layer = 0; layer < this.alphamapLayers; ++layer)
                    splatWeights[layer] = maps[0, 0, layer];
            }

            hit.Y = worldY;
            hit.Normal = normal;
            hit.SlopeDeg = slopeDeg;
            hit.SplatWeights = splatWeights;
            return true;
        }
    }
}
