#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GPUGrass.Editor
{
    /// <summary>
    /// Bakes a Unity built-in Terrain into a <see cref="GpuGrassBakeData"/> placement asset (a single grass
    /// type). Two placement sources, chosen by <see cref="GpuGrassConfig.PlacementSource"/>:
    ///
    /// • <b>DetailLayer</b> — walks every painted detail (grass) layer's density map and rejection-samples
    ///   blade positions weighted by per-cell painted coverage (paint where you want grass).
    /// • <b>TerrainSurface</b> — scatters uniformly over the whole terrain SURFACE mesh at
    ///   <c>TargetDensityPerSqM</c> (no painting required), masked by an altitude band.
    ///
    /// Both paths ground-snap each blade to the terrain height, mask by surface slope, and record per-blade
    /// yaw + uniform scale. Deterministic (seeded by <see cref="GpuGrassConfig.Seed"/>). Editor-bake to a
    /// data asset so mobile pays no runtime <c>GetDetailLayer</c>/scatter cost. Re-run after edits.
    /// </summary>
    public static class GpuGrassBaker
    {
        // TerrainSurface grid: cell size in metres, and a cap so huge terrains don't explode the loop.
        private const float SURFACE_CELL_M  = 2f;
        private const int   MAX_SURFACE_GRID = 512;

        /// <summary>
        /// Bakes <paramref name="terrain"/> into <paramref name="bake"/> using <paramref name="config"/>'s
        /// placement tunables + source. Returns the number of blades placed.
        /// </summary>
        public static int Bake(Terrain terrain, GpuGrassConfig config, GpuGrassBakeData bake)
        {
            if (terrain == null || terrain.terrainData == null || config == null || bake == null)
                return 0;

            TerrainData data = terrain.terrainData;
            Vector3 terrainPos = terrain.transform.position;
            Vector3 size = data.size;

            float targetDensity = Mathf.Max(0f, config.TargetDensityPerSqM);
            if (targetDensity <= 0f)
                return 0;

            var positions = new List<Vector3>(4096);
            var yaws       = new List<float>(4096);
            var scales     = new List<float>(4096);
            Bounds bounds = default;
            bool hasBounds = false;

            // Deterministic RNG (seed folded with a constant so seed 0 still varies from raw cell hashing).
            var rng = new System.Random(config.Seed * 73856093 + 19349663);

            int sourceTag;
            if (config.PlacementSource == GrassPlacementSource.TerrainSurface)
            {
                ScatterSurface(terrain, data, terrainPos, size, config, targetDensity, rng,
                    positions, yaws, scales, ref bounds, ref hasBounds);
                sourceTag = -2; // -2 → baked from the terrain surface mesh
            }
            else
            {
                if (!ScatterDetailLayers(terrain, data, terrainPos, size, config, targetDensity, rng,
                        positions, yaws, scales, ref bounds, ref hasBounds))
                    return 0;
                sourceTag = -1; // -1 → baked from the combined detail layers
            }

            // Pad bounds for blade height + bend headroom so the field AABB encloses leaning/tall blades.
            if (hasBounds)
            {
                float reach = Mathf.Max(0f, config.BladeHeightRange.y) * Mathf.Max(1f, config.ScaleRange.y);
                bounds.Expand(new Vector3(reach, reach * 2f, reach));
            }

            bake.SetData(positions.ToArray(), yaws.ToArray(), scales.ToArray(), bounds, sourceTag);
            return positions.Count;
        }

        // ── DetailLayer source ────────────────────────────────────────────────

        private static bool ScatterDetailLayers(
            Terrain terrain, TerrainData data, Vector3 terrainPos, Vector3 size,
            GpuGrassConfig config, float targetDensity, System.Random rng,
            List<Vector3> positions, List<float> yaws, List<float> scales, ref Bounds bounds, ref bool hasBounds)
        {
            int dw = data.detailWidth, dh = data.detailHeight;
            int layerCount = data.detailPrototypes.Length;
            if (dw <= 0 || dh <= 0 || layerCount == 0)
                return false;

            float cellSizeX = size.x / dw, cellSizeZ = size.z / dh;
            float cellArea  = cellSizeX * cellSizeZ;
            if (cellArea <= 0f)
                return false;

            // Combine all detail layers into one coverage field, normalized by the densest painted cell.
            int[,] coverage = new int[dh, dw];
            int maxCoverage = 0;
            for (int layer = 0; layer < layerCount; layer++)
            {
                int[,] map = data.GetDetailLayer(0, 0, dw, dh, layer);
                for (int z = 0; z < dh; z++)
                for (int x = 0; x < dw; x++)
                {
                    int v = coverage[z, x] + map[z, x];
                    coverage[z, x] = v;
                    if (v > maxCoverage) maxCoverage = v;
                }
            }
            if (maxCoverage <= 0)
                return false;

            float invCoverageMax = 1f / maxCoverage;
            GetSlope(config, out float minSlope, out float maxSlope);

            for (int z = 0; z < dh; z++)
            for (int x = 0; x < dw; x++)
            {
                int cov = coverage[z, x];
                if (cov <= 0) continue;

                float expected = cov * invCoverageMax * cellArea * targetDensity;
                int count = StochasticCount(expected, rng);
                for (int b = 0; b < count; b++)
                {
                    float worldX = terrainPos.x + (x + (float)rng.NextDouble()) * cellSizeX;
                    float worldZ = terrainPos.z + (z + (float)rng.NextDouble()) * cellSizeZ;
                    // Detail mode: no altitude mask (0..1 passes everything).
                    TryPlaceBlade(terrain, data, terrainPos, size, worldX, worldZ,
                        minSlope, maxSlope, 0f, 1f, config.ScaleRange, rng,
                        positions, yaws, scales, ref bounds, ref hasBounds);
                }
            }
            return true;
        }

        // ── TerrainSurface source ─────────────────────────────────────────────

        private static void ScatterSurface(
            Terrain terrain, TerrainData data, Vector3 terrainPos, Vector3 size,
            GpuGrassConfig config, float targetDensity, System.Random rng,
            List<Vector3> positions, List<float> yaws, List<float> scales, ref Bounds bounds, ref bool hasBounds)
        {
            int gridX = Mathf.Clamp(Mathf.CeilToInt(size.x / SURFACE_CELL_M), 1, MAX_SURFACE_GRID);
            int gridZ = Mathf.Clamp(Mathf.CeilToInt(size.z / SURFACE_CELL_M), 1, MAX_SURFACE_GRID);
            float cellSizeX = size.x / gridX, cellSizeZ = size.z / gridZ;
            float cellArea  = cellSizeX * cellSizeZ;

            GetSlope(config, out float minSlope, out float maxSlope);
            Vector2 h = config.HeightRange01;
            float minH = Mathf.Clamp01(Mathf.Min(h.x, h.y));
            float maxH = Mathf.Clamp01(Mathf.Max(h.x, h.y));

            for (int z = 0; z < gridZ; z++)
            for (int x = 0; x < gridX; x++)
            {
                int count = StochasticCount(cellArea * targetDensity, rng);
                for (int b = 0; b < count; b++)
                {
                    float worldX = terrainPos.x + (x + (float)rng.NextDouble()) * cellSizeX;
                    float worldZ = terrainPos.z + (z + (float)rng.NextDouble()) * cellSizeZ;
                    TryPlaceBlade(terrain, data, terrainPos, size, worldX, worldZ,
                        minSlope, maxSlope, minH, maxH, config.ScaleRange, rng,
                        positions, yaws, scales, ref bounds, ref hasBounds);
                }
            }
        }

        // ── Shared per-blade placement ────────────────────────────────────────

        /// <summary>
        /// Slope + altitude masks, ground-snaps to the terrain height, and (if accepted) appends a blade
        /// with a random yaw + scale. <paramref name="minH01"/>/<paramref name="maxH01"/> are the normalized
        /// altitude band (0=terrain min, 1=terrain max); pass (0,1) to disable the altitude mask.
        /// </summary>
        private static void TryPlaceBlade(
            Terrain terrain, TerrainData data, Vector3 terrainPos, Vector3 size, float worldX, float worldZ,
            float minSlope, float maxSlope, float minH01, float maxH01, Vector2 scaleRange, System.Random rng,
            List<Vector3> positions, List<float> yaws, List<float> scales, ref Bounds bounds, ref bool hasBounds)
        {
            float u = Mathf.Clamp01((worldX - terrainPos.x) / size.x);
            float v = Mathf.Clamp01((worldZ - terrainPos.z) / size.z);

            // Slope mask — reject blades on surfaces steeper/flatter than the allowed band.
            Vector3 normal = data.GetInterpolatedNormal(u, v);
            float slopeDeg = Vector3.Angle(normal, Vector3.up);
            if (slopeDeg < minSlope || slopeDeg > maxSlope) return;

            // Ground-snap to terrain height (SampleHeight is relative to the terrain transform).
            float localY = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));

            // Altitude mask (normalized terrain height). (0,1) → always passes.
            if (minH01 > 0f || maxH01 < 1f)
            {
                float normH = localY / Mathf.Max(size.y, 1e-4f);
                if (normH < minH01 || normH > maxH01) return;
            }

            Vector3 pos = new(worldX, localY + terrainPos.y, worldZ);
            positions.Add(pos);
            yaws.Add((float)rng.NextDouble() * 360f);
            scales.Add(Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble()));

            if (!hasBounds) { bounds = new Bounds(pos, Vector3.zero); hasBounds = true; }
            else bounds.Encapsulate(pos);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void GetSlope(GpuGrassConfig config, out float minSlope, out float maxSlope)
        {
            Vector2 s = config.SlopeRange;
            minSlope = Mathf.Min(s.x, s.y);
            maxSlope = Mathf.Max(s.x, s.y);
        }

        /// <summary>Fractional expectation → integer count via deterministic stochastic rounding.</summary>
        private static int StochasticCount(float expected, System.Random rng)
        {
            if (expected <= 0f) return 0;
            int count = Mathf.FloorToInt(expected);
            if (rng.NextDouble() < (expected - count)) count++;
            return count;
        }
    }
}
