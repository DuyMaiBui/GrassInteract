#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for the density/stamp brush math seams. After the WorldPainter merge
    /// (P7) the old Scatter-Studio paint pipeline (DensityPaintGPU / DensityMapFactory /
    /// ScatterBrushPreview) was deleted; the surviving math is:
    ///
    /// <list type="number">
    ///   <item><b>UV mapping</b> — <see cref="GrassFieldSpace"/> round-trip (no GPU).</item>
    ///   <item><b>Stroke interpolation</b> — <see cref="WorldPainterStampMath.ComputeStampPositions"/>
    ///         (ported from DensityPaintGPU; pure math, no GPU).</item>
    ///   <item><b>Density encode round-trip</b> — <see cref="WorldPainterDensityEncoder.ExecuteSync"/>
    ///         RT→bytes→map, GPU-gated. This is the kept replacement for the old
    ///         DensityMapFactory.ReadbackToPixels golden + DensityPaintGPU GPU-paint smoke.</item>
    /// </list>
    ///
    /// Dropped in P7 (test deleted-code, superseded, no silent coverage loss):
    ///   - DensityMapFactory.ReadbackToPixels golden → covered by the DensityEncoder round-trip below.
    ///   - DensityPaintGPU GPU-paint smoke → the density paint is now the TerrainBrush.compute
    ///     PaintDensity kernel + WorldPainterDensityEncoder (round-trip below).
    ///   - ScatterBrushPreview.ComputeDecalRotation (×3) → the decal-quad-rotation cursor was
    ///     replaced by TerrainBrushPreview's height-conforming disc; no rotation math remains.
    /// </summary>
    public sealed class DensityBrushMathTests
    {
        // ── 1. UV mapping round-trip ───────────────────────────────────────────

        [Test]
        public void GrassFieldSpace_CenterWorld_MapsTo_HalfHalfUv()
        {
            var origin = new Vector3(10f, 0f, 20f);
            var bounds = new Vector2(100f, 80f);
            var space  = new GrassFieldSpace(origin, bounds);

            var worldCenter = new Vector3(10f, 0f, 20f);
            Vector2 uv = space.WorldToUv(worldCenter);

            Assert.AreEqual(0.5f, uv.x, 1e-5f, "Center X → UV.x = 0.5");
            Assert.AreEqual(0.5f, uv.y, 1e-5f, "Center Z → UV.y = 0.5");
        }

        [Test]
        public void GrassFieldSpace_BottomLeft_MapsTo_ZeroZeroUv()
        {
            var origin = new Vector3(0f, 0f, 0f);
            var bounds = new Vector2(50f, 50f);
            var space  = new GrassFieldSpace(origin, bounds);

            var worldBL = new Vector3(-25f, 0f, -25f);
            Vector2 uv  = space.WorldToUv(worldBL);

            Assert.AreEqual(0f, uv.x, 1e-5f, "BL → UV.x = 0");
            Assert.AreEqual(0f, uv.y, 1e-5f, "BL → UV.y = 0");
        }

        [Test]
        public void GrassFieldSpace_TopRight_MapsTo_OneOneUv()
        {
            var origin = new Vector3(0f, 0f, 0f);
            var bounds = new Vector2(50f, 50f);
            var space  = new GrassFieldSpace(origin, bounds);

            var worldTR = new Vector3(25f, 0f, 25f);
            Vector2 uv  = space.WorldToUv(worldTR);

            Assert.AreEqual(1f, uv.x, 1e-5f, "TR → UV.x = 1");
            Assert.AreEqual(1f, uv.y, 1e-5f, "TR → UV.y = 1");
        }

        [Test]
        public void GrassFieldSpace_UvToWorld_RoundTrip_PreservesPosition()
        {
            var origin = new Vector3(5f, 3f, -10f);
            var bounds = new Vector2(200f, 150f);
            var space  = new GrassFieldSpace(origin, bounds);

            var worldIn = new Vector3(40f, 3f, 20f);
            Vector2 uv  = space.WorldToUv(worldIn);
            Vector3 worldOut = space.UvToWorld(uv, worldIn.y);

            Assert.AreEqual(worldIn.x, worldOut.x, 1e-4f, "Round-trip X");
            Assert.AreEqual(worldIn.z, worldOut.z, 1e-4f, "Round-trip Z");
        }

        // ── 2. Stroke-interpolation spacing (WorldPainterStampMath) ────────────

        [Test]
        public void ComputeStampPositions_ZeroDistance_ReturnsSinglePositionAtTo()
        {
            var from = new Vector2(0.5f, 0.5f);
            var to   = new Vector2(0.5f, 0.5f);

            Vector2[] positions = WorldPainterStampMath.ComputeStampPositions(from, to, 0.1f, 0.25f);

            Assert.AreEqual(1, positions.Length, "Zero-distance → exactly one stamp at 'to'");
            Assert.AreEqual(to.x, positions[0].x, 1e-5f, "Position.x == to.x");
            Assert.AreEqual(to.y, positions[0].y, 1e-5f, "Position.y == to.y");
        }

        [Test]
        public void ComputeStampPositions_LastPosition_IsAlwaysToUv()
        {
            var from = new Vector2(0.1f, 0.1f);
            var to   = new Vector2(0.9f, 0.5f);

            Vector2[] positions = WorldPainterStampMath.ComputeStampPositions(from, to, 0.05f, 0.25f);

            Assert.Greater(positions.Length, 0, "Should have at least one stamp");
            Vector2 last = positions[positions.Length - 1];
            Assert.AreEqual(to.x, last.x, 1e-5f, "Last stamp.x == to.x");
            Assert.AreEqual(to.y, last.y, 1e-5f, "Last stamp.y == to.y");
        }

        [Test]
        public void ComputeStampPositions_Count_MatchesCeilDivision()
        {
            var from = new Vector2(0f, 0f);
            var to   = new Vector2(1f, 0f);
            float radius        = 0.1f;
            float spacingFactor = 0.25f;
            float spacing       = radius * spacingFactor;
            float dist          = Vector2.Distance(from, to);

            int expectedCount = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
            Vector2[] positions = WorldPainterStampMath.ComputeStampPositions(from, to, radius, spacingFactor);

            Assert.AreEqual(expectedCount, positions.Length,
                "Stamp count must equal ceil(dist / spacing)");
        }

        [Test]
        public void ComputeStampPositions_MaxGap_LessThanOrEqualToSpacing()
        {
            var from = new Vector2(0.2f, 0.1f);
            var to   = new Vector2(0.8f, 0.7f);
            float radius        = 0.08f;
            float spacingFactor = 0.25f;
            float maxAllowedGap = radius * spacingFactor * 1.01f; // 1% tolerance

            Vector2[] positions = WorldPainterStampMath.ComputeStampPositions(from, to, radius, spacingFactor);

            for (int i = 1; i < positions.Length; ++i)
            {
                float gap = Vector2.Distance(positions[i - 1], positions[i]);
                Assert.LessOrEqual(gap, maxAllowedGap,
                    $"Gap between stamp {i-1} and {i} ({gap:F5}) exceeds max allowed ({maxAllowedGap:F5})");
            }
        }

        [Test]
        public void ComputeStampPositions_Spacing_IsUniform()
        {
            var from = new Vector2(0f, 0f);
            var to   = new Vector2(0.5f, 0f);

            Vector2[] positions = WorldPainterStampMath.ComputeStampPositions(from, to, 0.1f, 0.25f);

            if (positions.Length < 2) return; // single stamp — spacing not applicable

            float first = Vector2.Distance(positions[0], positions[1]);
            for (int i = 2; i < positions.Length; ++i)
            {
                float gap = Vector2.Distance(positions[i - 1], positions[i]);
                Assert.AreEqual(first, gap, 1e-4f, $"Gap at index {i} differs from first gap — not uniform");
            }
        }

        // ── 3. WorldPainterDensityEncoder round-trip (GPU-gated) ───────────────

        /// <summary>
        /// RT→bytes→map round-trip for <see cref="WorldPainterDensityEncoder"/>.
        /// Allocates a small R8_UNorm (or best supported) RT, blits a known value 0.7,
        /// calls <see cref="WorldPainterDensityEncoder.ExecuteSync"/> with a blank Texture2D,
        /// and asserts the map's R channel ≈ 0.7.
        /// Gated behind <c>SystemInfo.graphicsDeviceType != Null</c>.
        /// </summary>
        [Test]
        public void DensityEncoder_RoundTrip_RTToBytesPreservesValue()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Skipping density encoder round-trip test — null graphics device.");
                return;
            }

            const int   SIZE    = 16;
            const float DENSITY = 0.7f;

            GraphicsFormat fmt;
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render))
                fmt = GraphicsFormat.R8_UNorm;
            else if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render))
                fmt = GraphicsFormat.R16_SFloat;
            else
                fmt = GraphicsFormat.R32_SFloat;

            var rt = new RenderTexture(SIZE, SIZE, 0, fmt) { enableRandomWrite = true };
            rt.Create();

            var seed = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
            var seedPx = new Color[SIZE * SIZE];
            for (int i = 0; i < seedPx.Length; ++i)
                seedPx[i] = new Color(DENSITY, DENSITY, DENSITY, 1f);
            seed.SetPixels(seedPx);
            seed.Apply(false);
            Graphics.Blit(seed, rt);

            var map = new Texture2D(SIZE, SIZE, TextureFormat.RFloat, false, true);
            var blank = new Color[SIZE * SIZE];
            for (int i = 0; i < blank.Length; ++i) blank[i] = Color.black;
            map.SetPixels(blank);
            map.Apply(false);

            try
            {
                var encoder = new WorldPainterDensityEncoder();
                // Use the Texture2D overload directly (the legacy DensityScatterLayer overload is removed).
                encoder.ExecuteSync(map, rt);

                Color[] result = map.GetPixels();
                Assert.AreEqual(SIZE * SIZE, result.Length, "Pixel count mismatch after round-trip");

                float tol = 1.5f / 255f; // ±1 step for R8 quantisation
                for (int i = 0; i < result.Length; ++i)
                {
                    Assert.AreEqual(DENSITY, result[i].r, tol,
                        $"Pixel {i} R after round-trip: expected ~{DENSITY} got {result[i].r}");
                }
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(seed);
                Object.DestroyImmediate(map);
            }
        }
    }
}
