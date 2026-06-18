#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Geometry tests for <see cref="TerrainBrushPreview.BuildPerimeterPoints"/>.
    ///
    /// All tests call the pure internal method with a null HeightFn so Y falls back to
    /// hitPoint.y — we are only asserting XZ geometry, not terrain conformance.
    /// Constants (SQUARE_SAMPLES_PER_EDGE, CIRCLE_SEGMENTS_MIN/MAX) are referenced
    /// directly from TerrainBrushPreview so tests never hardcode magic numbers.
    /// </summary>
    [TestFixture]
    public sealed class TerrainBrushPreviewTests
    {
        // ── Square geometry ───────────────────────────────────────────────────

        [Test]
        public void Square_PointCount_IsFourEdgesSampledPlusClosingPoint()
        {
            const float radius = 5f;
            var pts = BuildSquare(Vector3.zero, radius);

            // 4 edges × SQUARE_SAMPLES_PER_EDGE body points, plus 1 closing duplicate.
            int expected = 4 * TerrainBrushPreview.SQUARE_SAMPLES_PER_EDGE + 1;
            Assert.AreEqual(expected, pts.Count,
                $"Expected {expected} points (4 edges × {TerrainBrushPreview.SQUARE_SAMPLES_PER_EDGE} + 1 close).");
        }

        [Test]
        public void Square_XZExtents_AreExactlyPlusMinusRadius()
        {
            const float radius = 7f;
            var pts = BuildSquare(Vector3.zero, radius);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            // Exclude the closing duplicate (last point == first point).
            for (int i = 0; i < pts.Count - 1; ++i)
            {
                minX = Mathf.Min(minX, pts[i].x);
                maxX = Mathf.Max(maxX, pts[i].x);
                minZ = Mathf.Min(minZ, pts[i].z);
                maxZ = Mathf.Max(maxZ, pts[i].z);
            }

            Assert.AreEqual(-radius, minX, 1e-4f, "Min X must be -radius.");
            Assert.AreEqual( radius, maxX, 1e-4f, "Max X must be +radius.");
            Assert.AreEqual(-radius, minZ, 1e-4f, "Min Z must be -radius.");
            Assert.AreEqual( radius, maxZ, 1e-4f, "Max Z must be +radius.");
        }

        [Test]
        public void Square_AllFourCorners_ArePresent()
        {
            const float radius = 4f;
            const float eps    = 1e-4f;
            var pts = BuildSquare(Vector3.zero, radius);

            bool HasCorner(float cx, float cz)
            {
                foreach (var p in pts)
                    if (Mathf.Abs(p.x - cx) < eps && Mathf.Abs(p.z - cz) < eps)
                        return true;
                return false;
            }

            Assert.IsTrue(HasCorner(-radius, -radius), "Missing corner (-r, -r).");
            Assert.IsTrue(HasCorner( radius, -radius), "Missing corner (+r, -r).");
            Assert.IsTrue(HasCorner( radius,  radius), "Missing corner (+r, +r).");
            Assert.IsTrue(HasCorner(-radius,  radius), "Missing corner (-r, +r).");
        }

        [Test]
        public void Square_LoopIsClosed_LastPointEqualsFirst()
        {
            var pts = BuildSquare(Vector3.zero, 3f);
            Assert.Greater(pts.Count, 1, "Perimeter must have at least 2 points.");
            Assert.AreEqual(pts[0], pts[pts.Count - 1], "Last point must equal first (closed loop).");
        }

        [Test]
        public void Square_HitPointOffset_IsRespected()
        {
            // All XZ points must be offset by the hitPoint XZ.
            var origin = new Vector3(10f, 0f, 20f);
            const float radius = 5f;
            var pts = BuildSquare(origin, radius);

            foreach (var p in pts)
            {
                Assert.GreaterOrEqual(p.x, origin.x - radius - 1e-4f, "Point X below expected min.");
                Assert.LessOrEqual   (p.x, origin.x + radius + 1e-4f, "Point X above expected max.");
                Assert.GreaterOrEqual(p.z, origin.z - radius - 1e-4f, "Point Z below expected min.");
                Assert.LessOrEqual   (p.z, origin.z + radius + 1e-4f, "Point Z above expected max.");
            }
        }

        // ── Circle geometry ───────────────────────────────────────────────────

        [Test]
        public void Circle_PointCount_IsWithinAdaptiveRange()
        {
            // radius = 10m → circumference ≈ 62.8m → raw = ceil(62.8/1.5) = 42 → clamped to [16,128].
            var pts = BuildCircle(Vector3.zero, 10f);

            // Subtract 1 for the closing point.
            int bodyCount = pts.Count - 1;
            Assert.GreaterOrEqual(bodyCount, TerrainBrushPreview.CIRCLE_SEGMENTS_MIN,
                "Body point count must be >= CIRCLE_SEGMENTS_MIN.");
            Assert.LessOrEqual(bodyCount, TerrainBrushPreview.CIRCLE_SEGMENTS_MAX,
                "Body point count must be <= CIRCLE_SEGMENTS_MAX.");
        }

        [Test]
        public void Circle_AllBodyPoints_LieOnRadius()
        {
            var origin = new Vector3(5f, 0f, 5f);
            const float radius = 8f;
            const float eps    = 1e-3f;
            var pts = BuildCircle(origin, radius);

            // Exclude the closing duplicate.
            for (int i = 0; i < pts.Count - 1; ++i)
            {
                float dx   = pts[i].x - origin.x;
                float dz   = pts[i].z - origin.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                Assert.AreEqual(radius, dist, eps,
                    $"Point {i} at dist {dist} is not on the expected radius {radius}.");
            }
        }

        [Test]
        public void Circle_LoopIsClosed_LastPointEqualsFirst()
        {
            var pts = BuildCircle(Vector3.zero, 5f);
            Assert.Greater(pts.Count, 1, "Perimeter must have at least 2 points.");
            Assert.AreEqual(pts[0], pts[pts.Count - 1], "Last point must equal first (closed loop).");
        }

        [Test]
        public void Circle_SmallRadius_ClampedToMinSegments()
        {
            // radius = 0.01m → circumference ≈ 0.063m → raw = 1 → clamped to MIN.
            var pts = BuildCircle(Vector3.zero, 0.01f);
            int bodyCount = pts.Count - 1;
            Assert.AreEqual(TerrainBrushPreview.CIRCLE_SEGMENTS_MIN, bodyCount,
                "Tiny radius must clamp to CIRCLE_SEGMENTS_MIN body points.");
        }

        [Test]
        public void Circle_LargeRadius_ClampedToMaxSegments()
        {
            // radius = 1000m → circumference ≈ 6283m → raw > 4000 → clamped to MAX.
            var pts = BuildCircle(Vector3.zero, 1000f);
            int bodyCount = pts.Count - 1;
            Assert.AreEqual(TerrainBrushPreview.CIRCLE_SEGMENTS_MAX, bodyCount,
                "Large radius must clamp to CIRCLE_SEGMENTS_MAX body points.");
        }

        // ── Lift compensation (root-scale float fix) ──────────────────────────

        [Test]
        public void Lift_IsConstant_IndependentOfRadius()
        {
            // null HeightFn → Y = origin.y + lift. origin.y = 0 → perimeter Y == lift.
            // The lift must NOT grow with brush radius: a radius-proportional lift floats large
            // brushes above the terrain, and a floating ring parallax-shifts off the cursor when the
            // scene camera is angled — the "brush preview doesn't follow the mouse" bug.
            var small = new List<Vector3>();
            var big   = new List<Vector3>();
            TerrainBrushPreview.BuildPerimeterPoints(Vector3.zero, 5f,   BrushShape.Circle, null, small);
            TerrainBrushPreview.BuildPerimeterPoints(Vector3.zero, 200f, BrushShape.Circle, null, big);

            Assert.AreEqual(TerrainBrushPreview.Y_OFFSET, small[0].y, 1e-3f, "Lift must equal the constant Y_OFFSET.");
            Assert.AreEqual(TerrainBrushPreview.Y_OFFSET, big[0].y,   1e-3f,
                "Lift must stay constant for a 40× larger radius (no parallax float).");
        }

        [Test]
        public void Lift_CompensatedScale_ScalesConstantLift()
        {
            // Under a root scaled ×3, liftScale = 1/3 so the painting-space lift is divided by 3 —
            // after the ×3 root matrix maps it to world the on-screen lift is unchanged (root-scale fix).
            const float liftScale = 1f / 3f;
            var output = new List<Vector3>();
            TerrainBrushPreview.BuildPerimeterPoints(Vector3.zero, 20f, BrushShape.Circle, null, output, liftScale);

            Assert.AreEqual(TerrainBrushPreview.Y_OFFSET * liftScale, output[0].y, 1e-3f,
                "liftScale must scale the constant lift (root-scale world-invariance).");
        }

        // ── Set() smoke test (keep one; confirms the public API compiles/runs) ─

        [Test]
        public void Set_ValidInputs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                TerrainBrushPreview.Set(
                    new Vector3(10f, 5f, 10f), 5f,
                    new Color(0.3f, 0.7f, 1f), BrushShape.Circle, null));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Build a square perimeter via the pure method; returns the output list.
        private static List<Vector3> BuildSquare(Vector3 origin, float radius)
        {
            var output = new List<Vector3>();
            TerrainBrushPreview.BuildPerimeterPoints(origin, radius, BrushShape.Square, null, output);
            return output;
        }

        // Build a circle perimeter via the pure method; returns the output list.
        private static List<Vector3> BuildCircle(Vector3 origin, float radius)
        {
            var output = new List<Vector3>();
            TerrainBrushPreview.BuildPerimeterPoints(origin, radius, BrushShape.Circle, null, output);
            return output;
        }
    }
}
