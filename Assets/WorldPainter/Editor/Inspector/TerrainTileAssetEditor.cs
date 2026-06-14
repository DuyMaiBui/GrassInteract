#nullable enable
using UnityEditor;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="TerrainTileAsset"/>. Shows the managed-by notice
    /// and read-only resolutions. (Phase 3 cleanup — splat resolution selector removed;
    /// per-tile palette weights live in alphamaps[].)
    /// </summary>
    [CustomEditor(typeof(TerrainTileAsset))]
    public sealed class TerrainTileAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var tile = (TerrainTileAsset)this.target;

            EditorGUILayout.HelpBox(
                "Managed by WorldPainter. Select the WorldPainter component to sculpt.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Coord",      tile.tileCoord.ToString());
            EditorGUILayout.LabelField("Height res", $"{tile.heightRes}² ({(tile.IsHeightValid ? "valid" : "empty")})");
            EditorGUILayout.LabelField("Alphamaps",  $"{tile.AlphamapCount}");
        }
    }
}
