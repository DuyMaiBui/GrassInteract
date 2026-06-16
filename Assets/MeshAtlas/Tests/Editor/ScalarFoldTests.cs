using MeshAtlas.Editor.Baking;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class ScalarFoldTests
    {
        [Test]
        public void FoldAlbedo_MultipliesPerComponent()
        {
            var folded = ScalarFold.FoldAlbedo(new Color(0.5f, 0.5f, 0.5f, 1f), new Color(1f, 0f, 0f, 1f));
            Assert.That(folded.r, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(folded.g, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(folded.b, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void FoldAlbedo_WhiteTint_IsIdentity()
        {
            var src = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            Assert.AreEqual(src, ScalarFold.FoldAlbedo(src, Color.white));
        }

        [Test]
        public void FoldMask_WhiteSource_YieldsFactors()
        {
            // No mask map → source is white → folded R = metallic, A = smoothness.
            var folded = ScalarFold.FoldMask(Color.white, metallic: 0.7f, smoothness: 0.3f);
            Assert.That(folded.r, Is.EqualTo(0.7f).Within(1e-5f), "metallic in R");
            Assert.That(folded.a, Is.EqualTo(0.3f).Within(1e-5f), "smoothness in A");
        }

        [Test]
        public void FromMaterial_NullMaterial_ReturnsIdentity()
        {
            var f = ScalarFactors.FromMaterial(null);
            Assert.AreEqual(Color.white, f.BaseColor);
            Assert.That(f.Metallic, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(f.Smoothness, Is.EqualTo(0.5f).Within(1e-5f));
        }
    }
}
