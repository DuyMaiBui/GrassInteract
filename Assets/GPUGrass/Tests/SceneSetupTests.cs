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
    /// skips terrains without TerrainData, and that <see cref="GpuGrassSceneSetup.EnsureSharedConfig"/> is
    /// idempotent.
    /// </summary>
    public sealed class SceneSetupTests
    {
        private const float SIZE = 20f, HEIGHT = 5f;
        private const string SHARED_CONFIG_PATH = "Assets/GPUGrass/Generated/SceneGrassConfig.asset";

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

        /// <summary>Creates a config asset at <paramref name="path"/> and registers it for cleanup.</summary>
        private GpuGrassConfig CreateConfigAsset(string path)
        {
            EnsureGeneratedFolder();
            var cfg = ScriptableObject.CreateInstance<GpuGrassConfig>();
            AssetDatabase.CreateAsset(cfg, path);
            this.assetsToDelete.Add(path);
            return AssetDatabase.LoadAssetAtPath<GpuGrassConfig>(path)!;
        }

        private static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/GPUGrass"))
                AssetDatabase.CreateFolder("Assets", "GPUGrass");
            if (!AssetDatabase.IsValidFolder("Assets/GPUGrass/Generated"))
                AssetDatabase.CreateFolder("Assets/GPUGrass", "Generated");
        }

        /// <summary>Registers all bake assets that SetupScene creates for a set of terrain names.</summary>
        private void TrackGeneratedAssets(params string[] terrainNames)
        {
            foreach (var n in terrainNames)
            {
                // Possible paths for bake + any render assets (blade, mat).
                this.assetsToDelete.Add($"Assets/GPUGrass/Generated/{n}_GpuGrassBake.asset");
                this.assetsToDelete.Add($"Assets/GPUGrass/Generated/{n}_GpuGrassBlade.asset");
                this.assetsToDelete.Add($"Assets/GPUGrass/Generated/{n}_GpuGrassMat.mat");
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void SetupScene_AssignsSameConfigInstanceToEveryController()
        {
            var t1 = this.CreateSyntheticTerrain("T_Shared_1", Vector3.zero);
            var t2 = this.CreateSyntheticTerrain("T_Shared_2", new Vector3(SIZE + 10f, 0f, 0f));
            this.TrackGeneratedAssets(t1.name, t2.name);

            string cfgPath = "Assets/GPUGrass/Generated/TestShared_Config.asset";
            GpuGrassConfig shared = this.CreateConfigAsset(cfgPath);

            List<GpuGrassTerrainStatus> results = GpuGrassSceneSetup.SetupScene(shared);

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
            this.TrackGeneratedAssets(t1.name, t2.name);

            string cfgPath = "Assets/GPUGrass/Generated/TestBake_Config.asset";
            GpuGrassConfig shared = this.CreateConfigAsset(cfgPath);

            GpuGrassSceneSetup.SetupScene(shared);

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
            this.TrackGeneratedAssets(valid.name);

            var badGo = new GameObject("T_NullData");
            badGo.AddComponent<Terrain>(); // terrainData defaults to null
            this.toDestroy.Add(badGo);

            string cfgPath = "Assets/GPUGrass/Generated/TestSkip_Config.asset";
            GpuGrassConfig shared = this.CreateConfigAsset(cfgPath);

            // Must not throw even though badGo.GetComponent<Terrain>().terrainData == null.
            List<GpuGrassTerrainStatus> results = null!;
            Assert.DoesNotThrow(
                () => results = GpuGrassSceneSetup.SetupScene(shared),
                "SetupScene must not throw when a terrain has null terrainData.");

            // Only the valid terrain should appear in results (bad one skipped).
            Assert.AreEqual(1, results.Count,
                "The null-terrainData terrain must be skipped; only the valid one processes.");
            Assert.AreEqual(valid.name, results[0].Name);
        }

        [Test]
        public void EnsureSharedConfig_IsIdempotent()
        {
            // Pre-delete the shared config so EnsureSharedConfig creates it fresh.
            AssetDatabase.DeleteAsset(SHARED_CONFIG_PATH);
            this.assetsToDelete.Add(SHARED_CONFIG_PATH);

            GpuGrassConfig first  = GpuGrassSceneSetup.EnsureSharedConfig();
            GpuGrassConfig second = GpuGrassSceneSetup.EnsureSharedConfig();

            Assert.IsNotNull(first,  "EnsureSharedConfig must return a non-null config.");
            Assert.IsNotNull(second, "Second call must also return a non-null config.");

            string path1 = AssetDatabase.GetAssetPath(first);
            string path2 = AssetDatabase.GetAssetPath(second);
            Assert.AreEqual(path1, path2,
                "Both calls must resolve to the same asset path (idempotent — no duplicate).");
            Assert.AreSame(first, second,
                "Both calls must return the same loaded asset instance.");
        }
    }
}
