#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// One-shot rebuild of the demo TerrainScatterConfig as 2 typed layers:
    ///   - Grass: DensityScatterLayer, kind=Grass, wind+interact=on, fresh 256×256 white density map
    ///   - Rock:  InstanceScatterLayer, kind=Mesh, wind+interact=off, empty AuthoredInstancesData sidecar
    ///
    /// Then rewires the SampleScene's ScatterField to the new config and saves the scene.
    ///
    /// Pre-flight: requires the LOD blade meshes and LOD rock meshes to exist on disk. Use
    /// Tools / GrassInteract / Build Demo Scene (legacy) FIRST to generate them once if missing
    /// — only the mesh+material assets are reused; this menu fully replaces the config + layers.
    /// </summary>
    internal static class SetupFreshDemoConfig
    {
        private const string DEMO_DIR             = "Assets/GrassInteract/Demo";
        private const string MESH_DIR             = "Assets/GrassInteract/Meshes";
        private const string CONFIG_PATH          = DEMO_DIR + "/GrassInteractDemoScatterConfig.asset";
        private const string SCENE_PATH           = DEMO_DIR + "/GrassInteractDemo.unity";
        private const string INDIRECT_MAT_PATH    = DEMO_DIR + "/GrassInteractIndirectMat.mat";
        private const string GRASS_INSTANCED_MAT  = DEMO_DIR + "/GrassInteractDemo.mat";
        private const string ROCK_MAT_PATH        = DEMO_DIR + "/ScatterPropRock.mat";
        private const string CULL_COMPUTE_PATH    = "Assets/GrassInteract/Shaders/GrassCull.compute";

        private const string GRASS_LAYER_NAME     = "GrassInteractDemoLayer";
        private const string ROCK_LAYER_NAME      = "Rock";
        private const int    DENSITY_RESOLUTION   = 256;
        private const float  FIELD_SIZE           = 40f;

        [MenuItem("Tools/GrassInteract/Setup/Fresh Demo Config (Density Grass + Instance Rock)")]
        private static void Run()
        {
            // 1. Pre-flight: meshes exist?
            Mesh? bladeLod0 = Load<Mesh>(MESH_DIR + "/GrassBlade_LOD0.mesh");
            Mesh? bladeLod1 = Load<Mesh>(MESH_DIR + "/GrassBlade_LOD1.mesh");
            Mesh? bladeLod2 = Load<Mesh>(MESH_DIR + "/GrassBlade_LOD2.mesh");
            Mesh? rockLod0  = Load<Mesh>(MESH_DIR + "/ScatterPropRock_LOD0.mesh");
            Mesh? rockLod1  = Load<Mesh>(MESH_DIR + "/ScatterPropRock_LOD1.mesh");
            Mesh? rockLod2  = Load<Mesh>(MESH_DIR + "/ScatterPropRock_LOD2.mesh");

            if (bladeLod0 == null || bladeLod1 == null || bladeLod2 == null)
            {
                EditorUtility.DisplayDialog("Setup Fresh Demo Config",
                    "Blade LOD meshes are missing. Run Tools / GrassInteract / Build Demo Scene first " +
                    "to generate them, then re-run this menu.", "OK");
                return;
            }
            if (rockLod0 == null || rockLod1 == null || rockLod2 == null)
            {
                EditorUtility.DisplayDialog("Setup Fresh Demo Config",
                    "Rock LOD meshes are missing. Run Tools / GrassInteract / Build Prop Rock + Demo Layer " +
                    "(or Build Prop Mesh) first to generate them, then re-run this menu.", "OK");
                return;
            }

            // 2. Pre-flight: materials exist?
            Material? grassInstancedMat = Load<Material>(GRASS_INSTANCED_MAT);
            Material? indirectMat       = Load<Material>(INDIRECT_MAT_PATH);
            Material? rockMat           = Load<Material>(ROCK_MAT_PATH);
            if (grassInstancedMat == null || indirectMat == null || rockMat == null)
            {
                EditorUtility.DisplayDialog("Setup Fresh Demo Config",
                    "Required materials missing. Run Tools / GrassInteract / Build Demo Scene first.", "OK");
                return;
            }

            // 3. Pre-flight: cull compute?
            ComputeShader? cullCompute = Load<ComputeShader>(CULL_COMPUTE_PATH);

            // 4. Delete the existing config (user explicitly opted out of backup).
            if (File.Exists(CONFIG_PATH))
            {
                AssetDatabase.DeleteAsset(CONFIG_PATH);
            }

            // 5. Create the new TerrainScatterConfig + wire GPU resources via SerializedObject
            //    (CullCompute / IndirectMaterial / Layers fields are read-only IReadOnlyList accessors).
            var config = ScriptableObject.CreateInstance<TerrainScatterConfig>();
            config.name = Path.GetFileNameWithoutExtension(CONFIG_PATH);
            AssetDatabase.CreateAsset(config, CONFIG_PATH);

            using (var soConfig = new SerializedObject(config))
            {
                soConfig.FindProperty("cullCompute").objectReferenceValue       = cullCompute;
                soConfig.FindProperty("indirectMaterial").objectReferenceValue = indirectMat;
                soConfig.ApplyModifiedPropertiesWithoutUndo();
            }

            // 6. Create the grass DensityScatterLayer + its density map.
            var grassLayer = ScriptableObject.CreateInstance<DensityScatterLayer>();
            grassLayer.name = GRASS_LAYER_NAME;
            AssetDatabase.AddObjectToAsset(grassLayer, CONFIG_PATH);
            Texture2D grassDensityMap = CreateWhiteDensityMap(GRASS_LAYER_NAME + "_DensityMap");
            AssetDatabase.AddObjectToAsset(grassDensityMap, CONFIG_PATH);
            WireGrassLayer(grassLayer, grassInstancedMat, bladeLod0, bladeLod1, bladeLod2, grassDensityMap);

            // 7. Create the Rock InstanceScatterLayer + its empty AuthoredInstancesData sidecar.
            var rockLayer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            rockLayer.name = ROCK_LAYER_NAME;
            AssetDatabase.AddObjectToAsset(rockLayer, CONFIG_PATH);
            var rockSidecar = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            rockSidecar.name = ROCK_LAYER_NAME + "_AuthoredInstances";
            AssetDatabase.AddObjectToAsset(rockSidecar, CONFIG_PATH);
            WireRockLayer(rockLayer, rockMat, rockLod0, rockLod1, rockLod2, rockSidecar);

            // 8. Add both layers to config.Layers (the field is backed by a serialized list).
            using (var soConfig = new SerializedObject(config))
            {
                SerializedProperty layersProp = soConfig.FindProperty("layers");
                layersProp.arraySize = 2;
                layersProp.GetArrayElementAtIndex(0).objectReferenceValue = grassLayer;
                layersProp.GetArrayElementAtIndex(1).objectReferenceValue = rockLayer;
                soConfig.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(grassLayer);
            EditorUtility.SetDirty(rockLayer);
            EditorUtility.SetDirty(grassDensityMap);
            EditorUtility.SetDirty(rockSidecar);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(CONFIG_PATH);

            // 9. Rewire SampleScene's ScatterField to the new config.
            int sceneFieldsWired = RewireScene(config);

            Debug.Log($"[SetupFreshDemoConfig] Created {CONFIG_PATH} with: " +
                      $"DensityScatterLayer '{GRASS_LAYER_NAME}' (wind+interact ON) + " +
                      $"InstanceScatterLayer '{ROCK_LAYER_NAME}' (wind+interact OFF, 0 instances). " +
                      $"Rewired {sceneFieldsWired} ScatterField(s) in active scene.");
        }

        private static void WireGrassLayer(DensityScatterLayer layer, Material grassMaterial,
            Mesh lod0, Mesh lod1, Mesh lod2, Texture2D densityMap)
        {
            using var so = new SerializedObject(layer);

            // Kind field removed in Phase A (route via InteractsWithDeform).
            // Deform bools (Grass equivalent: both ON).
            so.FindProperty("affectedByWind").boolValue             = true;
            so.FindProperty("affectedByInteractors").boolValue      = true;

            // Density + placement.
            so.FindProperty("densityMap").objectReferenceValue      = densityMap;
            so.FindProperty("targetInstances").intValue             = 50000;
            so.FindProperty("fieldBounds").vector2Value             = new Vector2(FIELD_SIZE, FIELD_SIZE);
            so.FindProperty("scaleRange").vector2Value              = new Vector2(0.8f, 1.2f);
            so.FindProperty("seed").intValue                        = 0;
            so.FindProperty("slopeRange").vector2Value              = new Vector2(0f, 90f);

            // Render — grassMaterial collapsed to 'material' in Phase A.
            so.FindProperty("material").objectReferenceValue        = grassMaterial;

            // LOD meshes.
            SerializedProperty lods = so.FindProperty("lods");
            lods.arraySize = 3;
            WriteLod(lods.GetArrayElementAtIndex(0), lod0, 30f);
            WriteLod(lods.GetArrayElementAtIndex(1), lod1, 80f);
            WriteLod(lods.GetArrayElementAtIndex(2), lod2, 400f);

            // Bounds headroom for wind.
            so.FindProperty("maxBladeHeight").floatValue            = 1f;
            so.FindProperty("bendHeadroom").floatValue              = 1f;
            so.FindProperty("chunkSize").intValue                   = 16;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireRockLayer(InstanceScatterLayer layer, Material meshMaterial,
            Mesh lod0, Mesh lod1, Mesh lod2, AuthoredInstancesData sidecar)
        {
            using var so = new SerializedObject(layer);

            // Kind field removed in Phase A (route via InteractsWithDeform).
            // Deform bools (Mesh equivalent: both OFF).
            so.FindProperty("affectedByWind").boolValue             = false;
            so.FindProperty("affectedByInteractors").boolValue      = false;

            // Placement defaults.
            so.FindProperty("fieldBounds").vector2Value             = new Vector2(FIELD_SIZE, FIELD_SIZE);
            so.FindProperty("scaleRange").vector2Value              = new Vector2(0.8f, 1.2f);
            so.FindProperty("seed").intValue                        = 11;
            so.FindProperty("slopeRange").vector2Value              = new Vector2(0f, 90f);

            // Render — meshMaterial collapsed to 'material' in Phase A.
            so.FindProperty("material").objectReferenceValue        = meshMaterial;

            // LOD meshes.
            SerializedProperty lods = so.FindProperty("lods");
            lods.arraySize = 3;
            WriteLod(lods.GetArrayElementAtIndex(0), lod0, 30f);
            WriteLod(lods.GetArrayElementAtIndex(1), lod1, 80f);
            WriteLod(lods.GetArrayElementAtIndex(2), lod2, 400f);

            // Sidecar (empty).
            so.FindProperty("authoredInstances").objectReferenceValue = sidecar;
            so.FindProperty("placeSpacing").floatValue                = 0.5f;

            // Bounds headroom (small for static props).
            so.FindProperty("maxBladeHeight").floatValue            = 1f;
            so.FindProperty("bendHeadroom").floatValue              = 0f;
            so.FindProperty("chunkSize").intValue                   = 16;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WriteLod(SerializedProperty elem, Mesh mesh, float maxDistance)
        {
            elem.FindPropertyRelative("mesh").objectReferenceValue = mesh;
            elem.FindPropertyRelative("maxDistance").floatValue    = maxDistance;
        }

        private static Texture2D CreateWhiteDensityMap(string name)
        {
            var tex = new Texture2D(DENSITY_RESOLUTION, DENSITY_RESOLUTION, TextureFormat.R8, false, true)
            {
                name      = name,
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
            Color32[] pixels = new Color32[DENSITY_RESOLUTION * DENSITY_RESOLUTION];
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        private static int RewireScene(TerrainScatterConfig newConfig)
        {
            Scene active = EditorSceneManager.GetActiveScene();
            if (!active.IsValid())
            {
                // Try to open the demo scene if no scene loaded.
                if (File.Exists(SCENE_PATH))
                {
                    active = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Single);
                }
                else
                {
                    Debug.LogWarning("[SetupFreshDemoConfig] No active scene; skipping scene rewire.");
                    return 0;
                }
            }

            ScatterField[] fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            int wired = 0;
            foreach (ScatterField field in fields)
            {
                using var so = new SerializedObject(field);
                so.FindProperty("config").objectReferenceValue = newConfig;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(field);
                wired++;
            }

            if (wired > 0)
            {
                EditorSceneManager.MarkSceneDirty(active);
                EditorSceneManager.SaveScene(active);
            }
            return wired;
        }

        private static T? Load<T>(string path) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }
    }
}
