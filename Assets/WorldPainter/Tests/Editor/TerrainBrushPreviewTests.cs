#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Smoke tests for TerrainBrushPreview (Handles-based rewrite).
    /// The old disc-mesh tests (CreateUnitDisc) were removed when the mesh-based
    /// implementation was replaced with a pure-Handles ring + fill in Phase 3 of
    /// the MegaWorld brush-preview port.
    ///
    /// These tests verify the public/internal contract of the new implementation:
    ///   - Set() accepts valid inputs without throwing.
    ///   - HeightFn delegate compiles and can be constructed.
    /// </summary>
    [TestFixture]
    public sealed class TerrainBrushPreviewTests
    {
        [Test]
        public void Set_ValidInputs_DoesNotThrow()
        {
            // Verify that Set() accepts valid inputs without throwing.
            Assert.DoesNotThrow(() =>
            {
                TerrainBrushPreview.Set(
                    worldPoint: new Vector3(10f, 5f, 10f),
                    radius: 5f,
                    tint: new Color(0.3f, 0.7f, 1f, 0.6f),
                    shape: BrushShape.Circle,
                    height: null);
            });
        }

        [Test]
        public void Set_SquareShape_DoesNotThrow()
        {
            // Verify Square shape variant accepted without throwing.
            Assert.DoesNotThrow(() =>
            {
                TerrainBrushPreview.Set(
                    worldPoint: new Vector3(0f, 0f, 0f),
                    radius: 10f,
                    tint: Color.white,
                    shape: BrushShape.Square,
                    height: null);
            });
        }

        [Test]
        public void HeightFn_Delegate_CanBeConstructed()
        {
            // Verify the HeightFn delegate can be instantiated with a valid lambda.
            TerrainBrushPreview.HeightFn fn = (float wx, float wz, out float wy) =>
            {
                wy = 0f;
                return true;
            };
            bool result = fn(0f, 0f, out float height);
            Assert.IsTrue(result);
            Assert.AreEqual(0f, height);
        }
    }
}
