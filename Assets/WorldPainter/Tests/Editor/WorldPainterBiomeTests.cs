#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// EditMode tests for Phase 5 biome composite brush:
    ///   - <see cref="BiomePreset"/> schema (channel count, summary)
    ///   - <see cref="BiomeChannelMask"/> flag semantics
    ///   - <see cref="WorldPainterBiomeContributionToggles"/> mute/unmute
    ///   - Multi-tile pin via <see cref="TerrainPaintTargetResolver.PinnedCoords"/>
    ///   - <see cref="WorldPainterState.ActiveBiomeLayerIndex"/> routing
    /// </summary>
    [TestFixture]
    public class WorldPainterBiomeTests
    {
        // ── BiomePreset schema ────────────────────────────────────────────────

        [Test]
        public void BiomePreset_DefaultHasZeroEnabledChannels()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            Assert.AreEqual(0, preset.EnabledChannelCount,
                "Fresh BiomePreset should have no channels enabled");
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void BiomePreset_ChannelSummary_WhenNoChannels_ReturnsNone()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            Assert.AreEqual("(no channels)", preset.ChannelSummary());
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void BiomePreset_ChannelSummary_ContainsEnabledChannelIcons()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            // Enable height and splat via SerializedObject to respect private fields.
            using var so = new UnityEditor.SerializedObject(preset);
            so.FindProperty("heightEnabled")!.boolValue = true;
            so.FindProperty("splatEnabled")!.boolValue  = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            string summary = preset.ChannelSummary();
            // Assert on plain-text channel names, not emoji glyphs: 🎨 and 🌿 share a
            // UTF-16 high surrogate (\uD83C), making ordinal emoji substring checks fragile.
            StringAssert.Contains("Height", summary, "Height channel should appear");
            StringAssert.Contains("Splat", summary, "Splat channel should appear");
            StringAssert.DoesNotContain("Grass", summary, "Grass channel should not appear");
            StringAssert.DoesNotContain("Props", summary, "Props channel should not appear");

            Object.DestroyImmediate(preset);
        }

        [Test]
        public void BiomePreset_EnabledChannelCount_CountsCorrectly()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            using var so = new UnityEditor.SerializedObject(preset);
            so.FindProperty("heightEnabled")!.boolValue = true;
            so.FindProperty("grassEnabled")!.boolValue  = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(2, preset.EnabledChannelCount);
            Object.DestroyImmediate(preset);
        }

        // ── BiomeChannelMask flag semantics ───────────────────────────────────

        [Test]
        public void BiomeChannelMask_None_HasNoFlagsSet()
        {
            Assert.AreEqual(BiomeChannelMask.None, BiomeChannelMask.None & BiomeChannelMask.Height);
        }

        [Test]
        public void BiomeChannelMask_SetAndClear_WorkCorrectly()
        {
            var mask = BiomeChannelMask.None;
            mask |= BiomeChannelMask.Grass;
            Assert.IsTrue(mask.HasFlag(BiomeChannelMask.Grass), "Grass should be muted");
            Assert.IsFalse(mask.HasFlag(BiomeChannelMask.Props), "Props should not be muted");

            mask &= ~BiomeChannelMask.Grass;
            Assert.IsFalse(mask.HasFlag(BiomeChannelMask.Grass), "Grass should be un-muted");
        }

        [Test]
        public void BiomeChannelMask_AllChannels_FitInSingleByte()
        {
            int allFlags = (int)(BiomeChannelMask.Height | BiomeChannelMask.Splat |
                                 BiomeChannelMask.Grass  | BiomeChannelMask.Props);
            Assert.Less(allFlags, 256, "All four channel flags should fit in a byte");
        }

        // ── Per-channel toggles ───────────────────────────────────────────────

        [Test]
        public void BiomeContributionToggles_DefaultMask_IsNone()
        {
            var toggles = new WorldPainterBiomeContributionToggles();
            Assert.AreEqual(BiomeChannelMask.None, toggles.CurrentMask,
                "Default mask should be None (all channels enabled)");
        }

        [Test]
        public void BiomeContributionToggles_ChannelContribution_HeightDisabled_ReturnsDisabled()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            // heightEnabled = false by default.
            string contribution = WorldPainterBiomeContributionToggles.ChannelContribution(
                preset, BiomeChannelMask.Height);
            Assert.AreEqual("(disabled)", contribution);
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void BiomeContributionToggles_ChannelContribution_HeightEnabled_ContainsDelta()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            using var so = new UnityEditor.SerializedObject(preset);
            so.FindProperty("heightEnabled")!.boolValue = true;
            so.FindProperty("heightDeltaM")!.floatValue = 2.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            string contribution = WorldPainterBiomeContributionToggles.ChannelContribution(
                preset, BiomeChannelMask.Height);
            StringAssert.Contains("Height", contribution);

            Object.DestroyImmediate(preset);
        }

        // ── Biome fan-out: N channels per stamp ───────────────────────────────

        [Test]
        public void BiomePreset_FanOutCount_MatchesEnabledChannels()
        {
            var preset = ScriptableObject.CreateInstance<BiomePreset>();
            using var so = new UnityEditor.SerializedObject(preset);
            so.FindProperty("heightEnabled")!.boolValue = true;
            so.FindProperty("splatEnabled")!.boolValue  = true;
            so.FindProperty("grassEnabled")!.boolValue  = false;
            so.FindProperty("propEnabled")!.boolValue   = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(2, preset.EnabledChannelCount,
                "Fan-out count should equal the number of enabled channels");
            Object.DestroyImmediate(preset);
        }

        // ── Multi-tile stream pin ─────────────────────────────────────────────

        [Test]
        public void PinnedCoords_AddedCoord_IsPinned()
        {
            TerrainPaintTargetResolver.PinnedCoords.Clear();
            var coord = new Vector2Int(3, 5);
            TerrainPaintTargetResolver.PinnedCoords.Add(coord);
            Assert.IsTrue(TerrainPaintTargetResolver.IsPinned(coord),
                "Coord should be pinned after Add");
        }

        [Test]
        public void PinnedCoords_AfterClear_NoPinned()
        {
            TerrainPaintTargetResolver.PinnedCoords.Add(new Vector2Int(1, 1));
            TerrainPaintTargetResolver.PinnedCoords.Clear();
            Assert.IsFalse(TerrainPaintTargetResolver.IsPinned(new Vector2Int(1, 1)),
                "Coord should not be pinned after Clear");
        }

        [Test]
        public void PinnedCoords_UnrelatedCoord_NotPinned()
        {
            TerrainPaintTargetResolver.PinnedCoords.Clear();
            TerrainPaintTargetResolver.PinnedCoords.Add(new Vector2Int(0, 0));
            Assert.IsFalse(TerrainPaintTargetResolver.IsPinned(new Vector2Int(9, 9)));
        }

        // ── ActiveBiomeLayerIndex routing ─────────────────────────────────────

        [Test]
        public void ActiveBiomeLayerIndex_NoSplatNoScatter_FirstBiomeAtIndex1()
        {
            // Create a WorldPainter with no splat/scatter layers, one biome.
            var go      = new GameObject("TestWP");
            var painter = go.AddComponent<WorldPainter>();
            var preset  = ScriptableObject.CreateInstance<BiomePreset>();
            painter.Biomes.Add(preset);

            // Stack layout: index 0 = Height, index 1 = first biome.
            WorldPainterState.ActiveLayerIndex = 1;
            int idx = WorldPainterState.ActiveBiomeLayerIndex(painter);
            Assert.AreEqual(0, idx,
                "First biome at stack index 1 (no splat/scatter) maps to biome list index 0");

            Object.DestroyImmediate(preset);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ActiveBiomeLayerIndex_WhenHeightSelected_ReturnsMinus1()
        {
            var go      = new GameObject("TestWP2");
            var painter = go.AddComponent<WorldPainter>();
            var preset  = ScriptableObject.CreateInstance<BiomePreset>();
            painter.Biomes.Add(preset);

            WorldPainterState.ActiveLayerIndex = 0; // Height row
            int idx = WorldPainterState.ActiveBiomeLayerIndex(painter);
            Assert.AreEqual(-1, idx, "Height row should not resolve to a biome index");

            Object.DestroyImmediate(preset);
            Object.DestroyImmediate(go);
        }

        // ── Atomic biome undo — mute mask ─────────────────────────────────────

        [Test]
        public void BiomeChannelMask_MutingAllChannels_YieldsFullMask()
        {
            var mask = BiomeChannelMask.Height | BiomeChannelMask.Splat |
                       BiomeChannelMask.Grass  | BiomeChannelMask.Props;
            Assert.IsTrue(mask.HasFlag(BiomeChannelMask.Height));
            Assert.IsTrue(mask.HasFlag(BiomeChannelMask.Splat));
            Assert.IsTrue(mask.HasFlag(BiomeChannelMask.Grass));
            Assert.IsTrue(mask.HasFlag(BiomeChannelMask.Props));
        }

        [Test]
        public void BiomeChannelMask_MutingProps_DoesNotAffectOtherChannels()
        {
            var mask = BiomeChannelMask.None;
            mask |= BiomeChannelMask.Props;

            Assert.IsTrue(mask.HasFlag(BiomeChannelMask.Props),   "Props muted");
            Assert.IsFalse(mask.HasFlag(BiomeChannelMask.Height), "Height not muted");
            Assert.IsFalse(mask.HasFlag(BiomeChannelMask.Splat),  "Splat not muted");
            Assert.IsFalse(mask.HasFlag(BiomeChannelMask.Grass),  "Grass not muted");
        }
    }
}
