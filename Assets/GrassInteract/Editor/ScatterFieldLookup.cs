#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Editor-only lookup helpers that resolve the active <see cref="ScatterField"/>
    /// owning a config/layer. Shared by SceneView authoring tools and editor previews.
    /// </summary>
    internal static class ScatterFieldLookup
    {
        internal static bool TryFindSingleActiveFieldForLayer(
            ScatterLayer layer,
            out TerrainScatterConfig? config,
            out ScatterField? field,
            out int layerIndex,
            out string error)
        {
            config = null;
            field = null;
            layerIndex = -1;
            error = string.Empty;

            string assetPath = AssetDatabase.GetAssetPath(layer);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "Save the TerrainScatterConfig asset before using SceneView authoring tools.";
                return false;
            }

            config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(assetPath);
            if (config == null)
            {
                error = "Could not resolve the owning TerrainScatterConfig for this layer.";
                return false;
            }

            if (!TryFindSingleActiveFieldForConfig(config, out field, out error))
                return false;

            for (int i = 0; i < config.Layers.Count; ++i)
            {
                if (config.Layers[i] == layer)
                {
                    layerIndex = i;
                    return true;
                }
            }

            field = null;
            error = "The selected layer is not present in its owning TerrainScatterConfig.";
            return false;
        }

        internal static bool TryFindSingleActiveFieldForConfig(
            TerrainScatterConfig config,
            out ScatterField? field,
            out string error)
        {
            field = null;
            error = string.Empty;

            ScatterField[] fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            int matchCount = 0;

            foreach (ScatterField candidate in fields)
            {
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                if (candidate.Config != config) continue;

                field = candidate;
                matchCount++;
            }

            if (matchCount == 1)
                return true;

            field = null;
            error = matchCount == 0
                ? "No active ScatterField in the open scene references this config."
                : "Multiple active ScatterFields reference this config. Keep exactly one active field while SceneView authoring this layer.";
            return false;
        }
    }
}
