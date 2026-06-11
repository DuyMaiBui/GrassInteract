#nullable enable
using UnityEditor;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="TerrainTileAsset"/>.
    ///
    /// All sculpt/paint UI has moved to <see cref="GpuTerrainRendererEditor"/>.
    /// This inspector shows only a managed-by notice so users know where to go.
    /// </summary>
    [CustomEditor(typeof(TerrainTileAsset))]
    public sealed class TerrainTileAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Managed by GpuTerrainRenderer. Select the renderer to sculpt.",
                MessageType.Info);
        }
    }
}
