#nullable enable
using UnityEditor;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="TerrainTileAsset"/>.
    ///
    /// All sculpt/paint UI lives on the <see cref="WorldPainter"/> component.
    /// This inspector shows only a managed-by notice so users know where to go.
    /// </summary>
    [CustomEditor(typeof(TerrainTileAsset))]
    public sealed class TerrainTileAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Managed by WorldPainter. Select the WorldPainter component to sculpt.",
                MessageType.Info);
        }
    }
}
