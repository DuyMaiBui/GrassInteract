#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="GpuTerrainRenderer"/>.
    ///
    /// Draws three foldout sections: Tiles, LOD Setup, Sculpt.
    /// Never draws cullCompute / patchMaterial (they are hidden infra).
    /// Sculpt panel draw methods live in GpuTerrainRendererEditor.Sculpt.cs (partial).
    /// </summary>
    [CustomEditor(typeof(GpuTerrainRenderer))]
    public sealed partial class GpuTerrainRendererEditor : UnityEditor.Editor
    {
        private const string PREF_TILES  = "GpuTerrainRendererEditor.TilesFoldout";
        private const string PREF_LOD    = "GpuTerrainRendererEditor.LodFoldout";
        private const string PREF_SCULPT = "GpuTerrainRendererEditor.SculptFoldout";

        // Undo uses the shared SSOT on TerrainSculptState — never construct a separate instance.
        // Writeback is inspector-owned (drives its own EditorApplication.update pump).
        private readonly TerrainSculptRtWriteback writeback = new TerrainSculptRtWriteback();

        private SerializedProperty? tilesProp;
        private SerializedProperty? lodProp;

        private void OnEnable()
        {
            this.tilesProp = this.serializedObject.FindProperty("tiles");
            this.lodProp   = this.serializedObject.FindProperty("lodRangesM");

            var renderer = (GpuTerrainRenderer)this.target;
            // H1: clear BOTH LastStrokedCoord AND LastStrokedTileSet atomically when the
            // inspected renderer changes — stale coords from the previous renderer must not
            // drive Undo/Save on the new one (would clobber wrong terrain if coords collide).
            if (TerrainSculptState.ActiveRenderer != renderer)
                TerrainSculptState.ResetLastStroked();
            TerrainSculptState.ActiveRenderer = renderer;
            EditorApplication.update += this.writeback.Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= this.writeback.Tick;
            this.writeback.Dispose();
        }

        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();
            var renderer = (GpuTerrainRenderer)this.target;

            this.DrawTilesFoldout(renderer);
            EditorGUILayout.Space(4f);
            this.DrawLodFoldout();
            EditorGUILayout.Space(4f);
            this.DrawSculptFoldout(renderer);

            this.serializedObject.ApplyModifiedProperties();
        }

        // ── Tiles foldout ─────────────────────────────────────────────────────

        private void DrawTilesFoldout(GpuTerrainRenderer renderer)
        {
            bool open = SessionState.GetBool(PREF_TILES, true);
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, "Tiles");
            SessionState.SetBool(PREF_TILES, open);
            if (open && this.tilesProp != null)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < this.tilesProp.arraySize; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    var elem = this.tilesProp.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(elem, GUIContent.none);
                    var tileAsset = elem.objectReferenceValue as TerrainTileAsset;
                    if (tileAsset != null)
                    {
                        string summary = $"({tileAsset.tileCoord.x},{tileAsset.tileCoord.y}) " +
                            $"{tileAsset.heightRes}² " +
                            $"{tileAsset.minHeight:F0}..{tileAsset.maxHeight:F0}m";
                        EditorGUILayout.LabelField(summary, EditorStyles.miniLabel,
                            GUILayout.Width(160f));
                    }
                    if (GUILayout.Button("-", GUILayout.Width(20f)))
                    {
                        this.tilesProp.DeleteArrayElementAtIndex(i);
                        this.serializedObject.ApplyModifiedProperties();
                        renderer.SendMessage("Rebuild", SendMessageOptions.DontRequireReceiver);
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
                if (GUILayout.Button("+ Add Tile"))
                {
                    this.tilesProp.arraySize++;
                    this.tilesProp.GetArrayElementAtIndex(this.tilesProp.arraySize - 1)
                        .objectReferenceValue = null;
                    this.serializedObject.ApplyModifiedProperties();
                    renderer.SendMessage("Rebuild", SendMessageOptions.DontRequireReceiver);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ── LOD foldout ───────────────────────────────────────────────────────

        private void DrawLodFoldout()
        {
            bool open = SessionState.GetBool(PREF_LOD, true);
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, "LOD Setup (shared)");
            SessionState.SetBool(PREF_LOD, open);
            if (open && this.lodProp != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(this.lodProp, new GUIContent("Ranges (m)"), true);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ── Undo / Save (backing logic) ───────────────────────────────────────

        private void PerformUndo(GpuTerrainRenderer renderer)
        {
            // P2: pop every tile touched by the last stroke atomically (multi-tile undo).
            foreach (var coord in TerrainSculptState.LastStrokedTileSet)
            {
                var tile = FindTileForCoord(renderer, coord);
                if (tile == null) continue;
                var snap = TerrainSculptState.Undo.Pop(tile);
                if (snap == null) continue;
                var gpu = renderer.ResourcesForCoord(coord);
                gpu?.Upload(tile);
                renderer.CommitHeight(coord);
                EditorUtility.SetDirty(tile);
            }
        }

        private void ForceSave(GpuTerrainRenderer renderer, Vector2Int coord)
        {
            var tile = FindTileForCoord(renderer, coord);
            var gpu  = renderer.ResourcesForCoord(coord);
            if (tile == null || gpu == null) return;
            // Persists the last-COMMITTED bytes (tile.heightData/splatData) to disk.
            // M1: this saves what the most recent throttled/final writeback committed,
            // NOT in-flight RT data — the RT is already committed by TerrainSculptRtWriteback
            // before mouse-up completes.
            gpu.Upload(tile);
            AssetDatabase.SaveAssetIfDirty(tile);
        }

        internal static TerrainTileAsset? FindTileForCoord(
            GpuTerrainRenderer renderer, Vector2Int coord)
        {
            var tiles = renderer.Tiles;
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i] != null && tiles[i].tileCoord == coord)
                    return tiles[i];
            return null;
        }
    }
}
