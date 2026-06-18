using MeshAtlas.Editor.Packing;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class GridRectLayoutTests
    {
        [Test]
        public void Compute_ZeroCount_ReturnsEmpty()
        {
            Assert.AreEqual(0, GridRectLayout.Compute(0, 2, 2).Length);
        }

        [Test]
        public void Compute_TwoByOne_SplitsHorizontally()
        {
            var rects = GridRectLayout.Compute(2, 2, 1);
            Assert.AreEqual(2, rects.Length);
            AssertRect(new Rect(0f, 0f, 0.5f, 1f), rects[0]);
            AssertRect(new Rect(0.5f, 0f, 0.5f, 1f), rects[1]);
        }

        [Test]
        public void Compute_TwoByTwo_FillsLeftToRightTopToBottom()
        {
            var rects = GridRectLayout.Compute(4, 2, 2);
            Assert.AreEqual(4, rects.Length);
            // Cell 0 is top-left; UV origin is bottom-left, so the top row has the higher y.
            AssertRect(new Rect(0f, 0.5f, 0.5f, 0.5f), rects[0]); // top-left
            AssertRect(new Rect(0.5f, 0.5f, 0.5f, 0.5f), rects[1]); // top-right
            AssertRect(new Rect(0f, 0f, 0.5f, 0.5f), rects[2]); // bottom-left
            AssertRect(new Rect(0.5f, 0f, 0.5f, 0.5f), rects[3]); // bottom-right
        }

        [Test]
        public void Compute_DegenerateGrid_ClampsToAtLeastOne()
        {
            var rects = GridRectLayout.Compute(1, 0, 0);
            Assert.AreEqual(1, rects.Length);
            AssertRect(new Rect(0f, 0f, 1f, 1f), rects[0]);
        }

        [Test]
        public void SuggestGrid_IsSquareIsh()
        {
            Assert.AreEqual(new Vector2Int(1, 1), GridRectLayout.SuggestGrid(1));
            Assert.AreEqual(new Vector2Int(2, 1), GridRectLayout.SuggestGrid(2));
            Assert.AreEqual(new Vector2Int(2, 2), GridRectLayout.SuggestGrid(3));
            Assert.AreEqual(new Vector2Int(2, 2), GridRectLayout.SuggestGrid(4));
            Assert.AreEqual(new Vector2Int(3, 3), GridRectLayout.SuggestGrid(9));
        }

        private static void AssertRect(Rect expected, Rect actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f), "x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f), "y");
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(1e-5f), "width");
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(1e-5f), "height");
        }
    }
}
