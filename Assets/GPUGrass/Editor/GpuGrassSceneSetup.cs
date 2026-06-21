#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GPUGrass.Editor
{
    /// <summary>
    /// Status of a single terrain after a scene-wide setup pass.
    /// </summary>
    public struct GpuGrassTerrainStatus
    {
        /// <summary>Terrain GameObject name.</summary>
        public string Name;
        /// <summary>Baked blade count (0 if no detail is painted and PlacementSource = DetailLayer).</summary>
        public int BladeCount;
        /// <summary>Device tier resolved by the controller on the last Rebuild.</summary>
        public GrassDeviceTier Tier;
    }

    /// <summary>
    /// Scene-level GPUGrass setup: applies one shared <see cref="GpuGrassConfig"/> to every active
    /// <see cref="Terrain"/> in the open scene, each terrain keeping its own per-terrain bake asset. All
    /// generated assets (config, bakes, material) live in a per-scene folder next to the scene file —
    /// mirroring how Unity stores baked lighting (<c>&lt;sceneDir&gt;/&lt;SceneName&gt;/</c>).
    /// </summary>
    public static class GpuGrassSceneSetup
    {
        private const string GPUGRASS_ROOT = "Assets/GPUGrass";

        /// <summary>
        /// Applies <paramref name="sharedConfig"/> to every active terrain in the scene. Skips terrains
        /// with null <c>terrainData</c>. All generated assets land in the per-scene bake folder. Calls
        /// <see cref="AssetDatabase.SaveAssets"/> once at the end (batched). Returns one status per terrain.
        /// </summary>
        public static List<GpuGrassTerrainStatus> SetupScene(GpuGrassConfig sharedConfig)
        {
            var statuses = new List<GpuGrassTerrainStatus>();
            string assetFolder = EnsureSceneBakeFolder();

            Terrain[] terrains = ResolveActiveTerrains();
            foreach (var terrain in terrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;

                int bladeCount = GpuGrassAutoSetup.SetupOnTerrain(terrain, sharedConfig, assetFolder);

                var controller = terrain.GetComponent<GpuGrassController>();
                var tier = controller != null ? controller.ResolvedTier : GrassDeviceTier.Disabled;

                statuses.Add(new GpuGrassTerrainStatus
                {
                    Name       = terrain.name,
                    BladeCount = bladeCount,
                    Tier       = tier,
                });
            }

            AssetDatabase.SaveAssets();
            return statuses;
        }

        /// <summary>
        /// Loads (or creates) the scene-shared config asset, named after the active scene
        /// (<c>&lt;SceneName&gt;_GpuGrassConfig.asset</c>) inside the per-scene bake folder
        /// (<c>Assets/GPUGrass/&lt;SceneName&gt;Bake/</c>), creating the folder when needed. Idempotent: a
        /// second call returns the same existing asset without creating a duplicate.
        /// </summary>
        public static GpuGrassConfig EnsureSharedConfig()
        {
            string folder = EnsureSceneBakeFolder();
            string path = GetSceneConfigPath();

            var existing = AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(path);
            if (existing != null)
                return existing;

            var config = ScriptableObject.CreateInstance<GpuGrassConfig>();
            AssetDatabase.CreateAsset(config, path); // names the object after the file → WireRenderAssets uses it

            // Auto-wire render assets on creation: GrassCull compute + GPUGrass/IndirectGrass shader + a fresh
            // material (in the per-scene bake folder). Lets a brand-new config render immediately — no need to
            // run Setup & Bake first just to get a material. (LOD mesh is still assigned manually.)
            GpuGrassAutoSetup.WireRenderAssets(config, folder);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>
        /// Returns the existing shared config for the ACTIVE scene without creating anything, or
        /// <c>null</c> if none exists yet. Used by the window to auto-assign on scene change.
        /// </summary>
        public static GpuGrassConfig? TryFindSceneConfig()
            => AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(GetSceneConfigPath());

        // ── Per-scene bake folder (Assets/GPUGrass/<SceneName>Bake) ────────────

        /// <summary>The active scene's name, or "Untitled" when the scene is unsaved.</summary>
        public static string GetSceneName()
        {
            Scene scene = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
        }

        /// <summary>The per-scene bake folder path (NOT created): <c>Assets/GPUGrass/&lt;SceneName&gt;Bake</c>.</summary>
        public static string GetSceneBakeFolderPath() => $"{GPUGRASS_ROOT}/{GetSceneName()}Bake";

        /// <summary>The active scene's shared-config asset path (NOT created).</summary>
        public static string GetSceneConfigPath()
            => $"{GetSceneBakeFolderPath()}/{GetSceneName()}_GpuGrassConfig.asset";

        /// <summary>
        /// Ensures + returns the per-scene bake folder <c>Assets/GPUGrass/&lt;SceneName&gt;Bake</c> (the home
        /// for the scene's config, material, and per-terrain bakes). Creates <c>Assets/GPUGrass</c> first if
        /// it is somehow missing.
        /// </summary>
        public static string EnsureSceneBakeFolder()
        {
            if (!AssetDatabase.IsValidFolder(GPUGRASS_ROOT))
                AssetDatabase.CreateFolder("Assets", "GPUGrass");

            string folder = GetSceneBakeFolderPath();
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(GPUGRASS_ROOT, $"{GetSceneName()}Bake");
            return folder;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Terrain[] ResolveActiveTerrains()
        {
            var active = Terrain.activeTerrains;
            if (active != null && active.Length > 0)
                return active;

            return Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        }
    }
}
