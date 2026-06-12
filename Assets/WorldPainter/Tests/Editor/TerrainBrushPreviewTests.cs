#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for the conforming brush disc geometry (TerrainBrushPreview.CreateUnitDisc).
    ///  - Vertex count = 1 (center) + rings × segments.
    ///  - All verts lie within the unit circle (radius ≤ 0.5) in the XZ plane, Y=0.
    ///  - UV maps the disc texture: center at (0.5,0.5), border verts at distance 0.5.
    ///  - Triangles are non-empty and index in range.
    /// </summary>
    [TestFixture]
    public sealed class TerrainBrushPreviewTests
    {
        [Test]
        public void CreateUnitDisc_VertexCount_IsCenterPlusRingGrid()
        {
            const int rings = 6, segments = 24;
            Mesh mesh = TerrainBrushPreview.CreateUnitDisc(rings, segments);
            try
            {
                Assert.AreEqual(1 + rings * segments, mesh.vertexCount);
                Assert.Greater(mesh.triangles.Length, 0, "Disc must have triangles.");
                foreach (int i in mesh.triangles)
                    Assert.Less(i, mesh.vertexCount, "Triangle index out of range.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void CreateUnitDisc_AllVerts_WithinUnitCircle_FlatXZ()
        {
            Mesh mesh = TerrainBrushPreview.CreateUnitDisc(10, 48);
            try
            {
                foreach (Vector3 p in mesh.vertices)
                {
                    Assert.AreEqual(0f, p.y, 1e-5f, "Disc verts must be flat (Y=0); height is applied at draw.");
                    float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                    Assert.LessOrEqual(r, 0.5f + 1e-4f, $"Vert radius {r} exceeds unit disc (0.5).");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void CreateUnitDisc_Uv_CentersTextureAndReachesBorder()
        {
            Mesh mesh = TerrainBrushPreview.CreateUnitDisc(4, 16);
            try
            {
                Vector2[] uv = mesh.uv;
                Vector3[] v  = mesh.vertices;
                Assert.AreEqual(new Vector2(0.5f, 0.5f), uv[0], "Center vert UV must be texture center.");

                // Outer-ring verts (radius 0.5) must map to UV distance 0.5 from center.
                float maxUvDist = 0f;
                for (int i = 0; i < v.Length; ++i)
                {
                    float r = Mathf.Sqrt(v[i].x * v[i].x + v[i].z * v[i].z);
                    if (r > 0.49f)
                    {
                        float uvDist = Vector2.Distance(uv[i], new Vector2(0.5f, 0.5f));
                        maxUvDist = Mathf.Max(maxUvDist, uvDist);
                    }
                }
                Assert.AreEqual(0.5f, maxUvDist, 1e-3f, "Border verts must reach the texture edge (UV dist 0.5).");
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
