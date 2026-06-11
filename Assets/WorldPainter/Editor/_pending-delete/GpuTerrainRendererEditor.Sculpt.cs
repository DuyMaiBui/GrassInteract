#nullable enable
using UnityEditor;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Sculpt foldout draw methods for <see cref="GpuTerrainRendererEditor"/> (partial).
    ///
    /// Kept in a separate file so the main inspector file stays under 200 lines.
    /// All sculpt UI: mode toggles, brush settings, Undo/Save buttons.
    /// </summary>
    public sealed partial class GpuTerrainRendererEditor
    {
        // ── Sculpt label arrays ───────────────────────────────────────────────

        private static readonly GUIContent[] SCULPT_LABELS = new GUIContent[]
        {
            new GUIContent("Raise"),
            new GUIContent("Lower"),
            new GUIContent("Smooth"),
            new GUIContent("Flatten"),
        };

        private static readonly GUIContent[] LAYER_LABELS = new GUIContent[]
        {
            new GUIContent("Layer 0"),
            new GUIContent("Layer 1"),
            new GUIContent("Layer 2"),
            new GUIContent("Layer 3"),
        };

        // ── Sculpt foldout (entry point from OnInspectorGUI) ──────────────────

        private void DrawSculptFoldout(GpuTerrainRenderer renderer)
        {
            bool open = SessionState.GetBool(PREF_SCULPT, false);
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, "Sculpt / Paint");
            SessionState.SetBool(PREF_SCULPT, open);
            if (open)
            {
                EditorGUI.indentLevel++;
                if (TerrainSculptState.PaintMode)
                    this.DrawPaintPanel(renderer);
                else
                    this.DrawSculptPanel(renderer);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ── Sculpt panel (height modes) ───────────────────────────────────────

        private void DrawSculptPanel(GpuTerrainRenderer renderer)
        {
            // Mode toggle
            if (GUILayout.Button("Switch to Paint Mode"))
                TerrainSculptState.PaintMode = true;

            EditorGUILayout.Space(2f);
            this.DrawSculptSubPanel();
            EditorGUILayout.Space(4f);
            this.DrawUndoSaveRow(renderer);
        }

        private void DrawSculptSubPanel()
        {
            // Sub-mode toolbar
            int modeIdx = (int)TerrainSculptState.SculptSubMode;
            int newIdx  = GUILayout.SelectionGrid(modeIdx, SCULPT_LABELS, 4);
            if (newIdx != modeIdx)
                TerrainSculptState.SculptSubMode = (SculptMode)newIdx;

            // Flatten target (only visible in Flatten mode)
            if (TerrainSculptState.SculptSubMode == SculptMode.Flatten)
            {
                TerrainSculptState.FlattenTarget = EditorGUILayout.Slider(
                    "Target (normalised)", TerrainSculptState.FlattenTarget, 0f, 1f);
            }

            // Brush parameters (shared with paint)
            DrawBrushParams();
        }

        // ── Paint panel (splat layers) ────────────────────────────────────────

        private void DrawPaintPanel(GpuTerrainRenderer renderer)
        {
            if (GUILayout.Button("Switch to Sculpt Mode"))
                TerrainSculptState.PaintMode = false;

            EditorGUILayout.Space(2f);

            // Layer selection
            int layerIdx = Mathf.Clamp(TerrainSculptState.SplatLayer, 0, LAYER_LABELS.Length - 1);
            int newLayer = GUILayout.SelectionGrid(layerIdx, LAYER_LABELS, 4);
            if (newLayer != layerIdx)
                TerrainSculptState.SplatLayer = newLayer;

            DrawBrushParams();
            EditorGUILayout.Space(4f);
            this.DrawUndoSaveRow(renderer);
        }

        // ── Shared brush parameters ───────────────────────────────────────────

        private static void DrawBrushParams()
        {
            TerrainSculptState.BrushSize = EditorGUILayout.Slider(
                "Brush Size (m)", TerrainSculptState.BrushSize, 0.5f, 50f);
            TerrainSculptState.BrushStrength = EditorGUILayout.Slider(
                "Brush Strength", TerrainSculptState.BrushStrength, 0f, 1f);
        }

        // ── Undo / Save button row ────────────────────────────────────────────

        private void DrawUndoSaveRow(GpuTerrainRenderer renderer)
        {
            var tileSet   = TerrainSculptState.LastStrokedTileSet;
            var lastCoord = TerrainSculptState.LastStrokedCoord;

            EditorGUILayout.BeginHorizontal();

            // Undo: enabled if ANY tile in the last stroked set has a snapshot.
            bool canUndo = false;
            foreach (var c in tileSet)
                if (TerrainSculptState.Undo.CanUndo(c)) { canUndo = true; break; }

            using (new EditorGUI.DisabledScope(!canUndo))
            {
                if (GUILayout.Button("Undo Last Stroke") && canUndo)
                    this.PerformUndo(renderer);
            }

            // Save: enabled when primary coord resolves to a real tile asset.
            bool canSave = lastCoord.HasValue &&
                           FindTileForCoord(renderer, lastCoord.Value) != null;
            using (new EditorGUI.DisabledScope(!canSave))
            {
                if (GUILayout.Button("Save Tile") && lastCoord.HasValue)
                    this.ForceSave(renderer, lastCoord.Value);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
