#nullable enable
using GPUGrass.Editor;
using NUnit.Framework;
using UnityEngine;

namespace GPUGrass.Tests
{
    /// <summary>EditMode tests for the placeholder blade-mesh builder (<see cref="GpuGrassBladeMesh.Build"/>).</summary>
    public sealed class BladeMeshTests
    {
        [Test]
        public void Build_HasExpectedTopology()
        {
            Mesh mesh = GpuGrassBladeMesh.Build(segments: 3);
            try
            {
                Assert.AreEqual(8, mesh.vertexCount, "3 segments → 4 rows × 2 verts = 8.");
                Assert.AreEqual(3 * 6, mesh.triangles.Length, "3 segments → 18 indices.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Build_UvVRunsZeroAtBaseToOneAtTip()
        {
            Mesh mesh = GpuGrassBladeMesh.Build(segments: 4);
            try
            {
                float minV = float.MaxValue, maxV = float.MinValue;
                foreach (Vector2 uv in mesh.uv) { minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y); }
                Assert.AreEqual(0f, minV, 1e-4f, "Base row uv.y must be 0 (shader reads it as bend factor).");
                Assert.AreEqual(1f, maxV, 1e-4f, "Tip row uv.y must be 1.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Build_TapersToAPointAtTheTipAndIsUnitHeight()
        {
            Mesh mesh = GpuGrassBladeMesh.Build(segments: 3, baseWidth: 0.2f);
            try
            {
                Vector3[] v = mesh.vertices;
                // Highest two verts (the tip row) must be (near) coincident in X → tapered to a point.
                float tipY = float.MinValue;
                foreach (var p in v) tipY = Mathf.Max(tipY, p.y);
                float tipSpread = 0f;
                float prevX = float.NaN;
                foreach (var p in v)
                {
                    if (!Mathf.Approximately(p.y, tipY)) continue;
                    if (!float.IsNaN(prevX)) tipSpread = Mathf.Abs(p.x - prevX);
                    prevX = p.x;
                }
                Assert.Less(tipSpread, 0.02f, "Tip row should taper to ~a point.");
                Assert.AreEqual(1f, mesh.bounds.size.y, 0.01f, "Blade mesh is unit height (scaled per-blade later).");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Build_SingleSegmentIsAQuad()
        {
            Mesh mesh = GpuGrassBladeMesh.Build(segments: 1);
            try
            {
                Assert.AreEqual(4, mesh.vertexCount);
                Assert.AreEqual(6, mesh.triangles.Length);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
