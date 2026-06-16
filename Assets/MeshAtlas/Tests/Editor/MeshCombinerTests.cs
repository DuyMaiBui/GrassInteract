using System.Collections.Generic;
using MeshAtlas.Editor.Combine;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class MeshCombinerTests
    {
        [Test]
        public void Combine_SharedVertexMultiMaterial_RemapsEachSubmeshIntoItsRect()
        {
            // A quad with two submeshes that SHARE vertices 0 and 2. Each submesh must
            // remap into a different atlas rect — proving per-submesh vertex extraction.
            var mesh = new Mesh();
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
            });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
            });
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 0, 2, 3 }, 1);

            var rectA = new Rect(0f, 0f, 0.5f, 0.5f);
            var rectB = new Rect(0.5f, 0.5f, 0.5f, 0.5f);
            var items = new List<CombineItem>
            {
                new CombineItem { Mesh = mesh, SubMeshIndex = 0, Transform = Matrix4x4.identity, SubRect = rectA },
                new CombineItem { Mesh = mesh, SubMeshIndex = 1, Transform = Matrix4x4.identity, SubRect = rectB },
            };

            var combined = MeshCombiner.Combine(items);
            try
            {
                Assert.AreEqual(1, combined.subMeshCount, "Merged to a single submesh.");
                Assert.AreEqual(6, combined.vertexCount, "Each submesh extracted to its own 3 verts (no shared-vertex bleed).");
                foreach (var uv in combined.uv)
                {
                    Assert.IsTrue(InRect(uv, rectA) || InRect(uv, rectB), $"UV {uv} fell outside both atlas rects.");
                }
            }
            finally
            {
                Object.DestroyImmediate(combined);
                Object.DestroyImmediate(mesh);
            }
        }

        private static bool InRect(Vector2 uv, Rect r)
            => uv.x >= r.xMin - 1e-4f && uv.x <= r.xMax + 1e-4f
               && uv.y >= r.yMin - 1e-4f && uv.y <= r.yMax + 1e-4f;
    }
}
