#nullable enable
using System.Collections.Generic;
using System.IO;
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
        private const string FALLBACK_FOLDER = "Assets/GPUGrass/Generated";

        /// <summary>
        /// Applies <paramref name="sharedConfig"/> to every active terrain in the scene. Skips terrains
        /// with null <c>terrainData</c>. All generated assets land in the per-scene folder. Calls
        /// <see cref="AssetDatabase.SaveAssets"/> once at the end (batched). Returns one status per terrain.
        /// </summary>
        public static List<GpuGrassTerrainStatus> SetupScene(GpuGrassConfig sharedConfig)
        {
            var statuses = new List<GpuGrassTerrainStatus>();
            string assetFolder = GetSceneAssetFolder();

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
        /// (<c>&lt;SceneName&gt;_GpuGrassConfig.asset</c>) inside the per-scene folder. Idempotent: a second
        /// call returns the same existing asset without creating a duplicate.
        /// </summary>
        public static GpuGrassConfig EnsureSharedConfig()
        {
            string folder = GetSceneAssetFolder();
            string path = $"{folder}/{GetSceneName()}_GpuGrassConfig.asset";

            var existing = AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(path);
            if (existing != null)
                return existing;

            var config = ScriptableObject.CreateInstance<GpuGrassConfig>();
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();
            return config;
        }

        // ── Per-scene asset folder (mirrors Unity baked-lighting layout) ───────

        /// <summary>The active scene's name, or "Untitled" when the scene is unsaved.</summary>
        public static string GetSceneName()
        {
            Scene scene = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
        }

        /// <summary>
        /// Returns (creating if needed) the per-scene asset folder: <c>&lt;sceneDir&gt;/&lt;SceneName&gt;/</c>,
        /// the same place Unity writes baked lighting. Falls back to <c>Assets/GPUGrass/Generated</c> when the
        /// scene has never been saved (no asset path).
        /// </summary>
        public static string GetSceneAssetFolder()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                GpuGrassAutoSetup.EnsureGeneratedFolder();
                return FALLBACK_FOLDER;
            }

            string dir = Path.GetDirectoryName(scene.path)!.Replace('\\', '/'); // e.g. Assets/Scenes
            string folder = $"{dir}/{scene.name}";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(dir, scene.name);
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
