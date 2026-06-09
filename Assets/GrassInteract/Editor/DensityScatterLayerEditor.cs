#nullable enable
using UnityEditor;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Inspector for <see cref="DensityScatterLayer"/> — draws the default inspector and routes any
    /// edit through <see cref="ScatterRebuildScheduler"/> (via <see cref="ScatterFieldLookup"/>) so a
    /// field change re-scatters the owning field's layer live (debounced).
    /// </summary>
    [CustomEditor(typeof(DensityScatterLayer))]
    internal sealed class DensityScatterLayerEditor : UnityEditor.Editor
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
