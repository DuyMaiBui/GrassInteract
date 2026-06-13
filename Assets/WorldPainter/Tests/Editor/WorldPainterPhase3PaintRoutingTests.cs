#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Phase 3 paint-routing tests:
    ///   - <see cref="WorldPainterState.ActiveSplatChannel"/> default, set, and Reset.
    ///   - <see cref="BrushToolTargets.ResolvePropLayer"/> returns the active unified
    ///     <see cref="PropLayer"/> and falls back to null when kind or id doesn't match.
    ///   - <see cref="SplatDispatch.ResolveChannel"/> uses <see cref="WorldPainterState.ActiveSplatChannel"/>
    ///     for the unified path and falls back to 0 for the legacy path.
    /// </summary>
    [TestFixture]
    public sealed class WorldPainterPhase3PaintRoutingTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static WorldPainter CreatePainter()
        {
            var go = new GameObject("TestWorldPainterP3");
            return go.AddComponent<WorldPainter>();
        }

        private static void DestroyPainter(WorldPainter painter)
        {
            if (painter != null)
                Object.DestroyImmediate(painter.gameObject);
        }

        [TearDown]
        public void TearDown() => WorldPainterState.Reset();

        // ── ActiveSplatChannel ────────────────────────────────────────────────

        [Test]
        public void ActiveSplatChannel_DefaultIsMinusOne()
        {
            WorldPainterState.Reset();
            Assert.AreEqual(-1, WorldPainterState.ActiveSplatChannel,
                "ActiveSplatChannel must default to -1 (no channel selected).");
        }

        [Test]
        public void ActiveSplatChannel_SetAndGet()
        {
            WorldPainterState.ActiveSplatChannel = 2;
            Assert.AreEqual(2, WorldPainterState.ActiveSplatChannel,
                "ActiveSplatChannel must retain the assigned value.");
        }

        [Test]
        public void ActiveSplatChannel_ResetClearsToMinusOne()
        {
            WorldPainterState.ActiveSplatChannel = 3;
            WorldPainterState.Reset();
            Assert.AreEqual(-1, WorldPainterState.ActiveSplatChannel,
                "Reset must clear ActiveSplatChannel back to -1.");
        }

        // ── SplatDispatch.ResolveChannel (unified path) ───────────────────────

        [Test]
        public void ResolveChannel_UnifiedSplatActive_UsesActiveSplatChannel()
        {
            var painter = CreatePainter();
            try
            {
                WorldPainterState.ActiveSplatChannel = 2;
                WorldPainterState.SetActiveLayer("UnifiedSplat", WorldPainterState.PaintLayerKind.Splat);

                int channel = SplatDispatch.ResolveChannel(painter);

                Assert.AreEqual(2, channel,
                    "ResolveChannel must return ActiveSplatChannel (2) when unified splat is active.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolveChannel_UnifiedSplatActive_ChannelZero_ReturnZero()
        {
            var painter = CreatePainter();
            try
            {
                WorldPainterState.ActiveSplatChannel = 0;
                WorldPainterState.SetActiveLayer("UnifiedSplat", WorldPainterState.PaintLayerKind.Splat);

                int channel = SplatDispatch.ResolveChannel(painter);

                Assert.AreEqual(0, channel,
                    "ResolveChannel must return 0 when ActiveSplatChannel is 0 and unified splat is active.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolveChannel_UnifiedSplatActive_NoChannelSet_FallsBackToLegacy()
        {
            var painter = CreatePainter();
            try
            {
                // Kind=Splat but ActiveSplatChannel = -1 (user selected layer row but not a channel slot).
                WorldPainterState.ActiveSplatChannel = -1;
                WorldPainterState.SetActiveLayer("UnifiedSplat", WorldPainterState.PaintLayerKind.Splat);

                int channel = SplatDispatch.ResolveChannel(painter);

                // ActiveSplatChannel < 0 → fall through to legacy path → 0.
                Assert.AreEqual(0, channel,
                    "ResolveChannel must fall back to 0 when ActiveSplatChannel is -1.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolveChannel_LegacyPath_NoUnifiedLayer_ReturnsFallback()
        {
            var painter = CreatePainter();
            try
            {
                WorldPainterState.Reset();

                int channel = SplatDispatch.ResolveChannel(painter);

                // Legacy path: no splat layers → index arithmetic yields -1 → clamps to 0.
                Assert.AreEqual(0, channel,
                    "ResolveChannel must return 0 as fallback when no unified splat layer is active.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolveChannel_UnifiedMeadowActive_NotSplat_FallsBackToLegacy()
        {
            var painter = CreatePainter();
            try
            {
                WorldPainterState.ActiveSplatChannel = 3;
                // Active kind is Meadow — must NOT use ActiveSplatChannel.
                WorldPainterState.SetActiveLayer("Grass", WorldPainterState.PaintLayerKind.Meadow);

                int channel = SplatDispatch.ResolveChannel(painter);

                // Meadow kind → legacy path → no splat layers → 0.
                Assert.AreEqual(0, channel,
                    "ResolveChannel must ignore ActiveSplatChannel when active kind is not Splat.");
            }
            finally { DestroyPainter(painter); }
        }

        // ── BrushToolTargets.ResolvePropLayer ─────────────────────────────────

        [Test]
        public void ResolvePropLayer_NoActiveLayer_ReturnsNull()
        {
            var painter = CreatePainter();
            try
            {
                WorldPainterState.Reset();
                Assert.IsNull(BrushToolTargets.ResolvePropLayer(painter),
                    "ResolvePropLayer must return null when no layer is active.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolvePropLayer_ActiveKindIsNotProp_ReturnsNull()
        {
            var painter = CreatePainter();
            try
            {
                WorldPainterState.SetActiveLayer("SomeGrass", WorldPainterState.PaintLayerKind.Meadow);
                Assert.IsNull(BrushToolTargets.ResolvePropLayer(painter),
                    "ResolvePropLayer must return null when active kind is Meadow (not Prop).");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolvePropLayer_MapIsNull_ReturnsNull()
        {
            var painter = CreatePainter();
            try
            {
                // No map assigned.
                WorldPainterState.SetActiveLayer("SomeProp", WorldPainterState.PaintLayerKind.Prop);
                Assert.IsNull(BrushToolTargets.ResolvePropLayer(painter),
                    "ResolvePropLayer must return null when painter.Map is null.");
            }
            finally { DestroyPainter(painter); }
        }

        [Test]
        public void ResolvePropLayer_ActiveIdMatchesPropLayer_ReturnsPropLayer()
        {
            var painter   = CreatePainter();
            var map       = ScriptableObject.CreateInstance<WorldMapAsset>();
            var propLayer = ScriptableObject.CreateInstance<PropLayer>();
            propLayer.name = "Layer_Tree";
            map.RegisterSurfaceLayer(propLayer);

            try
            {
                // Assign map via the public Map property.
                painter.Map = map;

                WorldPainterState.SetActiveLayer("Layer_Tree", WorldPainterState.PaintLayerKind.Prop);

                var resolved = BrushToolTargets.ResolvePropLayer(painter);

                Assert.IsNotNull(resolved,
                    "ResolvePropLayer must return the PropLayer when id and kind match.");
                Assert.AreSame(propLayer, resolved,
                    "ResolvePropLayer must return the exact PropLayer instance from map.SurfaceLayers.");
            }
            finally
            {
                DestroyPainter(painter);
                Object.DestroyImmediate(propLayer);
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void ResolvePropLayer_ActiveIdDoesNotMatch_ReturnsNull()
        {
            var painter   = CreatePainter();
            var map       = ScriptableObject.CreateInstance<WorldMapAsset>();
            var propLayer = ScriptableObject.CreateInstance<PropLayer>();
            propLayer.name = "Layer_Tree";
            map.RegisterSurfaceLayer(propLayer);

            try
            {
                painter.Map = map;

                // Active id points to a different name.
                WorldPainterState.SetActiveLayer("Layer_OtherTree", WorldPainterState.PaintLayerKind.Prop);

                Assert.IsNull(BrushToolTargets.ResolvePropLayer(painter),
                    "ResolvePropLayer must return null when id doesn't match any PropLayer.");
            }
            finally
            {
                DestroyPainter(painter);
                Object.DestroyImmediate(propLayer);
                Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void ResolvePropLayer_ActiveIdMatchesGrassLayer_ReturnsNull()
        {
            var painter    = CreatePainter();
            var map        = ScriptableObject.CreateInstance<WorldMapAsset>();
            var grassLayer = ScriptableObject.CreateInstance<GrassLayer>();
            grassLayer.name = "Layer_Meadow";
            map.RegisterSurfaceLayer(grassLayer);

            try
            {
                painter.Map = map;

                // Active kind is Prop but id names a GrassLayer — must return null.
                WorldPainterState.SetActiveLayer("Layer_Meadow", WorldPainterState.PaintLayerKind.Prop);

                Assert.IsNull(BrushToolTargets.ResolvePropLayer(painter),
                    "ResolvePropLayer must return null when the named layer is a GrassLayer not a PropLayer.");
            }
            finally
            {
                DestroyPainter(painter);
                Object.DestroyImmediate(grassLayer);
                Object.DestroyImmediate(map);
            }
        }
    }
}
