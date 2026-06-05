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
    /// One-shot, re-runnable builder for a self-contained GrassInteract demo scene: blade
    /// meshes → instanced material → render-only <see cref="GrassLODConfig"/> → a density
    /// <see cref="Texture2D"/> → a <see cref="GrassLayer"/> → a scene with ground, camera, light, a
    /// <see cref="GrassTrampleMap"/>, a circling <see cref="GrassInteractor"/>, and a wired
    /// <see cref="GrassInteractField"/>. Run via <c>Tools ▸ GrassInteract ▸ Build Demo Scene</c>.
    /// </summary>
    public static class GrassInteractDemoBuilder
    {
        private const string DEMO_DIR = "Assets/GrassInteract/Demo";
        private const string MESH_DIR = "Assets/GrassInteract/Meshes";
        private const string MATERIAL_PATH = DEMO_DIR + "/GrassInteractDemo.mat";
        private const string GROUND_MATERIAL_PATH = DEMO_DIR + "/GrassInteractGround.mat";
        private const string CONFIG_PATH = DEMO_DIR + "/GrassInteractDemoConfig.asset";
        private const string DENSITY_PATH = DEMO_DIR + "/GrassInteractDemoDensity.asset";
        private const string LAYER_PATH = DEMO_DIR + "/GrassInteractDemoLayer.asset";
        private const string SCENE_PATH = DEMO_DIR + "/GrassInteractDemo.unity";
        private const string SHADER_NAME = "GrassInteract/InstancedGrass";

        private const float FIELD_SIZE = 40f;
        private const int DENSITY_RESOLUTION = 256;

        [MenuItem("Tools/GrassInteract/Build Demo Scene")]
        public static void BuildDemo()
        {
            // Non-destructive guard: never silently discard the user's unsaved scene.
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

            // 2. Instanced material, render config, density texture, grass layer.
            CreateOrUpdateMaterial();
            CreateOrUpdateConfig(lod0, lod1, lod2);
            CreateOrUpdateDensity();
            AssetDatabase.SaveAssets();
            // Force synchronous imports so asset GUIDs are committed BEFORE the layer + scene reference them
            // (a same-run reference to a freshly-created asset otherwise fails to serialize).
            AssetDatabase.ImportAsset(MATERIAL_PATH, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(CONFIG_PATH, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(DENSITY_PATH, ImportAssetOptions.ForceSynchronousImport);

            CreateOrUpdateLayer();
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(LAYER_PATH, ImportAssetOptions.ForceSynchronousImport);
            GrassLayer layer = AssetDatabase.LoadAssetAtPath<GrassLayer>(LAYER_PATH);

            // 3. Fresh scene with default camera + directional light.
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

            // Ground plane (Unity plane is 10×10 at scale 1) — has a MeshCollider for the placement raycast.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            float groundScale = FIELD_SIZE / 10f + 0.4f;
            ground.transform.localScale = new Vector3(groundScale, 1f, groundScale);
            ground.GetComponent<MeshRenderer>().sharedMaterial = CreateOrUpdateGroundMaterial();

            // Trample map driver covering the field.
            var trampleGo = new GameObject("GrassTrampleMap");
            trampleGo.transform.position = Vector3.zero;
            GrassTrampleMap trample = trampleGo.AddComponent<GrassTrampleMap>();
            ConfigureTrampleMap(trample);

            // Circling interactor that sweeps the grass (writes the trample map).
            GameObject effectorGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            effectorGo.name = "Effector";
            effectorGo.transform.position = new Vector3(0f, 0.3f, 0f);
            effectorGo.transform.localScale = Vector3.one * 2f;
            // CreatePrimitive gives a built-in Standard material (magenta under URP) — replace it.
            Shader? effShader = Shader.Find("Universal Render Pipeline/Lit");
            if (effShader != null)
            {
                var effMat = new Material(effShader) { name = "GrassInteractEffector" };
                effMat.SetColor("_BaseColor", new Color(0.95f, 0.55f, 0.15f, 1f)); // orange marker
                effectorGo.GetComponent<MeshRenderer>().sharedMaterial = effMat;
            }
            // The collider on the sphere would catch the placement raycast — remove it so it doesn't
            // pock the grass with a no-grass dot under the effector's start position.
            Object.DestroyImmediate(effectorGo.GetComponent<Collider>());
            GrassInteractor interactor = effectorGo.AddComponent<GrassInteractor>();
            ConfigureInteractor(interactor);
            effectorGo.AddComponent<GrassInteractDemoEffector>();

            // The grass orchestrator, wired to the layer.
            var grassGo = new GameObject("GrassInteractField");
            grassGo.transform.position = Vector3.zero;
            GrassInteractField grass = grassGo.AddComponent<GrassInteractField>();
            AssignGrassLayer(grass, layer);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Second synchronous pass: a freshly-created GrassLayer reference can serialize as null in the
            // SAME-run scene save (known asset-commit quirk — also seen with the config asset). Now that
            // SaveAssets + Refresh have committed the GUID, re-assign + re-save so the reference sticks.
            // Done synchronously (NOT via EditorApplication.delayCall, which never fires when the editor is
            // driven headless without focus).
            GrassLayer committed = AssetDatabase.LoadAssetAtPath<GrassLayer>(LAYER_PATH);
            AssignGrassLayer(grass, committed);
            EditorUtility.SetDirty(grass);
            grass.Rebuild();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GrassInteractDemoBuilder] Demo scene built → {SCENE_PATH}. " +
                      "Press Play to see the effector leave a recovering trample trail through the swaying grass.");
        }

        private static Material CreateOrUpdateMaterial()
        {
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
                throw new System.Exception($"Shader '{SHADER_NAME}' not found — ensure it compiled.");

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

            mat.SetColor("_BaseColor", new Color(0.12f, 0.15f, 0.10f, 1f)); // dark earthy soil
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GrassLODConfig CreateOrUpdateConfig(Mesh lod0, Mesh lod1, Mesh lod2)
        {
            GrassLODConfig? config = AssetDatabase.LoadAssetAtPath<GrassLODConfig>(CONFIG_PATH);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GrassLODConfig>();
                AssetDatabase.CreateAsset(config, CONFIG_PATH);
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            var so = new SerializedObject(config);
            so.FindProperty("grassMaterial").objectReferenceValue = material;

            SerializedProperty meshes = so.FindProperty("lodMeshes");
            meshes.arraySize = 3;
            meshes.GetArrayElementAtIndex(0).objectReferenceValue = lod0;
            meshes.GetArrayElementAtIndex(1).objectReferenceValue = lod1;
            meshes.GetArrayElementAtIndex(2).objectReferenceValue = lod2;

            SerializedProperty dists = so.FindProperty("lodMaxDistances");
            dists.arraySize = 2;
            dists.GetArrayElementAtIndex(0).floatValue = 18f;
            dists.GetArrayElementAtIndex(1).floatValue = 38f;

            so.FindProperty("maxBladeHeight").floatValue = 1f;
            so.FindProperty("bendHeadroom").floatValue = 1f;

            // Ambient wind (visible idle sway).
            so.FindProperty("windDirection").vector2Value = new Vector2(1f, 0.3f);
            so.FindProperty("windStrength").floatValue = 0.15f;
            so.FindProperty("windFrequency").floatValue = 1.2f;
            so.FindProperty("windNoiseScale").floatValue = 0.25f;
            // shadowCastingMode left at its serialized default (Off).

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        /// <summary>Creates a native, readable, uncompressed R8 density texture (full field by default).</summary>
        private static Texture2D CreateOrUpdateDensity()
        {
            Texture2D? tex = AssetDatabase.LoadAssetAtPath<Texture2D>(DENSITY_PATH);
            bool isNew = tex == null;
            if (isNew)
            {
                tex = new Texture2D(DENSITY_RESOLUTION, DENSITY_RESOLUTION, TextureFormat.R8, mipChain: false,
                    linear: true)
                {
                    name = "GrassInteractDemoDensity",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };

                // Default: full density across the field (paint with the Grass Painter to carve it).
                var pixels = new Color[DENSITY_RESOLUTION * DENSITY_RESOLUTION];
                for (int i = 0; i < pixels.Length; ++i)
                    pixels[i] = Color.white;
                tex.SetPixels(pixels);
                tex.Apply(false);

                AssetDatabase.CreateAsset(tex, DENSITY_PATH);
            }

            EditorUtility.SetDirty(tex!);
            return tex!;
        }

        private static GrassLayer CreateOrUpdateLayer()
        {
            GrassLayer? layer = AssetDatabase.LoadAssetAtPath<GrassLayer>(LAYER_PATH);
            if (layer == null)
            {
                layer = ScriptableObject.CreateInstance<GrassLayer>();
                AssetDatabase.CreateAsset(layer, LAYER_PATH);
            }

            Texture2D density = AssetDatabase.LoadAssetAtPath<Texture2D>(DENSITY_PATH);
            GrassLODConfig config = AssetDatabase.LoadAssetAtPath<GrassLODConfig>(CONFIG_PATH);

            var so = new SerializedObject(layer);
            so.FindProperty("densityMap").objectReferenceValue = density;
            so.FindProperty("targetInstances").intValue = 20000;
            so.FindProperty("fieldBounds").vector2Value = new Vector2(FIELD_SIZE, FIELD_SIZE);
            so.FindProperty("chunkSize").floatValue = 8f;
            so.FindProperty("scaleRange").vector2Value = new Vector2(0.7f, 1.3f);
            so.FindProperty("seed").intValue = 12345;
            so.FindProperty("groundSnapMask").intValue = ~0; // everything
            so.FindProperty("renderConfig").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static void ConfigureInteractor(GrassInteractor interactor)
        {
            var so = new SerializedObject(interactor);
            // A 2.5 m footprint (vs the component's smaller default) so the orbiting effector carves a
            // clearly visible ~5 m-wide trail in the demo. Pairs with the slower trample recovery below.
            so.FindProperty("worldRadius").floatValue = 2.5f;
            so.FindProperty("strength").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureTrampleMap(GrassTrampleMap trample)
        {
            // Slower recovery than the component default (1.5/s) so the demo's moving effector leaves a
            // trail that lingers long enough to read before standing back up. Pairs with the wider
            // interactor footprint above. Tunable per-field on the GrassTrampleMap component.
            var so = new SerializedObject(trample);
            so.FindProperty("recoveryPerSecond").floatValue = 1.0f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignGrassLayer(GrassInteractField grass, GrassLayer layer)
        {
            var so = new SerializedObject(grass);
            so.FindProperty("grassLayer").objectReferenceValue = layer;
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
