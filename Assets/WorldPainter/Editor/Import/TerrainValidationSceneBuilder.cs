#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Editor utility: builds a 2-tile validation scene for multi-tile render + B1 UV fix testing.
    ///
    /// Menu: Tools/GPU Terrain/Create 2-Tile Validation Scene
    ///
    /// Outputs:
    ///   Assets/GpuTerrain/Demo/TileA_0_0.asset      — radial hill, tileCoord (0,0)
    ///   Assets/GpuTerrain/Demo/TileB_1_0.asset      — diagonal ridges, tileCoord (1,0)
    ///   Assets/GpuTerrain/Demo/ValidationLayerSet.asset
    ///   Assets/GpuTerrain/Demo/TerrainValidation.unity
    ///
    /// ONE GpuTerrainRenderer with a tiles array of size 2.  Both tiles are always
    /// visible in the validation scene; one shared lodRangesM governs both.
    /// </summary>
    public static class TerrainValidationSceneBuilder
    {
        private const string DEMO_DIR    = "Assets/GpuTerrain/Demo";
        private const string SCENE_PATH  = DEMO_DIR + "/TerrainValidation.unity";
        private const string TILE_A_PATH = DEMO_DIR + "/TileA_0_0.asset";
        private const string TILE_B_PATH = DEMO_DIR + "/TileB_1_0.asset";
        private const string LAYER_PATH  = DEMO_DIR + "/ValidationLayerSet.asset";

        private const float MIN_HEIGHT = 0f;
        private const float MAX_HEIGHT = 40f;

        [MenuItem("Tools/GPU Terrain/Create 2-Tile Validation Scene")]
        public static void CreateValidationScene()
        {
            // 1. Ensure Demo folder exists.
            if (!AssetDatabase.IsValidFolder(DEMO_DIR))
                AssetDatabase.CreateFolder("Assets/GpuTerrain", "Demo");

            // 2. Generate tile assets.
            TerrainTileAsset tileA = CreateOrReplaceTile(
                TILE_A_PATH, new Vector2Int(0, 0), GenerateRadialHill);
            TerrainTileAsset tileB = CreateOrReplaceTile(
                TILE_B_PATH, new Vector2Int(1, 0), GenerateDiagonalRidges);

            // 3. Generate layer set (1-layer trivial: layer 0 all white).
            TerrainLayerSet layerSet = CreateOrReplaceLayerSet(LAYER_PATH);

            // 4. Locate required assets from project.
            ComputeShader? cullCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainNodeCull.compute");
            Material? patchMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/GpuTerrain/Materials/TerrainPatch.mat");
            if (patchMat == null)
            {
                // Fallback: create material from shader if .mat doesn't exist yet.
                Shader? patchShader = Shader.Find("GpuTerrain/TerrainPatch");
                if (patchShader == null)
                    patchShader = AssetDatabase.LoadAssetAtPath<Shader>(
                        "Assets/GpuTerrain/Shaders/TerrainPatch.shader");
                if (patchShader != null)
                {
                    patchMat = new Material(patchShader) { name = "TerrainPatch_Validation" };
                    AssetDatabase.CreateAsset(patchMat, DEMO_DIR + "/TerrainPatch_Validation.mat");
                }
            }

            if (cullCompute == null)
                Debug.LogWarning("[TerrainValidation] Could not find TerrainNodeCull.compute — " +
                    "renderer will warn on enable. Assign manually in the scene.");
            if (patchMat == null)
                Debug.LogWarning("[TerrainValidation] Could not find/create TerrainPatch material. " +
                    "Assign manually in the scene.");

            // 5. Build the scene.
            BuildScene(tileA, tileB, layerSet, cullCompute, patchMat);

            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainValidation] 2-tile validation scene created at: " + SCENE_PATH);
        }

        // ── Tile generation ───────────────────────────────────────────────────

        private static TerrainTileAsset CreateOrReplaceTile(
            string path, Vector2Int coord,
            System.Func<int, float, float, byte[]> heightGen)
        {
            int res      = TerrainWorldGrid.DEFAULT_HEIGHT_RES;
            int splatRes = TerrainWorldGrid.DEFAULT_SPLAT_RES;

            TerrainTileAsset? existing = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(path);
            TerrainTileAsset tile = existing != null
                ? existing
                : ScriptableObject.CreateInstance<TerrainTileAsset>();

            tile.tileCoord  = coord;
            tile.heightRes  = res;
            tile.splatRes   = splatRes;
            tile.minHeight  = MIN_HEIGHT;
            tile.maxHeight  = MAX_HEIGHT;
            tile.heightData = heightGen(res, MIN_HEIGHT, MAX_HEIGHT);
            tile.splatData  = GenerateSplatAllLayer0(splatRes);

            if (existing == null)
                AssetDatabase.CreateAsset(tile, path);
            else
                EditorUtility.SetDirty(tile);

            Debug.Log($"[TerrainValidation] Saved tile {coord} → {path} " +
                $"(height={res}² valid={tile.IsHeightValid}, splat={splatRes}² valid={tile.IsSplatValid})");
            return tile;
        }

        /// <summary>Radial hill: peak in tile centre, zero at edges.</summary>
        private static byte[] GenerateRadialHill(int res, float minH, float maxH)
        {
            byte[] data = new byte[res * res * TerrainHeightFormat.BYTES_PER_SAMPLE];
            float centre = (res - 1) * 0.5f;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dx = (x - centre) / centre;
                    float dz = (z - centre) / centre;
                    float t  = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz));
                    float y  = minH + t * t * (maxH - minH);
                    ushort raw = TerrainHeightFormat.EncodeHeight(y, minH, maxH);
                    TerrainHeightFormat.WriteRaw(data, z * res + x, raw);
                }
            }
            return data;
        }

        /// <summary>Diagonal ridges: alternating bands at ~45 degrees. Visually distinct from tile A.</summary>
        private static byte[] GenerateDiagonalRidges(int res, float minH, float maxH)
        {
            byte[] data = new byte[res * res * TerrainHeightFormat.BYTES_PER_SAMPLE];
            float freq = 8f;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u  = (float)x / (res - 1);
                    float v  = (float)z / (res - 1);
                    float t  = 0.5f + 0.5f * Mathf.Sin((u + v) * freq * Mathf.PI);
                    float y  = minH + t * (maxH - minH);
                    ushort raw = TerrainHeightFormat.EncodeHeight(y, minH, maxH);
                    TerrainHeightFormat.WriteRaw(data, z * res + x, raw);
                }
            }
            return data;
        }

        /// <summary>Trivial splat: all weight on layer 0 (R channel = 255, GBA = 0).</summary>
        private static byte[] GenerateSplatAllLayer0(int splatRes)
        {
            byte[] data = new byte[splatRes * splatRes * 4];
            for (int i = 0; i < splatRes * splatRes; i++)
            {
                data[i * 4 + 0] = 255;
                data[i * 4 + 1] = 0;
                data[i * 4 + 2] = 0;
                data[i * 4 + 3] = 0;
            }
            return data;
        }

        // ── Layer set ─────────────────────────────────────────────────────────

        private static TerrainLayerSet CreateOrReplaceLayerSet(string path)
        {
            TerrainLayerSet? existing = AssetDatabase.LoadAssetAtPath<TerrainLayerSet>(path);
            TerrainLayerSet ls = existing != null
                ? existing
                : ScriptableObject.CreateInstance<TerrainLayerSet>();

            using var so = new SerializedObject(ls);
            so.Update();
            SerializedProperty albedosProp = so.FindProperty("layerAlbedos");
            albedosProp.arraySize = 1;
            albedosProp.GetArrayElementAtIndex(0).objectReferenceValue = null;
            SerializedProperty tilingProp = so.FindProperty("layerTiling");
            tilingProp.floatValue = TerrainShadingConfig.DEFAULT_LAYER_TILING;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (existing == null)
                AssetDatabase.CreateAsset(ls, path);
            else
                EditorUtility.SetDirty(ls);

            Debug.Log("[TerrainValidation] Saved layer set → " + path);
            return ls;
        }

        // ── Scene construction ────────────────────────────────────────────────

        private static void BuildScene(
            TerrainTileAsset tileA, TerrainTileAsset tileB,
            TerrainLayerSet layerSet,
            ComputeShader? cullCompute, Material? patchMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Directional light.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ONE renderer with both tiles.
            CreateTerrainRenderer("TerrainRenderer", new[] { tileA, tileB }, cullCompute, patchMat);

            // Camera: positioned at X=256 (seam), Y=80, Z=-60, looking at the seam.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView   = 60f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane  = 1500f;
            camGo.transform.position = new Vector3(256f, 80f, -60f);
            camGo.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            Debug.Log("[TerrainValidation] Scene saved: " + SCENE_PATH);
        }

        private static void CreateTerrainRenderer(
            string goName, TerrainTileAsset[] tiles,
            ComputeShader? cullCompute, Material? patchMat)
        {
            var go       = new GameObject(goName);
            var renderer = go.AddComponent<GpuTerrainRenderer>();

            using var so = new SerializedObject(renderer);
            so.Update();

            // Populate the tiles list.
            var tilesProp = so.FindProperty("tiles");
            tilesProp.arraySize = tiles.Length;
            for (int i = 0; i < tiles.Length; i++)
                tilesProp.GetArrayElementAtIndex(i).objectReferenceValue = tiles[i];

            // Write hidden infra fields (deterministic; auto-resolve handles runtime null).
            so.FindProperty("cullCompute").objectReferenceValue   = cullCompute;
            so.FindProperty("patchMaterial").objectReferenceValue = patchMat;

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
