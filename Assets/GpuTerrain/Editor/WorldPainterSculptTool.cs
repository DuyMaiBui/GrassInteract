#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Scene-view EditorTool for WorldPainter sculpt (Task 8).
    ///
    /// Reuses the existing stroke plumbing (<see cref="TileRtCache"/>,
    /// <see cref="TerrainBrushStroke"/>, <see cref="TerrainSculptRtWriteback"/>)
    /// retargeted onto the <see cref="WorldPainter"/> tile refs.
    ///
    /// Spacing-stamping path: <see cref="WorldPainterStroke"/> interpolates the
    /// drag path and stamps every spacing metres.  The falloff LUT is uploaded
    /// once on tool activate and re-uploaded whenever the CurveField changes.
    ///
    /// Stroke undo: a single Ctrl+Z reverts one full stroke per Unity Undo group.
    /// On Ctrl+Z, <see cref="Undo.undoRedoPerformed"/> fires; the handler pops the
    /// WorldPainterUndo snapshot for every tile touched and re-uploads bytes to GPU.
    ///
    /// Stroke code is in <c>WorldPainterSculptTool.Stroke.cs</c> (partial).
    /// </summary>
    [EditorTool("WorldPainter Sculpt")]
    internal sealed partial class WorldPainterSculptTool : EditorTool
    {
        // ── Tool resources ────────────────────────────────────────────────────

        internal ComputeShader? brushCompute;
        internal readonly TerrainSculptRtWriteback writeback  = new TerrainSculptRtWriteback();
        internal readonly TileRtCache              rtCache    = new TileRtCache();
        internal readonly WorldPainterStroke       stroke     = new WorldPainterStroke();
        internal readonly BrushFalloffLut          falloffLut = new BrushFalloffLut();

        // ── Per-stroke tracking ───────────────────────────────────────────────

        internal readonly HashSet<Vector2Int> undoPushedCoords    = new HashSet<Vector2Int>();
        internal readonly HashSet<Vector2Int> strokeTouchedCoords = new HashSet<Vector2Int>();
        internal readonly List<Vector2Int>    resolveResults      = new List<Vector2Int>(4);

        // ── Undo group ────────────────────────────────────────────────────────

        internal int undoGroupId = -1;

        // ── Toolbar icon ──────────────────────────────────────────────────────

        public override GUIContent toolbarIcon =>
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolRaise");

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public override void OnActivated()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainBrush.compute");

            // Upload initial falloff LUT from current brush settings.
            var brush = WorldPainterState.Brush;
            this.falloffLut.Upload(brush.falloff);

            // Subscribe: re-upload LUT when CurveField changes without re-activating.
            WorldPainterState.BrushFalloffDirty += this.OnBrushFalloffDirty;

            // Subscribe: Ctrl+Z triggers our custom per-tile snapshot revert.
            Undo.undoRedoPerformed += this.OnUndoRedoPerformed;

            EditorApplication.update += this.OnEditorUpdate;
        }

        public override void OnWillBeDeactivated()
        {
            WorldPainterState.BrushFalloffDirty -= this.OnBrushFalloffDirty;
            Undo.undoRedoPerformed -= this.OnUndoRedoPerformed;
            EditorApplication.update -= this.OnEditorUpdate;

            if (this.stroke.InStroke)
                this.TeardownActiveStroke(WorldPainterState.ActivePainter);
            else
                this.rtCache.ReleaseAll();

            this.falloffLut.Dispose();
        }

        private void OnEditorUpdate() => this.writeback.Tick();

        // ── LUT re-upload (finding #3) ────────────────────────────────────────

        private void OnBrushFalloffDirty()
        {
            this.falloffLut.Upload(WorldPainterState.Brush.falloff);
        }

        // ── Ctrl+Z stroke revert (finding #2) ────────────────────────────────

        /// <summary>
        /// Called by Unity when any Undo or Redo fires.
        /// We pop the WorldPainterUndo snapshot for every tile that was touched by the
        /// last stroke and re-upload bytes to GPU so the visual reverts immediately.
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            // Pop the latest WorldPainter snapshot for every tile that was last-stroked.
            foreach (var coord in WorldPainterState.LastStrokedTileSet)
            {
                var tile = this.FindTile(painter, coord);
                if (tile == null) continue;

                var snap = WorldPainterAuthoring.UndoStack.Pop(tile);
                if (snap == null) continue;

                // Re-upload the restored bytes to GPU.
                var gpu = this.FindGpu(painter, coord);
                if (gpu == null) continue;

                if (!this.rtCache.GetOrCreate(coord, gpu, out var heightRT, out var splatRT))
                    continue;

                // Write restored CPU bytes back to the RT and commit synchronously.
                this.writeback.ExecuteSync(tile, gpu, heightRT, splatRT);
                painter.CommitHeight(coord);
            }
        }

        // ── Scene view GUI ────────────────────────────────────────────────────

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;
            if (this.brushCompute == null) return;

            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            Event e         = Event.current;
            int   controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            Ray  ray    = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = this.TryGetBrushWorldPoint(ray, painter, out Vector3 worldPoint);

            if (hasHit)
            {
                var brush = WorldPainterState.Brush;
                var previewColor = new Color(0.3f, 0.7f, 1.0f, 0.6f); // WorldPainter blue
                TerrainBrushPreview.Set(worldPoint, brush.size, previewColor, null);
                HandleUtility.Repaint();
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                    this.HandleMouseDown(painter, worldPoint, controlId);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.stroke.InStroke:
                    if (hasHit) this.HandleMouseDrag(painter, worldPoint);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.stroke.InStroke:
                    this.HandleMouseUp(painter);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            this.DrawHud();
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void DrawHud()
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 230, 52);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("WorldPainter Sculpt", EditorStyles.boldLabel);
            var brush = WorldPainterState.Brush;
            GUILayout.Label($"Size: {brush.size:F1}m  Strength: {brush.strength:F2}",
                EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
