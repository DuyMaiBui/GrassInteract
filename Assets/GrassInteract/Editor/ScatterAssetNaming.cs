#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Naming-convention helpers for TerrainScatterConfig sub-assets.
    ///
    /// Convention:
    ///   Layer sub-asset:            Layer_&lt;Density|Instance&gt;_&lt;idx&gt;
    ///   Density-map texture:        Density_&lt;layerName&gt;
    ///   AuthoredInstancesData:      Authored_&lt;layerName&gt;
    ///
    /// Renames only on mismatch to avoid triggering unnecessary reimports.
    /// Scheduled via EditorApplication.delayCall to avoid mutating the AssetDatabase
    /// inside a postprocessor callback.
    /// </summary>
    internal static class ScatterAssetNaming
    {
        private static bool scheduled;

        /// <summary>
        /// Schedules a naming-convention pass on the given config via delayCall.
        /// Safe to call during an AssetPostprocessor callback or any editor event.
        /// </summary>
        public static void ScheduleNamingConvention(TerrainScatterConfig config)
        {
            if (scheduled) return;
            scheduled = true;
            var cfg = config;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                if (cfg != null)
                    ApplyNamingConvention(cfg);
            };
        }

        /// <summary>
        /// Immediately applies the sub-asset naming convention to all layers in the config.
        /// Renames mismatched sub-assets and saves if any rename occurred.
        /// </summary>
        public static void ApplyNamingConvention(TerrainScatterConfig config)
        {
            if (config == null) return;

            string configPath = AssetDatabase.GetAssetPath(config);
            if (string.IsNullOrEmpty(configPath)) return;

            bool dirty = false;

            for (int i = 0; i < config.Layers.Count; ++i)
            {
                ScatterLayer layer = config.Layers[i];
                if (layer == null) continue;

                // Expected layer name: Layer_Density_<i> or Layer_Instance_<i>.
                // We don't rename the layer itself here — the layer keeps its user name.
                // We only rename the sub-asset companions to follow the layer name.

                if (layer is DensityScatterLayer densLayer)
                {
                    dirty |= RenameCompanionTexture(densLayer, configPath);
                }
                else if (layer is InstanceScatterLayer instLayer)
                {
                    dirty |= RenameCompanionAuthored(instLayer, configPath);
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static bool RenameCompanionTexture(DensityScatterLayer layer, string configPath)
        {
            // Reach the serialized density map reference.
            using var so = new SerializedObject(layer);
            var texProp = so.FindProperty("densityMap");
            if (texProp == null) return false;

            var tex = texProp.objectReferenceValue as Texture2D;
            if (tex == null) return false;

            // Only rename if it's a sub-asset of this config (don't rename external textures).
            if (AssetDatabase.GetAssetPath(tex) != configPath) return false;

            string expected = $"Density_{layer.name}";
            if (tex.name == expected) return false;

            tex.name = expected;
            EditorUtility.SetDirty(tex);
            return true;
        }

        private static bool RenameCompanionAuthored(InstanceScatterLayer layer, string configPath)
        {
            using var so = new SerializedObject(layer);
            var authProp = so.FindProperty("authoredInstances");
            if (authProp == null) return false;

            var authored = authProp.objectReferenceValue as AuthoredInstancesData;
            if (authored == null) return false;

            if (AssetDatabase.GetAssetPath(authored) != configPath) return false;

            string expected = $"Authored_{layer.name}";
            if (authored.name == expected) return false;

            authored.name = expected;
            EditorUtility.SetDirty(authored);
            return true;
        }
    }
}
