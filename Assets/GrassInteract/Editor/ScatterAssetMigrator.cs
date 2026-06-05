#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GrassInteract.EditorTools
{
    internal static class ScatterAssetMigrator
    {
        internal static bool IsLegacy(ScatterField field)
        {
            if (field == null) return false;
            if (field.Config != null) return false;

            using var so = new SerializedObject(field);
            var layersProp = so.FindProperty("layers");
            return layersProp != null && layersProp.isArray && layersProp.arraySize > 0;
        }

        internal static bool Migrate(ScatterField field)
        {
            if (field == null) return false;

            if (!EditorUtility.DisplayDialog(
                "Migrate Legacy ScatterField",
                $"Convert legacy assets on '{field.name}' into a new TerrainScatterConfig?\n\n" +
                "• A new .asset file will be created next to the scene.\n" +
                "• All inline layers will be copied as sub-assets.\n" +
                "• Density textures will be copied (NOT moved) into the config.\n" +
                "• Old loose assets are left untouched on disk for safety.\n" +
                "• Rollback: revert scene + delete the new config asset.",
                "Migrate", "Cancel"))
            {
                return false;
            }

            string scenePath = field.gameObject.scene.path;
            string configPath = System.IO.Path.GetDirectoryName(scenePath) + "/"
                + field.gameObject.scene.name + "_ScatterConfig.asset";

            var config = ScriptableObject.CreateInstance<TerrainScatterConfig>();
            AssetDatabase.CreateAsset(config, configPath);

            using var fso = new SerializedObject(field);

            CopySerializedRef(fso, "cullCompute", config, "cullCompute");
            CopySerializedRef(fso, "indirectMaterial", config, "indirectMaterial");

            var legacyLayers = fso.FindProperty("layers");
            int n = legacyLayers != null ? legacyLayers.arraySize : 0;
            var copiedLayers = new List<ScatterLayer>(n);

            for (int i = 0; i < n; ++i)
            {
                var legacy = legacyLayers!.GetArrayElementAtIndex(i).objectReferenceValue as ScatterLayer;
                if (legacy == null)
                {
                    copiedLayers.Add(null!);
                    continue;
                }

                var copy = Object.Instantiate(legacy);
                copy.name = legacy.name;
                copy.hideFlags = HideFlags.None;
                AssetDatabase.AddObjectToAsset(copy, config);
                copy.hideFlags = HideFlags.HideInHierarchy;

                Texture2D? legacyTex = legacy.DensityMap;
                if (legacyTex != null && legacyTex.isReadable)
                {
                    var texCopy = ClonePixelsR8(legacyTex, $"{copy.name}_DensityMap");
                    texCopy.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(texCopy, config);

                    using var lso = new SerializedObject(copy);
                    lso.FindProperty("densityMap").objectReferenceValue = texCopy;
                    lso.ApplyModifiedPropertiesWithoutUndo();
                }
                else if (legacyTex != null)
                {
                    Debug.LogWarning(
                        $"[Migrate] Layer '{legacy.name}' density texture not readable — " +
                        "linking original texture instead of cloning. Enable Read/Write to copy as sub-asset.", field);
                }

                copiedLayers.Add(copy);
            }

            using (var cso = new SerializedObject(config))
            {
                var layersList = cso.FindProperty("layers");
                layersList.arraySize = copiedLayers.Count;
                for (int i = 0; i < copiedLayers.Count; ++i)
                    layersList.GetArrayElementAtIndex(i).objectReferenceValue = copiedLayers[i];
                cso.ApplyModifiedPropertiesWithoutUndo();
            }

            fso.FindProperty("config").objectReferenceValue = config;
            fso.FindProperty("layers").arraySize = 0;
            var cullProp = fso.FindProperty("cullCompute");
            if (cullProp != null) cullProp.objectReferenceValue = null;
            var matProp = fso.FindProperty("indirectMaterial");
            if (matProp != null) matProp.objectReferenceValue = null;
            fso.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(field);
            EditorUtility.SetDirty(config);
            EditorSceneManager.MarkSceneDirty(field.gameObject.scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Migrate] Done. Created '{configPath}' with {copiedLayers.Count} layers. " +
                      "Old loose assets remain on disk — delete them after verifying the demo runs.");

            field.Rebuild();
            return true;
        }

        [MenuItem("Tools/GrassInteract/Migrate Legacy ScatterField")]
        private static void MigrateMenu()
        {
            var field = Selection.activeGameObject?.GetComponent<ScatterField>();
            if (field == null)
            {
                EditorUtility.DisplayDialog("Migrate",
                    "Select a GameObject with a ScatterField first.", "OK");
                return;
            }
            if (!IsLegacy(field))
            {
                EditorUtility.DisplayDialog("Migrate",
                    "This ScatterField is not in legacy state (config already assigned or no inline data).", "OK");
                return;
            }
            Migrate(field);
        }

        private static void CopySerializedRef(SerializedObject sourceSo, string sourceProp,
            ScriptableObject targetObj, string targetProp)
        {
            var sp = sourceSo.FindProperty(sourceProp);
            if (sp == null) return;

            using var tso = new SerializedObject(targetObj);
            var tp = tso.FindProperty(targetProp);
            if (tp != null)
            {
                tp.objectReferenceValue = sp.objectReferenceValue;
                tso.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Texture2D ClonePixelsR8(Texture2D source, string name)
        {
            int w = source.width;
            int h = source.height;

            var copy = new Texture2D(
                w, h,
                GraphicsFormat.R8_UNorm,
                TextureCreationFlags.None)
            {
                name       = name,
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.HideInHierarchy,
            };

            Color[] src = source.GetPixels();
            for (int i = 0; i < src.Length; ++i)
            {
                float g = src[i].grayscale;
                src[i] = new Color(g, g, g, 1f);
            }
            copy.SetPixels(src);
            copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return copy;
        }
    }
}
