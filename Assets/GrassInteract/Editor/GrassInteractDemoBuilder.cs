#nullable enable
using System.IO;
using GrassInteract.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// One-shot, re-runnable builder for a self-contained GrassInteract demo scene.
    ///
    /// Builds: blade meshes → instanced material → <see cref="TerrainScatterConfig"/> (owns
    /// <see cref="ScatterLayer"/> + density as sub-assets) → scene with ground, camera, light, a
    /// circling <see cref="GrassInteractor"/>, and a <see cref="ScatterField"/> wired to the config.
    /// Run via <c>Tools ▸ GrassInteract ▸ Build Demo Scene</c>.
    ///
    /// SSOT: all render/wind/bend tunables live on the sub-asset ScatterLayer directly.
    ///
    /// Idempotent: re-running re-creates / updates the assets in place, then re-saves the scene.
    /// </summary>
    public static class GrassInteractDemoBuilder
    {
        private const string DEMO_DIR              = "Assets/GrassInteract/Demo";
        private const string MESH_DIR              = "Assets/GrassInteract/Meshes";
        private const string SHADERS_DIR           = "Assets/GrassInteract/Shaders";
        private const string MATERIAL_PATH         = DEMO_DIR + "/GrassInteractDemo.mat";
        private const string INDIRECT_MAT_PATH     = DEMO_DIR + "/GrassInteractIndirectMat.mat";
        private const string GROUND_MATERIAL_PATH  = DEMO_DIR + "/GrassInteractGround.mat";
        private const string SCATTER_CONFIG_PATH   = DEMO_DIR + "/GrassInteractDemoScatterConfig.asset";
        private const string SCENE_PATH            = DEMO_DIR + "/GrassInteractDemo.unity";
        private const string CULL_COMPUTE_PATH     = SHADERS_DIR + "/GrassCull.compute";
        private const string INSTANCED_SHADER_NAME = "GrassInteract/InstancedGrass";
        private const string INDIRECT_SHADER_NAME  = "GrassInteract/IndirectGrass";
        private const string LAYER_NAME            = "GrassInteractDemoLayer";

        private const float FIELD_SIZE = 40f;
        private const int   DENSITY_RESOLUTION = 256;

        [MenuItem("Tools/GrassInteract/Build Demo Scene")]
        public static void BuildDemo()
        {
            Scene current = EditorSceneManager.GetActiveScene();
            if (current.isDirty)
            {
                Debug.LogError("[GrassInteractDemoBuilder] The active scene has unsaved changes. Save or " +
                               "discard it, then re-run Tools ▸ GrassInteract ▸ Build Demo Scene.");
                return;
            }

            if (!Directory.Exists(DEMO_DIR))
                Directory.CreateDirectory(DEMO_DIR);

            // 1. Blade meshes (LOD0/1/2).
            GrassBladeMeshBuilder.BuildAll();
            Mesh lod0 = LoadMeshOrThrow("GrassBlade_LOD0");
            Mesh lod1 = LoadMeshOrThrow("GrassBlade_LOD1");
            Mesh lod2 = LoadMeshOrThrow("GrassBlade_LOD2");

            // 2. Materials.
            Material instancedMat = CreateOrUpdateMaterial();
            Material indirectMat = CreateOrUpdateIndirectMaterial();

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MATERIAL_PATH, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(INDIRECT_MAT_PATH, ImportAssetOptions.ForceSynchronousImport);

            // 3. TerrainScatterConfig (parent asset) + ScatterLayer + density as sub-assets.
            TerrainScatterConfig scatterConfig = CreateOrUpdateScatterConfig(indirectMat);
            DensityScatterLayer layer = EnsureDemoLayer(scatterConfig);
            ConfigureLayer(layer, instancedMat, lod0, lod1, lod2);
            EnsureFullDensity(layer);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(SCATTER_CONFIG_PATH, ImportAssetOptions.ForceSynchronousImport);

            // 4. Fresh scene with default camera + directional light.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera? cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, 16f, -26f);
                cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
                cam.farClipPlane = 300f;
            }

            Light? light = Object.FindFirstObjectByType<Light>();
            if (light != null)
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            float groundScale = FIELD_SIZE / 10f + 0.4f;
            ground.transform.localScale = new Vector3(groundScale, 1f, groundScale);
            ground.GetComponent<MeshRenderer>().sharedMaterial = CreateOrUpdateGroundMaterial();

            GameObject effectorGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            effectorGo.name = "Effector";
            effectorGo.transform.position = new Vector3(0f, 0.3f, 0f);
            effectorGo.transform.localScale = Vector3.one * 2f;
            Shader? effShader = Shader.Find("Universal Render Pipeline/Lit");
            if (effShader != null)
            {
                var effMat = new Material(effShader) { name = "GrassInteractEffector" };
                effMat.SetColor("_BaseColor", new Color(0.95f, 0.55f, 0.15f, 1f));
                effectorGo.GetComponent<MeshRenderer>().sharedMaterial = effMat;
            }
            Object.DestroyImmediate(effectorGo.GetComponent<Collider>());
            GrassInteractor interactor = effectorGo.AddComponent<GrassInteractor>();
            ConfigureInteractor(interactor);
            effectorGo.AddComponent<GrassInteractDemoEffector>();

            // 5. ScatterField wired to the TerrainScatterConfig.
            var grassGo = new GameObject("ScatterField");
            grassGo.transform.position = Vector3.zero;
            ScatterField grass = grassGo.AddComponent<ScatterField>();
            AssignConfig(grass, scatterConfig);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Same-run reference re-bind: a freshly created TerrainScatterConfig can sometimes serialize
            // as null on first save. Re-assign + re-save synchronously to make the link stick.
            TerrainScatterConfig committed = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(SCATTER_CONFIG_PATH);
            AssignConfig(grass, committed);
            EditorUtility.SetDirty(grass);
            grass.Rebuild();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GrassInteractDemoBuilder] Demo scene built → {SCENE_PATH}. " +
                      $"Layer + density live as sub-assets under {SCATTER_CONFIG_PATH}. " +
                      "Press Play to see the effector lean the swaying grass aside as it circles.");
        }

        // ── Materials + render config ─────────────────────────────────────────

        private static Material CreateOrUpdateMaterial()
        {
            Shader shader = Shader.Find(INSTANCED_SHADER_NAME);
            if (shader == null)
                throw new System.Exception($"Shader '{INSTANCED_SHADER_NAME}' not found — ensure it compiled.");

            Material? mat = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            if (mat == null)
            {
                mat = new Material(shader) { name = "GrassInteractDemo" };
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, MATERIAL_PATH);
            }
            else
            {
                mat.shader = shader;
                mat.enableInstancing = true;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateIndirectMaterial()
        {
            Shader? shader = Shader.Find(INDIRECT_SHADER_NAME);
            Material? mat = AssetDatabase.LoadAssetAtPath<Material>(INDIRECT_MAT_PATH);

            if (shader == null)
            {
                // Indirect tier is optional; the GPU engine self-tests and the field falls back to CPU.
                if (mat == null)
                    return CreateOrUpdateMaterial(); // reuse instanced material as a placeholder ref
                return mat;
            }

            if (mat == null)
            {
                mat = new Material(shader) { name = "GrassInteractIndirectMat" };
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, INDIRECT_MAT_PATH);
            }
            else
            {
                mat.shader = shader;
                mat.enableInstancing = true;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateGroundMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new System.Exception("URP/Lit shader not found — is the project using URP?");

            Material? mat = AssetDatabase.LoadAssetAtPath<Material>(GROUND_MATERIAL_PATH);
            if (mat == null)
            {
                mat = new Material(shader) { name = "GrassInteractGround" };
                AssetDatabase.CreateAsset(mat, GROUND_MATERIAL_PATH);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", new Color(0.12f, 0.15f, 0.10f, 1f));
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ── TerrainScatterConfig + sub-asset layer ────────────────────────────

        private static TerrainScatterConfig CreateOrUpdateScatterConfig(Material indirectMaterial)
        {
            TerrainScatterConfig? config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(SCATTER_CONFIG_PATH);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<TerrainScatterConfig>();
                AssetDatabase.CreateAsset(config, SCATTER_CONFIG_PATH);
            }

            ComputeShader? cullCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(CULL_COMPUTE_PATH);

            var so = new SerializedObject(config);
            so.FindProperty("cullCompute").objectReferenceValue     = cullCompute;
            so.FindProperty("indirectMaterial").objectReferenceValue = indirectMaterial;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        private static DensityScatterLayer EnsureDemoLayer(TerrainScatterConfig config)
        {
            // Prefer the first existing DensityScatterLayer sub-asset; ignore stale loose refs.
            foreach (var existing in config.Layers)
            {
                if (existing is DensityScatterLayer d && AssetDatabase.IsSubAsset(d))
                    return d;
            }

            // Drop any stale non-sub-asset refs from the config's layers list before creating a fresh one.
            using (var cso = new SerializedObject(config))
            {
                SerializedProperty layersProp = cso.FindProperty("layers");
                if (layersProp != null && layersProp.isArray)
                {
                    layersProp.arraySize = 0;
                    cso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                }
            }

            return config.CreateDensityLayer(LAYER_NAME);
        }

        private static void ConfigureLayer(DensityScatterLayer layer, Material grassMaterial,
            Mesh lod0, Mesh lod1, Mesh lod2)
        {
            var so = new SerializedObject(layer);

            // Deform bools (Grass: both ON).
            so.FindProperty("affectedByWind").boolValue        = true;
            so.FindProperty("affectedByInteractors").boolValue = true;

            // Density placement fields now on DensityScatterLayer (Phase A move).
            so.FindProperty("targetInstances").intValue  = 20000;
            so.FindProperty("fieldBounds").vector2Value  = new Vector2(FIELD_SIZE, FIELD_SIZE);
            so.FindProperty("scaleRange").vector2Value   = new Vector2(1f, 2.5f);
            so.FindProperty("seed").intValue             = 12345;
            so.FindProperty("slopeRange").vector2Value   = new Vector2(0f, 90f);
            so.FindProperty("splatLayerIndex").intValue  = -1;
            so.FindProperty("splatThreshold").floatValue = 1f;
            so.FindProperty("alignToNormal").boolValue   = true;

            // groundSnapMask lives on base class.
            so.FindProperty("groundSnapMask").FindPropertyRelative("m_Bits").intValue = ~0;

            // Rendering — single 'material' field (Phase A collapse).
            so.FindProperty("material").objectReferenceValue = grassMaterial;
            so.FindProperty("shadowCastingMode").enumValueIndex = (int)UnityEngine.Rendering.ShadowCastingMode.Off;

            // Wind.
            so.FindProperty("windDirection").vector2Value = new Vector2(1f, 0.3f);
            so.FindProperty("windStrength").floatValue    = 0.4f;
            so.FindProperty("windFrequency").floatValue   = 2.44f;
            so.FindProperty("windNoiseScale").floatValue  = 0.28f;

            // Trample.
            so.FindProperty("bendStrength").floatValue  = 0.7f;
            so.FindProperty("flatten").floatValue       = 0.5f;
            so.FindProperty("recoveryRate").floatValue  = 4f;

            // Bounds.
            so.FindProperty("maxBladeHeight").floatValue = 1f;
            so.FindProperty("bendHeadroom").floatValue   = 1f;

            // GPU-driven grid.
            so.FindProperty("chunkSize").intValue = 16;

            // LODs.
            SerializedProperty lodsProp = so.FindProperty("lods");
            lodsProp.arraySize = 3;
            SerializedProperty l0 = lodsProp.GetArrayElementAtIndex(0);
            l0.FindPropertyRelative("mesh").objectReferenceValue = lod0;
            l0.FindPropertyRelative("maxDistance").floatValue = 18f;
            SerializedProperty l1 = lodsProp.GetArrayElementAtIndex(1);
            l1.FindPropertyRelative("mesh").objectReferenceValue = lod1;
            l1.FindPropertyRelative("maxDistance").floatValue = 38f;
            SerializedProperty l2 = lodsProp.GetArrayElementAtIndex(2);
            l2.FindPropertyRelative("mesh").objectReferenceValue = lod2;
            l2.FindPropertyRelative("maxDistance").floatValue = 1000f;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(layer);
        }

        /// <summary>
        /// Resets the sub-asset density texture to full-white (full density everywhere) so
        /// re-running the builder gives a deterministic baseline. CreateLayer initialises with
        /// black (zero density); we want grass visible immediately after the build.
        /// </summary>
        private static void EnsureFullDensity(ScatterLayer layer)
        {
            Texture2D? tex = layer.DensityMap;
            if (tex == null) return;
            if (!tex.isReadable) return;

            var pixels = new Color[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            EditorUtility.SetDirty(tex);
        }

        // ── Interactor + ScatterField wiring ──────────────────────────────────

        private static void ConfigureInteractor(GrassInteractor interactor)
        {
            var so = new SerializedObject(interactor);
            so.FindProperty("worldRadius").floatValue = 2.5f;
            so.FindProperty("strength").floatValue    = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignConfig(ScatterField grass, TerrainScatterConfig config)
        {
            var so = new SerializedObject(grass);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Mesh LoadMeshOrThrow(string assetName)
        {
            string path = $"{MESH_DIR}/{assetName}.asset";
            Mesh? mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
                throw new System.Exception($"Mesh not found at {path} — run Build Blade Meshes first.");
            return mesh;
        }
    }
}
