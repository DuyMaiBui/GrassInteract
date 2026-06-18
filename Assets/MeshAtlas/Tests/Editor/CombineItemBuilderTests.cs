using System.Collections.Generic;
using MeshAtlas.Editor.Combine;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class CombineItemBuilderTests
    {
        // Always-present editor shader, so tests don't depend on URP being resolvable.
        private static Material NewMaterial()
            => new Material(Shader.Find("Hidden/Internal-Colored"));

        private static Mesh TwoSubmeshQuad()
        {
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
            return mesh;
        }

        [Test]
        public void Build_SingleMeshTwoMaterials_AssignsEachSubmeshItsRect()
        {
            var mesh = TwoSubmeshQuad();
            var matA = NewMaterial();
            var matB = NewMaterial();
            var rectA = new Rect(0f, 0f, 0.5f, 0.5f);
            var rectB = new Rect(0.5f, 0.5f, 0.5f, 0.5f);
            var clip = new RendererClip { Mesh = mesh, Materials = new[] { matA, matB }, Transform = Matrix4x4.identity };
            var rects = new Dictionary<Material, Rect> { { matA, rectA }, { matB, rectB } };
            var warnings = new List<string>();

            try
            {
                var items = CombineItemBuilder.Build(new[] { clip }, rects, warnings);

                Assert.AreEqual(2, items.Count, "One item per submesh.");
                Assert.AreEqual(0, warnings.Count, "All materials resolved → no warnings.");
                var item0 = items.Find(i => i.SubMeshIndex == 0);
                var item1 = items.Find(i => i.SubMeshIndex == 1);
                Assert.IsNotNull(item0);
                Assert.IsNotNull(item1);
                Assert.AreEqual(rectA, item0.SubRect, "Submesh 0 → material A rect.");
                Assert.AreEqual(rectB, item1.SubRect, "Submesh 1 → material B rect.");
                // Single clip at the origin → pivot is zero → transform unchanged.
                Assert.AreEqual(Matrix4x4.identity, item0.Transform);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(matA);
                Object.DestroyImmediate(matB);
            }
        }

        [Test]
        public void Build_MaterialWithoutRect_DropsSubmeshWithWarning()
        {
            var mesh = TwoSubmeshQuad();
            var matA = NewMaterial();
            var matB = NewMaterial();
            var clip = new RendererClip { Mesh = mesh, Materials = new[] { matA, matB }, Transform = Matrix4x4.identity };
            var rects = new Dictionary<Material, Rect> { { matA, new Rect(0f, 0f, 1f, 1f) } }; // B missing
            var warnings = new List<string>();

            try
            {
                var items = CombineItemBuilder.Build(new[] { clip }, rects, warnings);

                Assert.AreEqual(1, items.Count, "Only the resolvable submesh becomes an item.");
                Assert.AreEqual(0, items[0].SubMeshIndex);
                Assert.AreEqual(1, warnings.Count, "The dropped submesh is reported, never silent.");
                StringAssert.Contains("submesh 1", warnings[0]);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(matA);
                Object.DestroyImmediate(matB);
            }
        }

        [Test]
        public void Build_NullOrEmptyInputs_ReturnEmpty()
        {
            Assert.AreEqual(0, CombineItemBuilder.Build(null, new Dictionary<Material, Rect>(), new List<string>()).Count);
            Assert.AreEqual(0, CombineItemBuilder.Build(new List<RendererClip>(), new Dictionary<Material, Rect>(), null).Count);
        }
    }
}
