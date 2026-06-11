#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace GpuTerrain.Tests
{
    /// <summary>
    /// Host-replicated tests for the central-difference normal derivation in TerrainNormals.hlsl.
    ///
    /// Approach: build a simple height function on the CPU, compute the expected analytic
    /// normal, then verify the central-difference approximation matches within epsilon.
    ///
    /// Height decode SSOT: TerrainHeightFormat.DecodeNormalized (mirrors TerrainVtf.hlsl DecodeHeight).
    /// Normal formula: normalize(float3(-dhdx, 1, -dhdz)) where dh/dx and dh/dz are central-
    /// difference gradients in world space.
    /// </summary>
    [TestFixture]
    public sealed class TerrainNormalsTests
    {
        // ── Constants mirrored from shader ────────────────────────────────────

        private const float TILE_SIZE_M         = 256f;
        private const float DEFAULT_NORMAL_EPS  = TerrainShadingConfig.DEFAULT_NORMAL_EPSILON;

        private const float MIN_HEIGHT = 0f;
        private const float MAX_HEIGHT = 512f;

        // ── Host-replicated height decode ─────────────────────────────────────

        // Mirrors TerrainVtf.hlsl DecodeHeight / TerrainHeightFormat.DecodeNormalized.
        private static float DecodeHeight(float normalized, float minH, float maxH)
            => TerrainHeightFormat.DecodeNormalized(normalized, minH, maxH);

        // ── Host-replicated central-difference normal ─────────────────────────
        // Mirrors TerrainNormals.hlsl DeriveNormalWS (using a height function delegate).

        private static Vector3 DeriveNormalWS(
            System.Func<Vector2, float> heightFn,
            Vector2 tileUV,
            float eps)
        {
            float hPX = heightFn(new Vector2(tileUV.x + eps, tileUV.y));
            float hNX = heightFn(new Vector2(tileUV.x - eps, tileUV.y));
            float hPZ = heightFn(new Vector2(tileUV.x, tileUV.y + eps));
            float hNZ = heightFn(new Vector2(tileUV.x, tileUV.y - eps));

            float worldStep = eps * TILE_SIZE_M;
            float invStep2  = 1f / (2f * worldStep);
            float dhdx = (hPX - hNX) * invStep2;
            float dhdz = (hPZ - hNZ) * invStep2;

            return new Vector3(-dhdx, 1f, -dhdz).normalized;
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        private static void AssertNormalsClose(Vector3 expected, Vector3 actual, float tolerance,
            string message)
        {
            float dot = Vector3.Dot(expected.normalized, actual.normalized);
            // dot ≥ cos(tolerance_angle); use tolerance as angular tolerance in radians.
            float cosThreshold = Mathf.Cos(tolerance);
            Assert.GreaterOrEqual(dot, cosThreshold,
                $"{message}: expected={expected}, actual={actual}, dot={dot:F4}");
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void FlatTerrain_NormalIsUp()
        {
            // A perfectly flat heightmap: all heights = some constant.
            float constH = 100f;
            float HeightFn(Vector2 uv) => constH;

            Vector3 normal = DeriveNormalWS(HeightFn, new Vector2(0.5f, 0.5f), DEFAULT_NORMAL_EPS);

            Assert.AreEqual(0f, normal.x, 1e-4f, "Flat terrain: X component must be 0.");
            Assert.AreEqual(0f, normal.z, 1e-4f, "Flat terrain: Z component must be 0.");
            Assert.AreEqual(1f, normal.y, 1e-4f, "Flat terrain: Y component must be 1.");
        }

        [Test]
        public void LinearRampAlongX_NormalMatchesAnalytic()
        {
            // Height = u * TILE_SIZE_M * slope (slope = rise/run in world space).
            // dh/dx = slope, dh/dz = 0 → analytic normal = normalize(-slope, 1, 0).
            const float slope = 0.5f; // 0.5 m rise per 1 m run
            float HeightFn(Vector2 uv) => uv.x * TILE_SIZE_M * slope;

            Vector3 normal  = DeriveNormalWS(HeightFn, new Vector2(0.5f, 0.5f), DEFAULT_NORMAL_EPS);
            Vector3 expected = new Vector3(-slope, 1f, 0f).normalized;

            // Allow ~1 degree angular tolerance for central-difference truncation error.
            AssertNormalsClose(expected, normal, Mathf.Deg2Rad * 1f,
                "Linear X-ramp: derived normal must match analytic slope.");
        }

        [Test]
        public void LinearRampAlongZ_NormalMatchesAnalytic()
        {
            const float slope = 0.3f;
            float HeightFn(Vector2 uv) => uv.y * TILE_SIZE_M * slope;

            Vector3 normal   = DeriveNormalWS(HeightFn, new Vector2(0.5f, 0.5f), DEFAULT_NORMAL_EPS);
            Vector3 expected = new Vector3(0f, 1f, -slope).normalized;

            AssertNormalsClose(expected, normal, Mathf.Deg2Rad * 1f,
                "Linear Z-ramp: derived normal must match analytic slope.");
        }

        [Test]
        public void DiagonalRamp_NormalMatchesAnalytic()
        {
            const float slopeX = 0.4f;
            const float slopeZ = 0.2f;
            float HeightFn(Vector2 uv)
                => uv.x * TILE_SIZE_M * slopeX + uv.y * TILE_SIZE_M * slopeZ;

            Vector3 normal   = DeriveNormalWS(HeightFn, new Vector2(0.5f, 0.5f), DEFAULT_NORMAL_EPS);
            Vector3 expected = new Vector3(-slopeX, 1f, -slopeZ).normalized;

            AssertNormalsClose(expected, normal, Mathf.Deg2Rad * 1f,
                "Diagonal ramp: derived normal must match analytic slope.");
        }

        [Test]
        public void DerivedNormal_IsUnitLength()
        {
            const float slope = 0.6f;
            float HeightFn(Vector2 uv) => uv.x * TILE_SIZE_M * slope;
            Vector3 normal = DeriveNormalWS(HeightFn, new Vector2(0.5f, 0.5f), DEFAULT_NORMAL_EPS);
            Assert.AreEqual(1f, normal.magnitude, 1e-4f, "Derived normal must be unit length.");
        }

        [Test]
        public void DerivedNormal_AlwaysUpward_OnNonSteepTerrain()
        {
            // For slope < 45° the Y component must be > 0.
            const float slope = 0.9f; // ~42° — just under 45°
            float HeightFn(Vector2 uv) => uv.x * TILE_SIZE_M * slope;
            Vector3 normal = DeriveNormalWS(HeightFn, new Vector2(0.5f, 0.5f), DEFAULT_NORMAL_EPS);
            Assert.Greater(normal.y, 0f, "Y component of derived normal must be > 0 for slope < 45°.");
        }
    }
}
