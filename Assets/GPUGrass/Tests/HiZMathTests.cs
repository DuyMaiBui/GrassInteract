#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode unit tests for the pure-math helpers on <see cref="GpuGrassHiZ"/>:
    ///   <see cref="GpuGrassHiZ.TryProjectAabbToScreenRect"/> — AABB→screen rect + nearest depth.
    ///   <see cref="GpuGrassHiZ.SelectMip"/> — rect pixel size → coarsest covering mip.
    ///
    /// These run without a GPU context (pure CPU math). They guard the two correctness properties
    /// that gate Phase-3 sign-off:
    ///   1. Conservative: the screen rect CONTAINS the projected AABB (never under-reports).
    ///   2. Fail-open: behind-camera / degenerate input → returns false → caller keeps the chunk.
    /// </summary>
    public sealed class HiZMathTests
    {
        // ── TryProjectAabbToScreenRect ────────────────────────────────────────

        /// <summary>
        /// An AABB centred directly in front of a standard perspective camera should project to
        /// a screen rect that:
        ///   • lies inside [0,1]×[0,1],
        ///   • is centred roughly at (0.5, 0.5),
        ///   • has positive width and height,
        ///   • has a positive nearest depth (in front of camera).
        /// </summary>
        [Test]
        public void TryProjectAabbToScreenRect_KnownAabb_ReturnsExpectedRectAndDepth()
        {
            // Build a simple perspective view-proj: camera at origin, looking down -Z, 60° FOV.
            // Unity column-major: projection * view applied to column vectors.
            Matrix4x4 view = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one).inverse;
            // Perspective: 60° FOV, aspect 1:1, near=0.1, far=100.
            Matrix4x4 proj = Matrix4x4.Perspective(60f, 1f, 0.1f, 100f);
            // Unity GPU projection matrix (Y-flip + platform depth convention).
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);
            Matrix4x4 viewProj = gpuProj * view;

            // AABB centred at (0, 0, -10): 2m cube, fully in front of camera.
            var aabb = new Bounds(new Vector3(0f, 0f, -10f), new Vector3(2f, 2f, 2f));

            bool ok = GpuGrassHiZ.TryProjectAabbToScreenRect(aabb, viewProj, out Rect rect, out float nearDepth);

            Assert.IsTrue(ok, "Expected projection to succeed for an AABB fully in front of camera.");

            // Rect should be inside [0,1] (clamped).
            Assert.GreaterOrEqual(rect.xMin, 0f, "xMin must be >= 0");
            Assert.GreaterOrEqual(rect.yMin, 0f, "yMin must be >= 0");
            Assert.LessOrEqual(rect.xMax, 1f, "xMax must be <= 1");
            Assert.LessOrEqual(rect.yMax, 1f, "yMax must be <= 1");

            // Centred near (0.5, 0.5) — AABB is at the origin looking down -Z with identity view.
            Assert.AreEqual(0.5f, rect.center.x, 0.05f, "X centre should be ~0.5 for centred AABB.");
            Assert.AreEqual(0.5f, rect.center.y, 0.05f, "Y centre should be ~0.5 for centred AABB.");

            // Must have positive extent.
            Assert.Greater(rect.width,  0f, "Rect width must be > 0.");
            Assert.Greater(rect.height, 0f, "Rect height must be > 0.");

            // Nearest depth must be positive (AABB is 9–11m in front of camera).
            Assert.Greater(nearDepth, 0f, "Nearest depth must be > 0 for a forward AABB.");
        }

        /// <summary>
        /// An AABB placed entirely behind the camera (positive Z in Unity's default camera space)
        /// should return <c>false</c>. The caller must KEEP the chunk (fail-open).
        /// </summary>
        [Test]
        public void TryProjectAabbToScreenRect_BehindCamera_ReturnsFalse()
        {
            Matrix4x4 view = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one).inverse;
            Matrix4x4 proj = Matrix4x4.Perspective(60f, 1f, 0.1f, 100f);
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);
            Matrix4x4 viewProj = gpuProj * view;

            // AABB fully behind camera (positive Z = behind in Unity camera space).
            var aabb = new Bounds(new Vector3(0f, 0f, 10f), new Vector3(2f, 2f, 2f));

            bool ok = GpuGrassHiZ.TryProjectAabbToScreenRect(aabb, viewProj, out _, out _);

            // Must return false — caller keeps chunk (fail-open guarantee).
            Assert.IsFalse(ok, "Behind-camera AABB must return false (fail-open: keep chunk).");
        }

        // ── SelectMip ────────────────────────────────────────────────────────

        /// <summary>
        /// A 1×1 pixel rect should select mip 0 (finest).
        /// A rect exactly equal to 1 pixel wide selects mip 0.
        /// </summary>
        [Test]
        public void SelectMip_SmallRect_ReturnsMip0()
        {
            var rect = new Rect(0, 0, 1f, 1f); // 1×1 pixel
            int mip = GpuGrassHiZ.SelectMip(rect, 512, 10);
            Assert.AreEqual(0, mip, "1-pixel rect should map to mip 0.");
        }

        /// <summary>
        /// A rect of width 4 pixels selects mip 2 (texelSize = 4 = 1&lt;&lt;2).
        /// </summary>
        [Test]
        public void SelectMip_RectSize4_ReturnsMip2()
        {
            var rect = new Rect(0, 0, 4f, 4f); // 4×4 pixels
            int mip = GpuGrassHiZ.SelectMip(rect, 512, 10);
            Assert.AreEqual(2, mip, "4-pixel rect should map to mip 2 (texelSize=4=1<<2).");
        }

        /// <summary>
        /// A very large rect (larger than the pyramid) must be clamped to mipCount-1.
        /// </summary>
        [Test]
        public void SelectMip_VeryLargeRect_ClampsToMaxMip()
        {
            var rect = new Rect(0, 0, 9999f, 9999f);
            int mipCount = 5;
            int mip = GpuGrassHiZ.SelectMip(rect, 512, mipCount);
            Assert.AreEqual(mipCount - 1, mip, "Oversized rect must clamp to mipCount-1.");
        }

        /// <summary>
        /// mipCount = 1 → always returns 0 regardless of rect size.
        /// </summary>
        [Test]
        public void SelectMip_SingleMip_AlwaysReturns0()
        {
            var rect = new Rect(0, 0, 512f, 512f);
            int mip = GpuGrassHiZ.SelectMip(rect, 512, 1);
            Assert.AreEqual(0, mip, "Single-mip pyramid must always return 0.");
        }

        /// <summary>
        /// A zero-size rect (degenerate) should return the coarsest mip so the cull never
        /// false-rejects on a pathological input (fail-open: keep chunk).
        /// </summary>
        [Test]
        public void SelectMip_ZeroSizeRect_ReturnsCoarsestMip()
        {
            var rect = new Rect(0, 0, 0f, 0f);
            int mipCount = 6;
            int mip = GpuGrassHiZ.SelectMip(rect, 512, mipCount);
            Assert.AreEqual(mipCount - 1, mip,
                "Zero-size rect should return the coarsest mip (fail-open for degenerate input).");
        }
    }
}
