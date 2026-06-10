#nullable enable
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using GrassInteract.Editor;

namespace GrassInteract.Tests
{
    /// <summary>
    /// EditMode tests for the density brush math seams introduced in the terrain-style brush
    /// redesign (P3/P5/P6). Tests are split into three groups:
    ///
    /// <list type="number">
    ///   <item><b>UV mapping</b> — <see cref="GrassFieldSpace"/> round-trip (no GPU required).</item>
    ///   <item><b>Stroke interpolation</b> — <see cref="DensityPaintGPU.ComputeStampPositions"/>
    ///         pure-math test (no GPU required).</item>
    ///   <item><b>RT readback golden</b> — allocates a small linear RT, blits a known value,
    ///         calls <see cref="DensityMapFactory.ReadbackToPixels"/>, asserts the R channel.
    ///         Gated behind <c>SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null</c>.</item>
    /// </list>
    ///
    /// <list type="number">
    ///   <item><b>Decal orientation</b> — <see cref="ScatterBrushPreview.ComputeDecalRotation"/>
    ///         up-normal edge case (no GPU required).</item>
    /// </list>
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

            // Center of the field in world space.
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

            // Bottom-left corner: center - halfBounds.
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

            // Top-right corner: center + halfBounds.
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

        // ── 2. Stroke-interpolation spacing ───────────────────────────────────

        [Test]
        public void ComputeStampPositions_ZeroDistance_ReturnsSinglePositionAtTo()
        {
            var from = new Vector2(0.5f, 0.5f);
            var to   = new Vector2(0.5f, 0.5f);

            Vector2[] positions = DensityPaintGPU.ComputeStampPositions(from, to, 0.1f, 0.25f);

            Assert.AreEqual(1, positions.Length, "Zero-distance → exactly one stamp at 'to'");
            Assert.AreEqual(to.x, positions[0].x, 1e-5f, "Position.x == to.x");
            Assert.AreEqual(to.y, positions[0].y, 1e-5f, "Position.y == to.y");
        }

        [Test]
        public void ComputeStampPositions_LastPosition_IsAlwaysToUv()
        {
            var from = new Vector2(0.1f, 0.1f);
            var to   = new Vector2(0.9f, 0.5f);

            Vector2[] positions = DensityPaintGPU.ComputeStampPositions(from, to, 0.05f, 0.25f);

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
            Vector2[] positions = DensityPaintGPU.ComputeStampPositions(from, to, radius, spacingFactor);

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

            Vector2[] positions = DensityPaintGPU.ComputeStampPositions(from, to, radius, spacingFactor);

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

            Vector2[] positions = DensityPaintGPU.ComputeStampPositions(from, to, 0.1f, 0.25f);

            if (positions.Length < 2) return; // single stamp — spacing not applicable

            float first = Vector2.Distance(positions[0], positions[1]);
            for (int i = 2; i < positions.Length; ++i)
            {
                float gap = Vector2.Distance(positions[i - 1], positions[i]);
                Assert.AreEqual(first, gap, 1e-4f, $"Gap at index {i} differs from first gap — not uniform");
            }
        }

        // ── 3. RT readback golden (GPU-gated) ─────────────────────────────────

        [Test]
        public void ReadbackToPixels_KnownValueRT_ReturnsCorrectR()
        {
            // Skip on headless/null-GPU (e.g. CI without a real device).
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Skipping GPU readback golden test — null graphics device.");
                return;
            }

            const int SIZE      = 16;
            const float DENSITY = 0.6f;

