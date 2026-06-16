using MeshAtlas.Editor.Packing;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class UvRangeInspectorTests
    {
        [Test]
        public void InRangeMesh_NotFlagged()
        {
            var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.25f) };
            Assert.IsFalse(UvRangeInspector.HasOutOfRangeUv(uvs, out var count));
            Assert.AreEqual(0, count);
        }

        [Test]
        public void TiledUv_Flagged_WithCount()
        {
            var uvs = new[] { new Vector2(0f, 0f), new Vector2(1.5f, 0f), new Vector2(0f, 4f) };
            Assert.IsTrue(UvRangeInspector.HasOutOfRangeUv(uvs, out var count));
            Assert.AreEqual(2, count);
        }

        [Test]
        public void NegativeUv_Flagged()
        {
            var uvs = new[] { new Vector2(-0.2f, 0.5f) };
            Assert.IsTrue(UvRangeInspector.HasOutOfRangeUv(uvs, out _));
        }

        [Test]
        public void WithinEpsilon_NotFlagged()
        {
            var uvs = new[] { new Vector2(1f + UvRangeInspector.EPSILON * 0.5f, 0f) };
            Assert.IsFalse(UvRangeInspector.HasOutOfRangeUv(uvs, out _));
        }

        [Test]
        public void Null_NotFlagged()
        {
            Assert.IsFalse(UvRangeInspector.HasOutOfRangeUv(null, out var count));
            Assert.AreEqual(0, count);
        }
    }
}
