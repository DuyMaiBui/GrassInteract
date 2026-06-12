#nullable enable
using NUnit.Framework;
using UnityEngine;
using GpuTerrain;
using GpuTerrain.Editor;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Tests for brush falloff math and affected-texel set calculations.
    /// These mirror the reference math used in TerrainBrush.compute.
    /// CPU-side equivalents so the test suite doesn't require a GPU.
    /// </summary>
    [TestFixture]
    public class TerrainBrushMathTests
    {
        // ── Falloff reference (must match BrushFalloff in TerrainBrush.compute) ───

        /// <summary>
        /// CPU reference for the compute shader BrushFalloff:
        ///   t = dist / radius  (clamped to [0,1])
        ///   inv = 1 - t
        ///   falloff = inv² * (3 - 2*inv)   (smoothstep on (1-t))
        /// </summary>
        private static float BrushFalloff(Vector2 texelUV, Vector2 centerUV, float radiusUV)
        {
            float dist = Vector2.Distance(texelUV, centerUV);
            float t    = Mathf.Clamp01(dist / Mathf.Max(radiusUV, 0.0001f));
            float inv  = 1f - t;
            return inv * inv * (3f - 2f * inv);
        }

        // ── Falloff boundary tests ────────────────────────────────────────────

        [Test]
        public void Falloff_AtCenter_ReturnsOne()
        {
            float f = BrushFalloff(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0.2f);
            Assert.AreEqual(1f, f, 0.0001f);
        }

        [Test]
        public void Falloff_AtEdge_ReturnsZero()
        {
            // Exactly at radius distance → t=1 → inv=0 → falloff=0.
            var center = new Vector2(0.5f, 0.5f);
            float radius = 0.2f;
            var edge   = new Vector2(0.5f + radius, 0.5f);
            float f = BrushFalloff(edge, center, radius);
            Assert.AreEqual(0f, f, 0.0001f);
        }

        [Test]
        public void Falloff_BeyondEdge_ReturnsZero()
        {
            var center = new Vector2(0.5f, 0.5f);
            var outside = new Vector2(0.5f + 0.5f, 0.5f); // well outside
            float f = BrushFalloff(outside, center, 0.2f);
            Assert.AreEqual(0f, f, 0.0001f);
        }

        [Test]
        public void Falloff_AtHalfRadius_IsPositiveAndLessThanOne()
        {
            var center = new Vector2(0.5f, 0.5f);
            var half   = new Vector2(0.5f + 0.1f, 0.5f); // half of radius=0.2
            float f = BrushFalloff(half, center, 0.2f);
            Assert.Greater(f, 0f);
            Assert.Less(f, 1f);
        }

        [Test]
        public void Falloff_IsSymmetric()
        {
            var center = new Vector2(0.5f, 0.5f);
            float radius = 0.3f;
            float f1 = BrushFalloff(new Vector2(0.5f + 0.1f, 0.5f), center, radius);
            float f2 = BrushFalloff(new Vector2(0.5f - 0.1f, 0.5f), center, radius);
            float f3 = BrushFalloff(new Vector2(0.5f, 0.5f + 0.1f), center, radius);
            Assert.AreEqual(f1, f2, 0.0001f, "Left/right falloff should be equal");
            Assert.AreEqual(f1, f3, 0.0001f, "Horizontal/vertical falloff should be equal");
        }

        // ── Affected-texel set ────────────────────────────────────────────────

        /// <summary>Count texels with falloff > 0 for a given brush config.</summary>
        private static int CountAffectedTexels(int rtRes, Vector2 centerUV, float radiusUV)
        {
            int count = 0;
            for (int y = 0; y < rtRes; ++y)
            {
                for (int x = 0; x < rtRes; ++x)
                {
                    var uv = new Vector2((x + 0.5f) / rtRes, (y + 0.5f) / rtRes);
                    if (BrushFalloff(uv, centerUV, radiusUV) > 0f)
                        ++count;
                }
            }
            return count;
        }

        [Test]
        public void AffectedTexels_TinyRadius_AtLeastOneTexel()
        {
            // Radius = 2 texels in UV space on a 64-texel grid → guaranteed to hit at least one texel.
            float radiusUV = 2f / 64f; // 2 texels
            int count = CountAffectedTexels(64, new Vector2(0.5f, 0.5f), radiusUV);
            Assert.GreaterOrEqual(count, 1);
        }

        [Test]
        public void AffectedTexels_FullRadius_AllTexels()
        {
            // radius = 1.0 covers entire [0,1]×[0,1] UV space from center.
            int res = 16;
            int count = CountAffectedTexels(res, new Vector2(0.5f, 0.5f), 1.0f);
            Assert.AreEqual(res * res, count);
        }

        [Test]
        public void AffectedTexels_GrowsWithRadius()
        {
            int res = 64;
            var center = new Vector2(0.5f, 0.5f);
            int small  = CountAffectedTexels(res, center, 0.05f);
            int medium = CountAffectedTexels(res, center, 0.2f);
            int large  = CountAffectedTexels(res, center, 0.4f);
            Assert.Less(small, medium);
            Assert.Less(medium, large);
        }

        // ── WorldBrushToTileUV helper ─────────────────────────────────────────

        [Test]
        public void WorldBrushToTileUV_TileCenter_ReturnsHalfUV()
        {
            var tileCoord = new Vector2Int(0, 0);
            float halfTile = TerrainWorldGrid.TILE_SIZE_M * 0.5f;
            var worldCenter = new Vector2(halfTile, halfTile);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldCenter, 10f, tileCoord,
                out Vector2 centerUV, out float radiusUV);
            Assert.AreEqual(0.5f, centerUV.x, 0.001f);
            Assert.AreEqual(0.5f, centerUV.y, 0.001f);
        }

        [Test]
        public void WorldBrushToTileUV_RadiusUV_CorrectlyScaled()
        {
            var tileCoord = new Vector2Int(0, 0);
            float radiusM = TerrainWorldGrid.TILE_SIZE_M * 0.25f;
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                Vector2.zero, radiusM, tileCoord,
                out _, out float radiusUV);
            Assert.AreEqual(0.25f, radiusUV, 0.001f);
        }

        // ── PaintTargetResolver: cross-tile detection ─────────────────────────

        [Test]
        public void Resolver_SingleTileCenter_ReturnsSingleTile()
        {
            var results = new System.Collections.Generic.List<Vector2Int>();
            float halfTile = TerrainWorldGrid.TILE_SIZE_M * 0.5f;
            TerrainPaintTargetResolver.Resolve(
                new Vector2(halfTile, halfTile),
                radiusM: 10f,
                residencySet: null,
                results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(new Vector2Int(0, 0), results[0]);
        }

        [Test]
        public void Resolver_BrushStraddles2Tiles_ReturnsBoth()
        {
            var results = new System.Collections.Generic.List<Vector2Int>();
            // Brush center at tile boundary (x = TILE_SIZE_M).
            TerrainPaintTargetResolver.Resolve(
                new Vector2(TerrainWorldGrid.TILE_SIZE_M, TerrainWorldGrid.TILE_SIZE_M * 0.5f),
                radiusM: 20f,
                residencySet: null,
                results);
            bool hasTile0 = results.Contains(new Vector2Int(0, 0));
            bool hasTile1 = results.Contains(new Vector2Int(1, 0));
            Assert.IsTrue(hasTile0, "Should include tile (0,0)");
            Assert.IsTrue(hasTile1, "Should include tile (1,0)");
        }

        [Test]
        public void Resolver_BrushStraddlesCorner_ReturnsFourTiles()
        {
            var results = new System.Collections.Generic.List<Vector2Int>();
            // Place brush exactly at the corner of 4 tiles.
            float corner = TerrainWorldGrid.TILE_SIZE_M;
            TerrainPaintTargetResolver.Resolve(
                new Vector2(corner, corner),
                radiusM: 20f,
                residencySet: null,
                results);
            Assert.GreaterOrEqual(results.Count, 4, "Corner brush must touch at least 4 tiles");
        }

        [Test]
        public void Resolver_ResidencyFilter_ExcludesNonResident()
        {
            // Only tile (0,0) is resident; brush straddles (0,0) and (1,0).
            var residency = new TerrainTileResidencySet();
            // Not adding any tiles → all absent → resolver returns empty.
            var results = new System.Collections.Generic.List<Vector2Int>();
            TerrainPaintTargetResolver.Resolve(
                new Vector2(TerrainWorldGrid.TILE_SIZE_M, TerrainWorldGrid.TILE_SIZE_M * 0.5f),
                radiusM: 20f,
                residencySet: residency,
                results);
            Assert.AreEqual(0, results.Count, "No resident tiles → empty result");
        }

    }
}