            // Create a small linear R8 RT (or best supported format).
            GraphicsFormat fmt;
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render))
                fmt = GraphicsFormat.R8_UNorm;
            else if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, GraphicsFormatUsage.Render))
                fmt = GraphicsFormat.R16_SFloat;
            else
                fmt = GraphicsFormat.R32_SFloat;

            var rt = new RenderTexture(SIZE, SIZE, 0, fmt);
            rt.Create();

            // Fill the RT with a known grey value via a temporary Texture2D blit.
            // Use mipChain=false, linear=true so no sRGB→linear conversion happens when blitting
            // to the linear RT — we want the raw float value preserved.
            var seed = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
            var pixels = new Color[SIZE * SIZE];
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = new Color(DENSITY, DENSITY, DENSITY, 1f);
            seed.SetPixels(pixels);
            seed.Apply(false);
            Graphics.Blit(seed, rt);

            try
            {
                Color[] result = DensityMapFactory.ReadbackToPixels(rt);

                Assert.AreEqual(SIZE * SIZE, result.Length, "Pixel count mismatch");

                // Allow ±1/255 tolerance for R8 quantisation.
                float tolerance = 1.5f / 255f;
                for (int i = 0; i < result.Length; ++i)
                {
                    Assert.AreEqual(DENSITY, result[i].r, tolerance,
                        $"Pixel {i} R channel: expected ~{DENSITY} got {result[i].r}");
                }
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(seed);
            }
        }

        // ── 4. GPU paint smoke test (GPU-gated) ───────────────────────────────

        /// <summary>
        /// End-to-end paint smoke test: creates a small all-black density map, runs a single
        /// Paint stamp at the center UV via the full GPU pipeline, ends the stroke (which reads back
        /// pixels from the RT into the CPU map), and asserts that the center pixel R value increased
        /// from 0. Reports actual pixel values.
        ///
        /// Gated behind <c>SystemInfo.graphicsDeviceType != Null</c>.
        /// </summary>
        [Test]
        public void GPUPaintSmoke_CenterStamp_IncreasesPixelR()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Skipping GPU paint smoke test — null graphics device.");
                return;
            }

            const int SIZE = 32;

            // Build an all-black, readable density map (same format DensityMapFactory.CreateBlank uses).
            var map = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
            var black = new Color[SIZE * SIZE];
            for (int i = 0; i < black.Length; ++i)
                black[i] = Color.black;
            map.SetPixels(black);
            map.Apply(false);

            float seedCenter = map.GetPixelBilinear(0.5f, 0.5f).r;
            Assert.AreEqual(0f, seedCenter, 1e-5f, "Seed center R must be 0 before painting");

            try
            {
                var paintGpu = new DensityPaintGPU();

                // Full-strength Paint stamp at center UV; non-square radius to exercise NTH-2.
                const float RAD_UV_X   = 0.3f;
                const float RAD_UV_Y   = 0.3f;
                const float STRENGTH   = 1.0f;
                const float INNER      = 0.5f;
                const int   MODE_PAINT = 0;
                var centerUv = new Vector2(0.5f, 0.5f);

                paintGpu.BeginStroke(map);
                paintGpu.StrokeTo(centerUv, centerUv, RAD_UV_X, RAD_UV_Y, 0.25f, STRENGTH, null, MODE_PAINT, INNER);

                // EndStroke does synchronous GPU→CPU readback and writes back into `map`.
                paintGpu.EndStroke();

                // Assert the CPU map was updated.
                float paintedCenter = map.GetPixelBilinear(0.5f, 0.5f).r;
                Debug.Log($"[Smoke] Center R before paint: {seedCenter}  |  after paint: {paintedCenter}");
                Assert.Greater(paintedCenter, seedCenter,
                    $"Center pixel R did not increase after Paint stamp. " +
                    $"Before={seedCenter} After={paintedCenter}");
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }

        // ── 5. Decal orientation — up-normal edge case ────────────────────────

        [Test]
        public void ComputeDecalRotation_UpNormal_ProducesValidBasis()
        {
            // A surface pointing straight up (flat ground) must not produce a NaN quaternion.
            Quaternion rot = ScatterBrushPreview.ComputeDecalRotation(Vector3.up);

            // A valid quaternion has unit length and no NaN components.
            float mag = Mathf.Sqrt(rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w);
            Assert.AreEqual(1f, mag, 1e-4f, "Up-normal rotation must have unit magnitude (no NaN)");
        }

        [Test]
        public void ComputeDecalRotation_ForwardNormal_ProducesValidBasis()
        {
            Quaternion rot = ScatterBrushPreview.ComputeDecalRotation(Vector3.forward);

            float mag = Mathf.Sqrt(rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w);
            Assert.AreEqual(1f, mag, 1e-4f, "Forward-normal rotation must have unit magnitude");
        }

        [Test]
        public void ComputeDecalRotation_SlopedNormal_ZAxisAligns()
        {
            // The quad mesh lies in its local XY plane, so its face normal is local +Z. For the decal
            // to lie FLAT on the surface, that +Z must align with the surface normal (within tolerance).
            var normal = new Vector3(0.3f, 0.9f, 0.1f).normalized;
            Quaternion rot = ScatterBrushPreview.ComputeDecalRotation(normal);

            Vector3 rotatedForward = rot * Vector3.forward;
            float dot = Vector3.Dot(rotatedForward, normal);
            Assert.AreEqual(1f, dot, 1e-4f, "Decal +Z (face normal) must align with surface normal");
        }
    }
}
