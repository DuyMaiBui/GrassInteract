#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldPainter"/> Phase 2 splat-layer model:
    ///   – 5th add is rejected with an <see cref="System.InvalidOperationException"/>
    ///     (errors-over-fallbacks rule; hard cap = <see cref="WorldPainter.MAX_SPLAT_LAYERS"/>).
    ///   – Reorder via list manipulation changes blend priority
    ///     (channel 0 = R = highest priority, matches TerrainSplatWeightTests SSOT).
    /// </summary>
    [TestFixture]
    public sealed class WorldPainterSplatLayerTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static WorldPainter CreatePainter()
        {
            var go = new GameObject("TestWorldPainter");
            return go.AddComponent<WorldPainter>();
        }

        private static void DestroyPainter(WorldPainter painter)
        {
            if (painter != null)
                Object.DestroyImmediate(painter.gameObject);
        }

        private static SplatLayerDef MakeLayer(string name) =>
            new SplatLayerDef { name = name, tiling = Vector2.one };

        // ── Cap enforcement ───────────────────────────────────────────────────

        [Test]
        public void AddSplatLayer_UpToMax_Succeeds()
        {
            var painter = CreatePainter();
            try
            {
                for (int i = 0; i < WorldPainter.MAX_SPLAT_LAYERS; i++)
                    Assert.DoesNotThrow(
                        () => painter.AddSplatLayer(MakeLayer($"Layer{i}")),
                        $"Adding layer {i} should succeed (cap is {WorldPainter.MAX_SPLAT_LAYERS}).");

                Assert.AreEqual(WorldPainter.MAX_SPLAT_LAYERS, painter.SplatLayers.Count);
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void AddSplatLayer_FifthLayer_ThrowsInvalidOperationException()
        {
            var painter = CreatePainter();
            try
            {
                for (int i = 0; i < WorldPainter.MAX_SPLAT_LAYERS; i++)
                    painter.AddSplatLayer(MakeLayer($"Layer{i}"));

                // 5th add MUST throw — not silently drop.
                Assert.Throws<System.InvalidOperationException>(
                    () => painter.AddSplatLayer(MakeLayer("ExtraLayer")),
                    "Adding a 5th splat layer must throw InvalidOperationException.");

                // Count must still be capped at MAX, not 5.
                Assert.AreEqual(WorldPainter.MAX_SPLAT_LAYERS, painter.SplatLayers.Count,
                    "Layer count must stay at MAX_SPLAT_LAYERS after rejected add.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void AddSplatLayer_ErrorMessage_ContainsLayerName()
        {
            var painter = CreatePainter();
            try
            {
                for (int i = 0; i < WorldPainter.MAX_SPLAT_LAYERS; i++)
                    painter.AddSplatLayer(MakeLayer($"Layer{i}"));

                var ex = Assert.Throws<System.InvalidOperationException>(
                    () => painter.AddSplatLayer(MakeLayer("SurfacedError")));

                Assert.IsTrue(ex!.Message.Contains("SurfacedError"),
                    "Exception message must include the layer name that was rejected.");
                Assert.IsTrue(ex.Message.Contains(WorldPainter.MAX_SPLAT_LAYERS.ToString()),
                    "Exception message must include the cap value.");
            }
            finally { DestroyPainter(painter); }
        }

        // ── Reorder priority ──────────────────────────────────────────────────

        [Test]
        public void SplatLayers_Reorder_ChangesChannelIndex()
        {
            var painter = CreatePainter();
            try
            {
                painter.AddSplatLayer(MakeLayer("Alpha"));
                painter.AddSplatLayer(MakeLayer("Beta"));
                painter.AddSplatLayer(MakeLayer("Gamma"));

                // Initial order: Alpha=0, Beta=1, Gamma=2
                Assert.AreEqual("Alpha", painter.SplatLayers[0].name);
                Assert.AreEqual("Beta",  painter.SplatLayers[1].name);
                Assert.AreEqual("Gamma", painter.SplatLayers[2].name);

                // Simulate reorder: move index 2 → index 0 (Gamma becomes channel 0 = highest priority)
                var layers = painter.SplatLayers;
                var moved  = layers[2];
                layers.RemoveAt(2);
                layers.Insert(0, moved);

                Assert.AreEqual("Gamma", painter.SplatLayers[0].name,
                    "After reorder, Gamma must be at channel 0 (highest priority / R channel).");
                Assert.AreEqual("Alpha", painter.SplatLayers[1].name);
                Assert.AreEqual("Beta",  painter.SplatLayers[2].name);
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void SplatLayers_ReorderSwap_OverlapWinnerChanges()
        {
            // Channel 0 (R) always wins overlap in NormalizeWeights.
            // Verify that placing a layer at index 0 makes it the overlap winner.
            var painter = CreatePainter();
            try
            {
                painter.AddSplatLayer(MakeLayer("Ground")); // ch 0 = R = winner
                painter.AddSplatLayer(MakeLayer("Grass"));  // ch 1 = G

                // Before swap: Ground (index 0) is channel R — it's the winner.
                Assert.AreEqual("Ground", painter.SplatLayers[0].name);

                // Swap: move Grass to index 0.
                var layers = painter.SplatLayers;
                var grass   = layers[1];
                layers.RemoveAt(1);
                layers.Insert(0, grass);

                // After swap: Grass is channel 0 = new winner.
                Assert.AreEqual("Grass", painter.SplatLayers[0].name,
                    "After swap, Grass at index 0 becomes the R-channel (overlap winner).");
                Assert.AreEqual("Ground", painter.SplatLayers[1].name);
            }
            finally { DestroyPainter(painter); }
        }

        // ── ActiveSplatLayerIndex clamping ────────────────────────────────────

        [Test]
        public void ActiveSplatLayerIndex_Clamps_ToValidRange()
        {
            var painter = CreatePainter();
            try
            {
                painter.AddSplatLayer(MakeLayer("L0"));
                painter.AddSplatLayer(MakeLayer("L1"));

                painter.ActiveSplatLayerIndex = 99; // beyond range
                Assert.AreEqual(1, painter.ActiveSplatLayerIndex,
                    "ActiveSplatLayerIndex must clamp to splatLayers.Count-1.");

                painter.ActiveSplatLayerIndex = -5; // below range
                Assert.AreEqual(0, painter.ActiveSplatLayerIndex,
                    "ActiveSplatLayerIndex must clamp to 0 when set negative.");
            }
            finally { DestroyPainter(painter); }
        }
    }
}
