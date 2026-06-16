using MeshAtlas.Editor.Baking;
using NUnit.Framework;
using UnityEngine;

namespace MeshAtlas.Tests
{
    public sealed class EdgeDilationTests
    {
        [Test]
        public void Dilate_BleedsCoveredColorIntoNeighbour()
        {
            // 3x1 strip: only the left pixel is covered (red). One dilation pass should
            // bleed red into the middle pixel.
            const int w = 3, h = 1;
            var pixels = new[] { Color.red, Color.clear, Color.clear };
            var covered = new[] { true, false, false };

            EdgeDilation.Dilate(pixels, covered, w, h, iterations: 1);

            Assert.AreEqual(Color.red, pixels[1], "Middle pixel should take the covered neighbour color.");
            Assert.IsTrue(covered[1]);
            Assert.IsFalse(covered[2], "Far pixel not reached in a single pass.");
        }

        [Test]
        public void Dilate_TwoPasses_ReachesFarPixel()
        {
            const int w = 3, h = 1;
            var pixels = new[] { Color.green, Color.clear, Color.clear };
            var covered = new[] { true, false, false };

            EdgeDilation.Dilate(pixels, covered, w, h, iterations: 2);

            Assert.AreEqual(Color.green, pixels[2]);
            Assert.IsTrue(covered[2]);
        }

        [Test]
        public void Dilate_FullyCovered_NoChange()
        {
            const int w = 2, h = 1;
            var pixels = new[] { Color.red, Color.blue };
            var covered = new[] { true, true };

            EdgeDilation.Dilate(pixels, covered, w, h, iterations: 3);

            Assert.AreEqual(Color.red, pixels[0]);
            Assert.AreEqual(Color.blue, pixels[1]);
        }

        [Test]
        public void Dilate_NullArgs_NoThrow()
        {
            Assert.DoesNotThrow(() => EdgeDilation.Dilate(null, null, 4, 4, 1));
        }
    }
}
