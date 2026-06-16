using MeshAtlas.Editor.Packing;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class UVRemapperTests
    {
        private static readonly Rect SubRect = new Rect(0.25f, 0.5f, 0.25f, 0.25f);

        [Test]
        public void Remap_Origin_MapsToSubRectPosition()
        {
            var mapped = UVRemapper.Remap(new Vector2(0f, 0f), SubRect);
            Assert.AreEqual(SubRect.position, mapped);
        }

        [Test]
        public void Remap_One_MapsToSubRectFarCorner()
        {
            var mapped = UVRemapper.Remap(new Vector2(1f, 1f), SubRect);
            Assert.That(mapped.x, Is.EqualTo(SubRect.xMax).Within(1e-5f));
            Assert.That(mapped.y, Is.EqualTo(SubRect.yMax).Within(1e-5f));
        }

        [Test]
        public void Remap_Midpoint_MapsToSubRectCenter()
        {
            var mapped = UVRemapper.Remap(new Vector2(0.5f, 0.5f), SubRect);
            Assert.That(mapped.x, Is.EqualTo(SubRect.center.x).Within(1e-5f));
            Assert.That(mapped.y, Is.EqualTo(SubRect.center.y).Within(1e-5f));
        }

        [Test]
        public void Remap_Batch_MatchesScalarRemap()
        {
            var uvs = new[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.3f, 0.7f) };
            var batch = UVRemapper.Remap(uvs, SubRect);
            Assert.AreEqual(uvs.Length, batch.Length);
            for (var i = 0; i < uvs.Length; i++)
            {
                Assert.AreEqual(UVRemapper.Remap(uvs[i], SubRect), batch[i]);
            }
        }

        [Test]
        public void Remap_Batch_Null_ReturnsEmpty()
        {
            Assert.AreEqual(0, UVRemapper.Remap(null, SubRect).Length);
        }
    }
}
