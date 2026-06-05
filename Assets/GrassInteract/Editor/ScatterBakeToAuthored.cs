#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// One-shot freeze of a procedural <see cref="DensityScatterLayer"/> into a new
    /// <see cref="InstanceScatterLayer"/> sub-asset on the same config.
    ///
    /// Selects a <see cref="DensityScatterLayer"/> asset in the Project window, then choose
    /// Tools / GrassInteract / Bake Procedural Layer to Authored. The menu:
    ///   1. Finds an enabled <see cref="ScatterField"/> in the active scene whose config references
    ///      the selected layer (used purely to pick the origin + terrain/raycast sampler).
    ///   2. Calls <see cref="GrassScatter.Build"/> once with the field's origin + sampler.
    ///   3. Decomposes each produced Matrix4x4 into a TRS and pushes it into a new
    ///      <see cref="AuthoredInstancesData"/> sub-asset attached to the new <see cref="InstanceScatterLayer"/>.
    ///   4. Creates the new <see cref="InstanceScatterLayer"/> via JSON round-trip (copies 26 shared fields),
    ///      swaps the config.layers entry, removes the source DensityScatterLayer sub-asset.
    ///
    /// Re-invoking shows a warning if the selected layer is already an InstanceScatterLayer.
    /// </summary>
    internal static class ScatterBakeToAuthored
    {
        private const string MENU = "Tools/GrassInteract/Bake Procedural Layer to Authored";

        [MenuItem(MENU, validate = true)]
        private static bool Validate() => Selection.activeObject is DensityScatterLayer;

        [MenuItem(MENU)]
        private static void Run()
        {
            var srcLayer = Selection.activeObject as DensityScatterLayer;
            if (srcLayer == null)
            {
                EditorUtility.DisplayDialog("Bake to Authored",
                    "Select a DensityScatterLayer asset in the Project window first.", "OK");
                return;
            }

            // Find config that owns this layer.
            string layerPath = AssetDatabase.GetAssetPath(srcLayer);
            if (string.IsNullOrEmpty(layerPath))
            {
                EditorUtility.DisplayDialog("Bake to Authored",
                    "Could not determine asset path for the selected layer. Save the config asset first.", "OK");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(layerPath);
            if (config == null)
            {
                EditorUtility.DisplayDialog("Bake to Authored",
                    "Could not find a TerrainScatterConfig at the layer's asset path. " +
                    "The layer must be a sub-asset of a TerrainScatterConfig.", "OK");
                return;
            }

            // Find a referencing ScatterField in the active scene.
            ScatterField? field = FindFieldForLayer(srcLayer);
            if (field == null)
            {
                EditorUtility.DisplayDialog("Bake to Authored",
                    "No enabled ScatterField in the active scene references this layer's config. " +
                    "Open the scene that uses this layer, then re-run the menu.", "OK");
                return;
            }

            // Construct sampler + origin to mirror ScatterField.BuildContext logic.
            Terrain? boundTerrain = GetPrivateRef<Terrain>(field, "boundTerrain");
            Vector3 origin;
            ISurfaceSampler sampler;
            if (boundTerrain != null && boundTerrain.terrainData != null)
            {
                sampler = new TerrainSurfaceSampler(boundTerrain);
                Vector3 terrainSize = boundTerrain.terrainData.size;
                Vector3 terrainPos  = boundTerrain.transform.position;
                origin = terrainPos + new Vector3(terrainSize.x * 0.5f, 0f, terrainSize.z * 0.5f);
            }
            else
            {
                sampler = new RaycastSurfaceSampler(srcLayer.GroundSnapMask, field.transform.position.y);
                origin  = field.transform.position;
            }

            // Validate before bake.
            if (!srcLayer.Validate(out string err))
            {
                EditorUtility.DisplayDialog("Bake to Authored", $"Layer invalid: {err}", "OK");
                return;
            }

            // Pool large enough for one bake.
            var pool = new InstanceBatchPool(prewarmSlabs: 32);

            GrassScatterResult result;
            try
            {
                result = GrassScatter.Build(srcLayer, origin, pool, sampler);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Bake to Authored",
                    $"GrassScatter.Build threw: {ex.GetType().Name}: {ex.Message}", "OK");
                return;
            }

            // Pull TRS out of result.BaseSlabs.
            var records = new List<InstanceRecord>(result.TotalCount);
            for (int b = 0; b < result.BaseSlabs.Length; ++b)
            {
                Matrix4x4[] slab = result.BaseSlabs[b];
                int count = result.SlabCounts[b];
                for (int k = 0; k < count; ++k)
                {
                    Matrix4x4 m = slab[k];
                    // V2: scale is float (uniform). Collapse lossyScale to average XYZ.
                    Vector3 ls = m.lossyScale;
                    records.Add(new InstanceRecord
                    {
                        position     = m.GetColumn(3),
                        rotation     = m.rotation,
                        scale        = (ls.x + ls.y + ls.z) / 3f,
                        overrideMask = InstanceOverrideMask.None,
                    });
                }
            }
            GrassScatter.ReturnSlabs(result, pool);

            // 1. Create new InstanceScatterLayer + copy shared fields via JSON round-trip.
            var newLayer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(srcLayer), newLayer);
            newLayer.name = srcLayer.name;

            // 2. Create AuthoredInstancesData sub-asset and attach it to the config path.
            var sidecar = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            sidecar.name = $"{newLayer.name}.AuthoredInstances";
            AssetDatabase.AddObjectToAsset(newLayer, layerPath);
            AssetDatabase.AddObjectToAsset(sidecar, layerPath);
            AssetDatabase.ImportAsset(layerPath);

            // 3. Populate sidecar.
            for (int i = 0; i < records.Count; ++i)
                sidecar.AddRecord(records[i]);
            sidecar.PackBlob();

            // 4. Wire the authoredInstances reference on newLayer via SerializedObject.
            var soNew = new SerializedObject(newLayer);
            soNew.Update();
            SerializedProperty authoredProp = soNew.FindProperty("authoredInstances");
            if (authoredProp != null)
            {
                authoredProp.objectReferenceValue = sidecar;
                soNew.ApplyModifiedPropertiesWithoutUndo();
            }

            // 5. Swap config.layers entry: srcLayer → newLayer (inlined; MigrateScatterLayerTypes deleted in Phase A).
            SwapLayerInConfig(config, srcLayer, newLayer);
            EditorUtility.SetDirty(config);

            // 6. Remove old DensityScatterLayer sub-asset.
            AssetDatabase.RemoveObjectFromAsset(srcLayer);

            EditorUtility.SetDirty(newLayer);
            EditorUtility.SetDirty(sidecar);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Bake to Authored",
                $"Baked {records.Count} instances from procedural scatter into '{newLayer.name}'.\n\n" +
                "A new InstanceScatterLayer sub-asset has been created. " +
                "The source DensityScatterLayer has been removed.",
                "OK");

            Debug.Log($"[ScatterBakeToAuthored] Layer '{newLayer.name}' → {records.Count} records authored.", newLayer);
        }

        private static T? GetPrivateRef<T>(object instance, string fieldName) where T : class
        {
            var f = instance.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return f?.GetValue(instance) as T;
        }

        private static ScatterField? FindFieldForLayer(ScatterLayer layer)
        {
            ScatterField[] fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (var f in fields)
            {
                if (f == null || !f.isActiveAndEnabled || f.Config == null) continue;
                foreach (var L in f.Config.Layers)
                {
                    if (L == layer)
                        return f;
                }
            }
            return null;
        }

        /// <summary>
        /// Replaces <paramref name="oldLayer"/> with <paramref name="newLayer"/> inside
        /// <paramref name="config"/>'s layers array via SerializedObject.
        /// Inlined here after MigrateScatterLayerTypes was deleted in Phase A.
        /// </summary>
        private static void SwapLayerInConfig(
            TerrainScatterConfig config, ScatterLayer oldLayer, ScatterLayer newLayer)
        {
            using var so = new SerializedObject(config);
            so.Update();
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null) return;
            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty elem = layers.GetArrayElementAtIndex(i);
                if (elem.objectReferenceValue == oldLayer)
                {
                    elem.objectReferenceValue = newLayer;
                    break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
