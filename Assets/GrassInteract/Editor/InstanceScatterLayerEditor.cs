#nullable enable
using UnityEditor;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Inspector for <see cref="InstanceScatterLayer"/> — draws the default inspector and routes any
    /// edit through <see cref="ScatterRebuildScheduler"/> (via <see cref="ScatterFieldLookup"/>) so a
    /// field change re-scatters the owning field's layer live (debounced). Per-instance editing lives
    /// in the <see cref="InstancePlacementTool"/> overlay, not here.
    /// </summary>
    [CustomEditor(typeof(InstanceScatterLayer))]
    internal sealed class InstanceScatterLayerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            this.DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
                ScatterFieldLookup.MarkDirtyForLayer((ScatterLayer)this.target);
        }
    }
}
