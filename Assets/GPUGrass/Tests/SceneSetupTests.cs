#nullable enable
using System.Collections.Generic;
using GPUGrass.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GPUGrass.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="GpuGrassSceneSetup"/> against synthetic Terrains. Verifies that the
    /// scene-setup loop assigns one shared config to every controller, produces per-terrain distinct bakes,
    /// skips terrains without TerrainData, and that the per-scene bake-folder paths are deterministic.
    ///
    /// Cleanup is robust to the per-scene asset layout: the test config lives in an isolated temp folder,
    /// and the assets SetupScene actually produces (material + per-terrain bakes, wherever they land) are
    /// tracked by their real paths and deleted in TearDown — no hard-coded Generated/ assumptions.
    /// </summary>
    public sealed class SceneSetupTests
    {
        private const float SIZE = 20f, HEIGHT = 5f;
        private const string TEMP_FOLDER = "Assets/GPUGrass/__SceneSetupTest__";

        // Per-test teardown tracking.
        private readonly List<Object> toDestroy   = new();
        private readonly List<string> assetsToDelete = new();

        // ── Setup / TearDown ──────────────────────────────────────────────────

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in this.toDestroy)
            {
                if (obj != null)
                    Object.DestroyImmediate(obj);
            }
            this.toDestroy.Clear();

            foreach (var path in this.assetsToDelete)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    AssetDatabase.DeleteAsset(path);
            }
            this.assetsToDelete.Clear();

            if (AssetDatabase.IsValidFolder(TEMP_FOLDER))
                AssetDatabase.DeleteAsset(TEMP_FOLDER);

            AssetDatabase.Refresh();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Creates a minimal flat terrain with a detail prototype; registers it for cleanup.</summary>
        private Terrain CreateSyntheticTerrain(string name, Vector3 position)
        {
            var data = new TerrainData { heightmapResolution = 33 };
            data.size = new Vector3(SIZE, HEIGHT, SIZE);
            data.SetDetailResolution(32, 16);

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            data.detailPrototypes = new[]
            {
                new DetailPrototype
                {
                    prototypeTexture = tex,
                    usePrototypeMesh = false,
                    renderMode       = DetailRenderMode.GrassBillboard,
                },
            };

            this.toDestroy.Add(tex);
            this.toDestroy.Add(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = name;
            go.transform.position = position;
            this.toDestroy.Add(go);

            return go.GetComponent<Terrain>();
        }

        /// <summary>Creates a config asset in the isolated temp folder and registers it for cleanup.</summary>
        private GpuGrassConfig CreateConfigAsset(string assetName)
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER))
                AssetDatabase.CreateFolder("Assets/GPUGrass", "__SceneSetupTest__");

            string path = $"{TEMP_FOLDER}/{assetName}.asset";
            var cfg = ScriptableObject.CreateInstance<GpuGrassConfig>();
            AssetDatabase.CreateAsset(cfg, path);
            this.assetsToDelete.Add(path);
            return AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(path)!;
        }

        /// <summary>
        /// Registers the assets <see cref="GpuGrassSceneSetup.SetupScene"/> actually produced — the material
        /// auto-wired onto the config and each terrain's bake — by their REAL paths (wherever the per-scene
        /// layout placed them), so TearDown removes them regardless of folder convention.
        /// </summary>
        private void TrackOutputs(GpuGrassConfig cfg, params Terrain[] terrains)
        {
            if (cfg != null && cfg.GrassMaterial != null)
            {
                string m = AssetDatabase.GetAssetPath(cfg.GrassMaterial);
                if (!string.IsNullOrEmpty(m)) this.assetsToDelete.Add(m);
            }
            foreach (var t in terrains)
            {
                var c = t.GetComponent<GpuGrassController>();
                if (c != null && c.Bake != null)
                {
                    string b = AssetDatabase.GetAssetPath(c.Bake);
                    if (!string.IsNullOrEmpty(b)) this.assetsToDelete.Add(b);
                }
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void SetupScene_AssignsSameConfigInstanceToEveryController()
        {
            var t1 = this.CreateSyntheticTerrain("T_Shared_1", Vector3.zero);
            var t2 = this.CreateSyntheticTerrain("T_Shared_2", new Vector3(SIZE + 10f, 0f, 0f));

            GpuGrassConfig shared = this.CreateConfigAsset("TestShared_Config");

            List<GpuGrassTerrainStatus> results = GpuGrassSceneSetup.SetupScene(shared);
            this.TrackOutputs(shared, t1, t2);

            Assert.AreEqual(2, results.Count, "Expected one status per terrain.");

            var ctrl1 = t1.GetComponent<GpuGrassController>();
            var ctrl2 = t2.GetComponent<GpuGrassController>();
            Assert.IsNotNull(ctrl1, "Controller must be added to terrain 1.");
            Assert.IsNotNull(ctrl2, "Controller must be added to terrain 2.");

            // Both controllers must reference the exact same config instance (Guard 1).
            Assert.AreSame(shared, ctrl1!.Config,
                "Controller on terrain 1 must reference the shared config (not a clone).");
            Assert.AreSame(shared, ctrl2!.Config,
                "Controller on terrain 2 must reference the shared config (not a clone).");
        }

        [Test]
        public void SetupScene_GivesEachTerrainDistinctBake()
        {
            var t1 = this.CreateSyntheticTerrain("T_Bake_1", Vector3.zero);
            var t2 = this.CreateSyntheticTerrain("T_Bake_2", new Vector3(SIZE + 10f, 0f, 0f));

            GpuGrassConfig shared = this.CreateConfigAsset("TestBake_Config");

            GpuGrassSceneSetup.SetupScene(shared);
            this.TrackOutputs(shared, t1, t2);

            var ctrl1 = t1.GetComponent<GpuGrassController>();
            var ctrl2 = t2.GetComponent<GpuGrassController>();
            Assert.IsNotNull(ctrl1?.Bake, "Terrain 1 must have a bake asset.");
            Assert.IsNotNull(ctrl2?.Bake, "Terrain 2 must have a bake asset.");

            // Bake assets must be distinct objects (Guard 2).
            Assert.AreNotSame(ctrl1!.Bake, ctrl2!.Bake,
                "Each terrain must have its own GpuGrassBakeData asset, not a shared one.");

            string path1 = AssetDatabase.GetAssetPath(ctrl1.Bake);
            string path2 = AssetDatabase.GetAssetPath(ctrl2.Bake);
            Assert.AreNotEqual(path1, path2,
                "Bake assets must live at different paths (per-terrain).");
        }

        [Test]
        public void SetupScene_SkipsTerrainsWithoutTerrainData()
        {
            // Create one valid terrain + one bare GO with a Terrain component but no TerrainData.
            var valid = this.CreateSyntheticTerrain("T_Valid", Vector3.zero);

            var badGo = new GameObject("T_NullData");
            badGo.AddComponent<Terrain>(); // terrainData defaults to null
            this.toDestroy.Add(badGo);

            GpuGrassConfig shared = this.CreateConfigAsset("TestSkip_Config");

            // Must not throw even though badGo.GetComponent<Terrain>().terrainData == null.
            List<GpuGrassTerrainStatus> results = null!;
            Assert.DoesNotThrow(
                () => results = GpuGrassSceneSetup.SetupScene(shared),
                "SetupScene must not throw when a terrain has null terrainData.");
            this.TrackOutputs(shared, valid);

            // Only the valid terrain should appear in results (bad one skipped).
            Assert.AreEqual(1, results.Count,
                "The null-terrainData terrain must be skipped; only the valid one processes.");
            Assert.AreEqual(valid.name, results[0].Name);
        }

        [Test]
        public void SceneBakeFolder_PathsAreDeterministicAndScoped()
        {
            // Pure path-shape contract — no side effects (does NOT create or delete the real scene config,
            // which would be unsafe to mutate from a test).
            string folder1 = GpuGrassSceneSetup.GetSceneBakeFolderPath();
            string folder2 = GpuGrassSceneSetup.GetSceneBakeFolderPath();
            Assert.AreEqual(folder1, folder2, "Bake folder path must be deterministic.");
            StringAssert.StartsWith("Assets/GPUGrass/", folder1, "Bake folder must live under Assets/GPUGrass.");
            StringAssert.EndsWith("Bake", folder1, "Bake folder must end with 'Bake'.");

            string cfg1 = GpuGrassSceneSetup.GetSceneConfigPath();
            Assert.AreEqual(cfg1, GpuGrassSceneSetup.GetSceneConfigPath(), "Config path must be deterministic.");
            StringAssert.StartsWith(folder1 + "/", cfg1, "Config must live inside the bake folder.");
            StringAssert.EndsWith("_GpuGrassConfig.asset", cfg1, "Config must use the _GpuGrassConfig suffix.");
        }
    }
}
