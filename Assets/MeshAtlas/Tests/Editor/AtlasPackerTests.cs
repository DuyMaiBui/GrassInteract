using System.Collections.Generic;
using MeshAtlas.Editor.Packing;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class AtlasPackerTests
    {
        [Test]
        public void Pack_EmptyInput_Fails()
        {
            var result = new AtlasPacker().Pack(new List<Vector2Int>());
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error);
        }

        [Test]
        public void Pack_NonPositiveSize_Fails()
        {
            var result = new AtlasPacker().Pack(new List<Vector2Int> { new Vector2Int(0, 16) });
            Assert.IsFalse(result.Success);
        }

        [Test]
        public void Pack_SingleRect_FitsAndIsPowerOfTwo()
        {
            var result = new AtlasPacker(padding: 4).Pack(new List<Vector2Int> { new Vector2Int(100, 100) });
            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(Mathf.NextPowerOfTwo(result.AtlasSize), result.AtlasSize, "Atlas must be power-of-two.");
            Assert.AreEqual(1, result.Rects.Length);
        }

        [Test]
        public void Pack_RectsAreNonOverlappingAndInsideUnitSquare()
        {
            var sizes = new List<Vector2Int>
            {
                new Vector2Int(128, 128), new Vector2Int(64, 200),
                new Vector2Int(256, 64), new Vector2Int(100, 100), new Vector2Int(50, 300),
            };
            var result = new AtlasPacker(padding: 4, maxAtlasSize: 4096).Pack(sizes);
            Assert.IsTrue(result.Success, result.Error);

            foreach (var r in result.Rects)
            {
                Assert.GreaterOrEqual(r.xMin, -1e-4f);
                Assert.GreaterOrEqual(r.yMin, -1e-4f);
                Assert.LessOrEqual(r.xMax, 1f + 1e-4f);
                Assert.LessOrEqual(r.yMax, 1f + 1e-4f);
            }
            for (var i = 0; i < result.Rects.Length; i++)
            {
                for (var j = i + 1; j < result.Rects.Length; j++)
                {
                    Assert.IsFalse(Overlaps(result.Rects[i], result.Rects[j]),
                        $"Rect {i} overlaps rect {j}.");
                }
            }
        }

        [Test]
        public void Pack_PaddingGutter_KeepsNeighboursApart()
        {
            // Two equal tiles side-by-side; with padding the gap in pixels must be >= padding.
            const int atlasPad = 8;
            var sizes = new List<Vector2Int> { new Vector2Int(64, 64), new Vector2Int(64, 64) };
            var result = new AtlasPacker(padding: atlasPad).Pack(sizes);
            Assert.IsTrue(result.Success, result.Error);

            var aPx = ToPixels(result.Rects[0], result.AtlasSize);
            var bPx = ToPixels(result.Rects[1], result.AtlasSize);
            // If they share a column/row, the inter-region gap should be at least the padding.
            var gapX = GapBetween(aPx.xMin, aPx.xMax, bPx.xMin, bPx.xMax);
            var gapY = GapBetween(aPx.yMin, aPx.yMax, bPx.yMin, bPx.yMax);
            Assert.IsTrue(gapX >= atlasPad - 1 || gapY >= atlasPad - 1,
                "Neighbouring regions must be separated by at least the padding gutter.");
        }

        [Test]
        public void Pack_Overflow_ReturnsFailureNotCrash()
        {
            var sizes = new List<Vector2Int>();
            for (var i = 0; i < 20; i++)
            {
                sizes.Add(new Vector2Int(200, 200));
            }
            var result = new AtlasPacker(padding: 4, maxAtlasSize: 256).Pack(sizes);
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error);
        }

        private static bool Overlaps(Rect a, Rect b)
            => b.xMin < a.xMax - 1e-5f && b.xMax > a.xMin + 1e-5f
               && b.yMin < a.yMax - 1e-5f && b.yMax > a.yMin + 1e-5f;

        private static Rect ToPixels(Rect r, int atlas)
            => new Rect(r.x * atlas, r.y * atlas, r.width * atlas, r.height * atlas);

        private static float GapBetween(float aMin, float aMax, float bMin, float bMax)
        {
            if (bMin >= aMax)
            {
                return bMin - aMax;
            }
            if (aMin >= bMax)
            {
                return aMin - bMax;
            }
            return 0f; // overlapping on this axis
        }
    }
}
