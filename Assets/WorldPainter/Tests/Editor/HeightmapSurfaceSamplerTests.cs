#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="HeightmapSurfaceSampler"/> — the load-bearing risk-20 mitigation.
    ///
    /// Key assertion: TrySample height MUST match TerrainHeightSampleCpu on the same data
    /// (same SSOT decode → no floating scatter vs. rendered surface).
    /// </summary>
    [TestFixture]
    public sealed class HeightmapSurfaceSamplerTests
    {
        private const float TILE    = TerrainWorldGrid.TILE_SIZE_M;
        private const float EPSILON = 0.05f; // half a quantisation step at 512m range

        // ── Tile builders ─────────────────────────────────────────────────────

        private static TerrainTileAsset MakeFlatTile(float height,
            float minH = 0f, float maxH = 512f, Vector2Int? coord = null)
        {
            int res  = TerrainWorldGrid.DEFAULT_HEIGHT_RES;
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = coord ?? Vector2Int.zero;
            tile.heightRes = res;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            int count = res * res;
            tile.heightData = new byte[count * TerrainHeightFormat.BYTES_PER_SAMPLE];
            ushort raw = TerrainHeightFormat.EncodeHeight(height, minH, maxH);
            for (int i = 0; i < count; ++i)
                TerrainHeightFormat.WriteRaw(tile.heightData, i, raw);
            return tile;
        }

        private static TerrainTileAsset MakeXRampTile(float minH = 0f, float maxH = 512f)
        {
            int res  = TerrainWorldGrid.DEFAULT_HEIGHT_RES;
            var tile = ScriptableObject.CreateInstance<TerrainTileAsset>();
            tile.tileCoord = Vector2Int.zero;
            tile.heightRes = res;
            tile.minHeight = minH;
            tile.maxHeight = maxH;
            int count = res * res;
            tile.heightData = new byte[count * TerrainHeightFormat.BYTES_PER_SAMPLE];
            for (int z = 0; z < res; ++z)
                for (int x = 0; x < res; ++x)
                {
                    float t = (res > 1) ? (float)x / (res - 1) : 0f;
                    ushort raw = TerrainHeightFormat.EncodeHeight(minH + t * (maxH - minH), minH, maxH);
                    TerrainHeightFormat.WriteRaw(tile.heightData, z * res + x, raw);
                }
            return tile;
        }

        // ── Height parity with TerrainHeightSampleCpu (risk-20 gate) ─────────

        [Test]
        public void FlatTile_SamplerHeight_MatchesCpuDecode()
        {
            float expected = 150f;
            var tile    = MakeFlatTile(expected);
            var sampler = new HeightmapSurfaceSampler(tile);

            bool cpuOk  = TerrainHeightSampleCpu.TrySample(tile, TILE * 0.5f, TILE * 0.5f, out float cpuY);
            bool sampOk = sampler.TrySample(TILE * 0.5f, TILE * 0.5f, out SurfaceHit hit);

            Assert.IsTrue(cpuOk,  "CPU TrySample should succeed on flat tile");
            Assert.IsTrue(sampOk, "HeightmapSurfaceSampler.TrySample should succeed on flat tile");
            Assert.AreEqual(cpuY, hit.Y, EPSILON,
                "Sampler height MUST equal CPU decode (SSOT parity — risk-20 gate).");
        }

        [Test]
        public void XRampTile_SamplerHeight_MatchesCpuDecode_AtQuarterX()
        {
            var tile    = MakeXRampTile(0f, 400f);
            var sampler = new HeightmapSurfaceSampler(tile);
            float qx    = TILE * 0.25f;

            bool cpuOk  = TerrainHeightSampleCpu.TrySample(tile, qx, TILE * 0.5f, out float cpuY);
            bool sampOk = sampler.TrySample(qx, TILE * 0.5f, out SurfaceHit hit);

            Assert.IsTrue(cpuOk  && sampOk, "Both samplers should succeed in-tile");
            Assert.AreEqual(cpuY, hit.Y, EPSILON,
                "Ramp parity at quarterX: sampler == CPU decode (SSOT).");
        }

        [Test]
        public void XRampTile_SamplerHeight_MatchesCpuDecode_AtHalfX()
        {
            var tile    = MakeXRampTile(0f, 512f);
            var sampler = new HeightmapSurfaceSampler(tile);
            float hx    = TILE * 0.5f;

            TerrainHeightSampleCpu.TrySample(tile, hx, TILE * 0.5f, out float cpuY);
            sampler.TrySample(hx, TILE * 0.5f, out SurfaceHit hit);

            Assert.AreEqual(cpuY, hit.Y, EPSILON,
                "Ramp parity at halfX: sampler == CPU decode (SSOT).");
        }

        // ── Off-grid returns false ────────────────────────────────────────────

        [Test]
        public void OffGrid_ReturnsFalse()
        {
            var tile    = MakeFlatTile(100f);
            var sampler = new HeightmapSurfaceSampler(tile);

            bool ok = sampler.TrySample(TILE + 1f, 0f, out _);
            Assert.IsFalse(ok, "Position in adjacent tile should return false.");
        }

        [Test]
        public void UnregisteredTile_ReturnsFalse()
        {
            // Resolver always returns null → off-grid for any coordinate.
            var sampler = new HeightmapSurfaceSampler(_ => null);
            bool ok = sampler.TrySample(TILE * 0.5f, TILE * 0.5f, out _);
            Assert.IsFalse(ok, "Null resolver should return false.");
        }

        // ── Normal and slope ─────────────────────────────────────────────────

        [Test]
        public void FlatTile_Normal_IsApproximatelyUp()
        {
            var tile    = MakeFlatTile(100f);
            var sampler = new HeightmapSurfaceSampler(tile);

            sampler.TrySample(TILE * 0.5f, TILE * 0.5f, out SurfaceHit hit);

            Assert.AreEqual(0f, hit.SlopeDeg, 1f,
                "Flat terrain slope should be ~0 degrees.");
            Assert.AreEqual(Vector3.up.y, hit.Normal.y, 0.01f,
                "Flat terrain normal Y should be ~1.");
        }

        [Test]
        public void XRampTile_SlopeDeg_IsNonZero()
        {
            var tile    = MakeXRampTile(0f, 512f); // steep ramp
            var sampler = new HeightmapSurfaceSampler(tile);

            sampler.TrySample(TILE * 0.5f, TILE * 0.5f, out SurfaceHit hit);

            Assert.Greater(hit.SlopeDeg, 5f,
                "Steep ramp should produce slope > 5 degrees.");
        }
    }
}
