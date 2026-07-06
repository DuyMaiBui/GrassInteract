#nullable enable
using GPUGrass.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="GpuGrassSceneWindow.ApplyMobilePreset"/> — the aggressive mobile
    /// defaults (docs/GPUGrass.md §8.2 #4) plus the opaque-shader guardrail (#2-preset, Guard 6). Locked
    /// values per the mobile-optimization plan's "Scope summary".
    /// </summary>
    public sealed class MobilePresetTests
    {
        private const string TEMP_FOLDER = "Assets/GPUGrass/__MobilePresetTest__";

        private GpuGrassConfig? config;
        private GpuGrassSceneWindow? window;
        private string? configPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER))
                AssetDatabase.CreateFolder("Assets/GPUGrass", "__MobilePresetTest__");

            this.configPath = $"{TEMP_FOLDER}/TestMobilePreset_Config.asset";
            var cfg = ScriptableObject.CreateInstance<GpuGrassConfig>();
            AssetDatabase.CreateAsset(cfg, this.configPath);
            this.config = AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(this.configPath);

            this.window = ScriptableObject.CreateInstance<GpuGrassSceneWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (this.window != null)
                Object.DestroyImmediate(this.window);

            // The preset's self-heal may have created a material wherever the active scene's bake
            // folder resolves to (mirrors GpuGrassAutoSetup's own idempotent behaviour) — clean it up by
            // its REAL path rather than assuming a folder convention.
            if (this.config != null && this.config.GrassMaterial != null)
            {
                string matPath = AssetDatabase.GetAssetPath(this.config.GrassMaterial);
                if (!string.IsNullOrEmpty(matPath))
                    AssetDatabase.DeleteAsset(matPath);
            }

            if (!string.IsNullOrEmpty(this.configPath) &&
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(this.configPath)))
                AssetDatabase.DeleteAsset(this.configPath);

            if (AssetDatabase.IsValidFolder(TEMP_FOLDER))
                AssetDatabase.DeleteAsset(TEMP_FOLDER);

            AssetDatabase.Refresh();
        }

        [Test]
        public void ApplyMobilePreset_WritesAllLockedValues()
        {
            this.window!.ApplyMobilePreset(this.config!);

            Assert.IsFalse(this.config!.EnableOcclusionCulling, "Occlusion must default OFF on mobile (#1).");
            Assert.IsTrue(this.config.EnableAdaptiveDensity, "Adaptive density must stay on.");
            Assert.AreEqual(new[] { 8f, 20f }, this.config.LodMaxDistances,
                "LOD switch distances must pull in to {8,20}.");
            Assert.AreEqual(60f, this.config.RenderCullDistance, 1e-3f,
                "Render cull distance must tighten to 60.");
            Assert.AreEqual(0.5f, this.config.TargetDensityPerSqM, 1e-3f, "Target density must drop to 0.5.");
            Assert.AreEqual(0.4f, this.config.MinDensity, 1e-3f, "Density floor must drop to 0.4.");
            Assert.AreEqual(ShadowCastingMode.Off, this.config.ShadowCastingMode, "Shadow casting must be off.");
            Assert.IsFalse(this.config.ReceiveShadows, "Receive shadows must be off.");
            Assert.AreEqual(GrassTierMode.Auto, this.config.TierMode, "Tier mode must stay Auto.");
        }

        [Test]
        public void ApplyMobilePreset_KeepsMaterialOnIndirectShader()
        {
            this.window!.ApplyMobilePreset(this.config!);

            Assert.IsNotNull(this.config!.GrassMaterial, "Preset must ensure a grass material exists.");
            Assert.AreEqual("GPUGrass/IndirectGrass", this.config.GrassMaterial!.shader.name,
                "The indirect renderer requires GPUGrass/IndirectGrass — any other shader silently renders " +
                "nothing (Guard 6).");
        }

        [Test]
        public void ApplyMobilePreset_DisablesWindPerlinKeyword()
        {
            this.window!.ApplyMobilePreset(this.config!);

            Assert.IsFalse(this.config!.GrassMaterial!.IsKeywordEnabled("_WIND_PERLIN"),
                "_WIND_PERLIN is ~16x the ALU cost of the default sin() wind — must stay off on mobile.");
        }

        [Test]
        public void WireRenderAssets_SyncsConfigAlphaClipOntoMaterial()
        {
            // Config alpha-clip ON → WireRenderAssets (run via the preset) must set the _Alphaclip float,
            // the _Cutoff, AND enable the _ALPHACLIP keyword. Float-without-keyword is the exact footgun that
            // made cutout grass render as solid quads — the keyword drives the #if defined(_ALPHACLIP) clip.
            this.config!.SetAlphaClip(true, 0.4f);
            // Persist BEFORE the preset: WireRenderAssets' material CreateAsset triggers a config reimport
            // that would revert an in-memory-only flag to its on-disk default (the same reimport-revert
            // footgun ApplyMobilePreset itself dodges). Real authoring saves via the Inspector — mirror that.
            EditorUtility.SetDirty(this.config);
            AssetDatabase.SaveAssets();
            this.window!.ApplyMobilePreset(this.config);

            Material mat = this.config.GrassMaterial!;
            Assert.IsTrue(mat.IsKeywordEnabled("_ALPHACLIP"),
                "Config AlphaClip=on must enable the _ALPHACLIP keyword (float alone is a no-op — clip compiles out).");
            Assert.AreEqual(1f, mat.GetFloat("_Alphaclip"), 1e-3f, "_Alphaclip float must be 1 when config AlphaClip is on.");
            Assert.AreEqual(0.4f, mat.GetFloat("_Cutoff"), 1e-3f, "_Cutoff must mirror the config's AlphaCutoff.");
        }

        [Test]
        public void WireRenderAssets_DisablesAlphaClipKeywordWhenConfigOff()
        {
            // Default config AlphaClip=off → keyword must be OFF (opaque blades). Guards against a stale
            // keyword surviving on a regenerated/reused material.
            this.window!.ApplyMobilePreset(this.config!);

            Assert.IsFalse(this.config!.GrassMaterial!.IsKeywordEnabled("_ALPHACLIP"),
                "Config AlphaClip=off must leave the _ALPHACLIP keyword disabled.");
        }
    }
}
