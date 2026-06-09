#nullable enable
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Maps a <see cref="ScatterLayer"/> sub-asset back to the loaded <see cref="ScatterField"/>(s)
    /// that own it, so layer-inspector and tool edits can mark the right field+layer dirty for
    /// debounced re-scatter without the layer needing a back-reference to its field.
    /// </summary>
    internal static class ScatterFieldLookup
    {
        /// <summary>Marks every loaded field that owns <paramref name="layer"/> dirty at that layer's index.</summary>
        public static void MarkDirtyForLayer(ScatterLayer layer)
        {
            if (layer == null) return;
            var fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (var field in fields)
            {
                int idx = IndexOfLayer(field, layer);
                if (idx >= 0)
                    ScatterRebuildScheduler.MarkDirty(field, idx);
            }
        }

        /// <summary>Returns the first loaded field owning <paramref name="layer"/> and its layer index, or (null,-1).</summary>
        public static (ScatterField? field, int index) FindOwningField(ScatterLayer layer)
        {
            if (layer == null) return (null, -1);
            var fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (var field in fields)
            {
                int idx = IndexOfLayer(field, layer);
                if (idx >= 0)
                    return (field, idx);
            }
            return (null, -1);
        }

        private static int IndexOfLayer(ScatterField field, ScatterLayer layer)
        {
            if (field == null || field.Config == null) return -1;
            var layers = field.Config.Layers;
            for (int i = 0; i < layers.Count; ++i)
                if (ReferenceEquals(layers[i], layer))
                    return i;
            return -1;
        }
    }
}
