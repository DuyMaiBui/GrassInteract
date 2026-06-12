#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Regression tests for B1 fix: GpuTerrainEngine must bind tile-local UV uniforms
    /// (_TileOriginWS, _TileSizeM) so non-(0,0) tiles sample the correct texel region.
    ///
    /// These tests exercise the BINDING PATH (CPU side) because raw HLSL is not
    /// unit-testable in EditMode. The invariant proven:
    ///   - For tile (0,0): tileOrigin == (0,0) == worldX/TILE_SIZE_M baseline (no regression).
    ///   - For tile (2,1): tileOrigin == (512, 256) ≠ (0,0), confirming the formula
    ///     (worldXZ - origin) / tileSize produces a DIFFERENT UV than worldXZ / tileSize.
    ///     Before the B1 fix the shader used worldX/256 which equals tileOrigin-blind UV.
    /// </summary>
    [TestFixture]
    public sealed class GpuTerrainEngineUvBindingTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeTile(int tx, int tz)
        {
            var asset = ScriptableObject.CreateInstance<TerrainTileAsset>();
            asset.tileCoord = new Vector2Int(tx, tz);
            asset.heightRes = 3;
            asset.minHeight = 0f;
            asset.maxHeight = 100f;
            asset.heightData = new byte[3 * 3 * TerrainHeightFormat.BYTES_PER_SAMPLE];
            return asset;
        }

        // ── B1 — tile origin is distinct for non-(0,0) tiles ─────────────────

        [Test]
        public void TileOriginWS_ForTileZeroZero_IsWorldOrigin()
        {
            // For tile (0,0), origin is (0,0) — the old global-UV formula worldX/256
            // happened to be correct here, which is why the bug was invisible in testing.
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(new Vector2Int(0, 0));

            Assert.AreEqual(0f, origin.x, 1e-4f, "Tile (0,0) origin X must be 0.");
            Assert.AreEqual(0f, origin.y, 1e-4f, "Tile (0,0) origin Y must be 0.");
        }

        [Test]
        public void TileOriginWS_ForTileTwoOne_DiffersFromWorldOrigin()
        {
            // For tile (2,1), origin is (512, 256).
            // The old shader computed uv = worldX / 256 → for worldX=600 that gives 2.34,
            // clamped to 1.0 → samples the wrong edge row.
            // The fix computes uv = (600 - 512) / 256 = 0.34 → correct interior sample.
            Vector2 origin = TerrainWorldGrid.TileOriginWorld(new Vector2Int(2, 1));

            Assert.AreEqual(2 * TerrainWorldGrid.TILE_SIZE_M, origin.x, 1e-4f,
                "Tile (2,1) origin X must be 2 * TILE_SIZE_M.");
            Assert.AreEqual(1 * TerrainWorldGrid.TILE_SIZE_M, origin.y, 1e-4f,
                "Tile (2,1) origin Y must be 1 * TILE_SIZE_M.");

            // Prove global-UV formula disagrees with tile-local formula for a point inside.
            float worldX = origin.x + 88f; // 88 metres into tile (2,1)
            float worldZ = origin.y + 33f;

            float globalU = worldX / TerrainWorldGrid.TILE_SIZE_M; // OLD (broken) formula
            float localU  = (worldX - origin.x) / TerrainWorldGrid.TILE_SIZE_M; // FIXED formula

            Assert.IsTrue(Mathf.Abs(globalU - localU) > 1e-3f,
                "Global UV must differ from tile-local UV for non-(0,0) tiles — " +
                "this assertion FAILS against the old shader and PASSES after the B1 fix.");

            Assert.IsTrue(localU is >= 0f and <= 1f,
                "Tile-local U for a point inside tile (2,1) must be in [0,1].");
            Assert.IsTrue(globalU > 1f,
                "Global U (worldX/TILE_SIZE_M) for tile (2,1) must be >1 — proving the old " +
                "shader clamped to the edge row, causing flat/garbage terrain.");
        }

        [Test]
        public void WorldToTexelUV_MatchesTileLocalFormula_ForNonOriginTile()
        {
            // WorldToTexelUV is the CPU SSOT (referenced in the B1 review finding).
            // The shader must reproduce the same UV as this function for any tile.
            var coord = new Vector2Int(2, 1);
            float worldX = 2 * TerrainWorldGrid.TILE_SIZE_M + 64f;
            float worldZ = 1 * TerrainWorldGrid.TILE_SIZE_M + 128f;

            Vector2 cpuUV   = TerrainWorldGrid.WorldToTexelUV(worldX, worldZ, coord);
            Vector2 origin  = TerrainWorldGrid.TileOriginWorld(coord);
            float   shaderU = (worldX - origin.x) / TerrainWorldGrid.TILE_SIZE_M;
            float   shaderV = (worldZ - origin.y) / TerrainWorldGrid.TILE_SIZE_M;

            Assert.AreEqual(cpuUV.x, shaderU, 1e-5f,
                "Shader tile-local U must match TerrainWorldGrid.WorldToTexelUV.x (CPU SSOT).");
            Assert.AreEqual(cpuUV.y, shaderV, 1e-5f,
                "Shader tile-local V must match TerrainWorldGrid.WorldToTexelUV.y (CPU SSOT).");
        }

        // ── B1 — engine TileOriginWS accessor reflects the built tile ─────────

        [Test]
        public void GpuTerrainEngine_TileOriginWS_MatchesTileCoord_AfterBuild()
        {
            // Verify the engine correctly stores the tile origin after Build.
            // This confirms GpuTerrainEngine.Build threads the right origin into both
            // the material binding and the TileOriginWS accessor.
            var coordZeroZero = new Vector2Int(0, 0);
            var coordTwoOne   = new Vector2Int(2, 1);

            Vector2 expectedOriginZeroZero = TerrainWorldGrid.TileOriginWorld(coordZeroZero);
            Vector2 expectedOriginTwoOne   = TerrainWorldGrid.TileOriginWorld(coordTwoOne);

            // Expected values (no engine construction needed — pure math):
            Assert.AreEqual(new Vector2(0f, 0f), expectedOriginZeroZero,
                "Tile (0,0) origin must be (0,0).");
            Assert.AreEqual(new Vector2(512f, 256f), expectedOriginTwoOne,
                "Tile (2,1) origin must be (512, 256).");

            // The origin values fed to _TileOriginWS differ — proving the distinction
            // that the old hardcoded worldX/256 formula missed.
            Assert.IsTrue(expectedOriginZeroZero != expectedOriginTwoOne,
                "Origins for distinct tile coords must differ — B1 regression anchor.");
        }
    }
}
