#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldRootBinder"/> — the painting↔world coordinate math that keeps
    /// terrain/grass/prop render, cull, colliders, and editor in lockstep under the root's
    /// non-uniform TRS. Gates the plan's risk-16 (frustum-plane transform) and risk-15
    /// (the CPU math `execute_code` cannot verify in this env).
    /// </summary>
    [TestFixture]
    public sealed class WorldRootBinderTests
    {
        private const float EPS = 1e-4f;

        private static GameObject MakeRoot(Vector3 pos, Quaternion rot, Vector3 scale, out WorldRootBinder binder)
        {
            var go = new GameObject("WP_Test_Root");
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            binder = new WorldRootBinder(go.transform);
            binder.PushGlobals();
            return go;
        }

        // ── Normal matrix = inverse-transpose of the upper-left 3x3 ──────────────

        [Test]
        public void NormalMatrix_EqualsInverseTranspose_UnderNonUniformScale()
        {
            Matrix4x4 m = Matrix4x4.TRS(
                new Vector3(10f, -3f, 5f),
                Quaternion.Euler(30f, 45f, 10f),
                new Vector3(2f, 1f, 0.5f));

            Matrix4x4 got = WorldRootBinder.NormalMatrix(m);

            // Oracle: zero translation, invert, transpose.
            Matrix4x4 linear = m;
            linear.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            linear.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            Matrix4x4 expected = linear.inverse.transpose;

            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    Assert.AreEqual(expected[r, c], got[r, c], EPS, $"normal matrix [{r},{c}]");
        }

        [Test]
        public void NormalMatrix_PreservesNormalDirection_PerpendicularToScaledTangents()
        {
            // A normal transformed by the inverse-transpose stays perpendicular to surface
            // tangents transformed by the matrix itself — the defining property of the normal matrix.
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(15f, 80f, -25f), new Vector3(3f, 1f, 0.4f));
            Matrix4x4 nm = WorldRootBinder.NormalMatrix(m);

            // Two tangents and their normal in painting space.
            Vector3 t1 = new Vector3(1f, 0.3f, 0f).normalized;
            Vector3 t2 = new Vector3(0f, 0.2f, 1f).normalized;
            Vector3 n  = Vector3.Cross(t1, t2).normalized;

            Vector3 t1w = m.MultiplyVector(t1);
            Vector3 t2w = m.MultiplyVector(t2);
            Vector3 nw  = ((Vector3)(nm * new Vector4(n.x, n.y, n.z, 0f))).normalized;

            Assert.AreEqual(0f, Vector3.Dot(nw, t1w.normalized), 1e-3f, "normal ⟂ tangent1 in world");
            Assert.AreEqual(0f, Vector3.Dot(nw, t2w.normalized), 1e-3f, "normal ⟂ tangent2 in world");
        }

        // ── Identity root ⇒ no-op (regression guard for "identity = no change") ──

        [Test]
        public void IdentityRoot_IsNoOp()
        {
            var go = MakeRoot(Vector3.zero, Quaternion.identity, Vector3.one, out WorldRootBinder binder);
            try
            {
                Assert.IsTrue(binder.IsIdentity, "identity root should report IsIdentity");
                Vector3 p = new Vector3(12.5f, -4f, 33f);
                Assert.That((binder.WorldToPainting(p) - p).magnitude, Is.LessThan(EPS), "WorldToPainting no-op");
                Assert.That((binder.PaintingToWorld(p) - p).magnitude, Is.LessThan(EPS), "PaintingToWorld no-op");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── World↔painting round-trip parity under non-identity TRS ──────────────

        [Test]
        public void RoundTrip_WorldToPaintingToWorld_IsIdentity()
        {
            var go = MakeRoot(
                new Vector3(7f, 2f, -9f), Quaternion.Euler(20f, -110f, 35f), new Vector3(1.5f, 2.5f, 0.75f),
                out WorldRootBinder binder);
            try
            {
                Assert.IsFalse(binder.IsIdentity, "TRS root should not be identity");
                foreach (Vector3 p in new[]
                {
                    new Vector3(0f, 0f, 0f), new Vector3(1f, 2f, 3f),
                    new Vector3(-50f, 8f, 17f), new Vector3(123f, -45f, 6f),
                })
                {
                    Vector3 back = binder.WorldToPainting(binder.PaintingToWorld(p));
                    Assert.That((back - p).magnitude, Is.LessThan(1e-3f), $"round-trip parity for {p}");
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ── Frustum planes → painting space preserve the inside/outside test ─────

        [Test]
        public void WorldPlanesToPainting_PreservesHalfSpaceClassification()
        {
            var go = MakeRoot(
                new Vector3(4f, 1f, -2f), Quaternion.Euler(10f, 60f, -15f), new Vector3(2f, 1f, 0.5f),
                out WorldRootBinder binder);
            try
            {
                // Build a world frustum from a proj*view matrix.
                Matrix4x4 proj = Matrix4x4.Perspective(60f, 1.3333f, 0.3f, 100f);
                Matrix4x4 view = Matrix4x4.TRS(new Vector3(0f, 5f, -20f), Quaternion.Euler(10f, 0f, 0f), Vector3.one).inverse;
                Plane[] worldPlanes = GeometryUtility.CalculateFrustumPlanes(proj * view);

                var paintingPlanes = new Vector4[6];
                binder.WorldPlanesToPainting(worldPlanes, paintingPlanes);

                // Sample a grid of world points; each must classify identically in both spaces.
                int checks = 0;
                for (float x = -30f; x <= 30f; x += 7.5f)
                for (float y = -10f; y <= 20f; y += 7.5f)
                for (float z = -10f; z <= 60f; z += 10f)
                {
                    Vector3 pw = new Vector3(x, y, z);
                    Vector3 pp = binder.WorldToPainting(pw);

                    for (int i = 0; i < 6; i++)
                    {
                        float dw = worldPlanes[i].GetDistanceToPoint(pw);
                        Vector4 pl = paintingPlanes[i];
                        float dp = pl.x * pp.x + pl.y * pp.y + pl.z * pp.z + pl.w;

                        // Skip near-boundary points (sign is ambiguous within FP/renorm noise).
                        if (Mathf.Abs(dw) < 1e-2f) continue;
                        Assert.AreEqual(dw >= 0f, dp >= 0f,
                            $"plane {i} half-space mismatch at world {pw}: dw={dw:F4} dp={dp:F4}");
                        checks++;
                    }
                }
                Assert.Greater(checks, 100, "expected many half-space comparisons");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
