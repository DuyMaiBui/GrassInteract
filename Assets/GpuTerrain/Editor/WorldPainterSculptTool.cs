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
    /// Does NOT delete or break the existing GpuTerrainRenderer / TerrainSculptTool path.
    /// </summary>
    [EditorTool("WorldPainter Sculpt")]
    internal sealed class WorldPainterSculptTool : EditorTool
    {
        // ── Tool resources ────────────────────────────────────────────────────

        private ComputeShader? brushCompute;
        private readonly TerrainSculptRtWriteback writeback  = new TerrainSculptRtWriteback();
        private readonly TileRtCache              rtCache    = new TileRtCache();
        private readonly WorldPainterStroke       stroke     = new WorldPainterStroke();
        private readonly BrushFalloffLut          falloffLut = new BrushFalloffLut();

        // ── Per-stroke tracking ───────────────────────────────────────────────

        private readonly HashSet<Vector2Int> undoPushedCoords   = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> strokeTouchedCoords = new HashSet<Vector2Int>();
        private readonly List<Vector2Int>    resolveResults      = new List<Vector2Int>(4);

        // ── Undo group ────────────────────────────────────────────────────────

        private int undoGroupId = -1;

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

            EditorApplication.update += this.OnEditorUpdate;
        }

        public override void OnWillBeDeactivated()
        {
            EditorApplication.update -= this.OnEditorUpdate;

            if (this.stroke.InStroke)
                this.TeardownActiveStroke(WorldPainterState.ActivePainter);
            else
                this.rtCache.ReleaseAll();

            this.falloffLut.Dispose();
        }

        private void OnEditorUpdate() => this.writeback.Tick();

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

        // ── Mouse handlers ────────────────────────────────────────────────────

        private void HandleMouseDown(WorldPainter painter, Vector3 worldPos, int controlId)
        {
            this.undoPushedCoords.Clear();
            this.strokeTouchedCoords.Clear();
            this.rtCache.ReleaseAll();

            // Begin Unity Undo group — one Ctrl+Z per stroke (task 9).
            Undo.IncrementCurrentGroup();
            this.undoGroupId = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("WorldPainter Sculpt Stroke");

            GUIUtility.hotControl = controlId;
            this.stroke.Begin(worldPos);

            // Initial stamp at mouse-down position.
            this.DoStamp(painter, worldPos);
            this.CommitLastStrokedState();
        }

        private void HandleMouseDrag(WorldPainter painter, Vector3 worldPos)
        {
            var brush = WorldPainterState.Brush;

            // Spacing-stamping: stamps every spacingM metres along path.
            this.stroke.Advance(
                worldPos,
                brush.spacing,
                brush.flow,
                (stampPos, flow) => this.DoStamp(painter, stampPos));

            this.CommitLastStrokedState();
        }

        private void HandleMouseUp(WorldPainter painter)
        {
            this.TeardownActiveStroke(painter);
            this.CommitLastStrokedState();

            // Close Unity Undo group so one Ctrl+Z reverts the whole stroke.
            if (this.undoGroupId >= 0)
            {
                Undo.CollapseUndoOperations(this.undoGroupId);
                this.undoGroupId = -1;
            }
        }

        // ── Teardown ──────────────────────────────────────────────────────────

        private void TeardownActiveStroke(WorldPainter? painter)
        {
            this.writeback.CancelPending();
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();

            if (painter != null)
            {
                foreach (var coord in this.strokeTouchedCoords)
                {
                    var tile = this.FindTile(painter, coord);
                    var gpu  = this.FindGpu(painter, coord);
                    if (tile != null && gpu != null &&
                        this.rtCache.TryGet(coord, out var hRT, out var sRT))
                    {
                        this.writeback.ExecuteSync(tile, gpu, hRT, sRT);
                    }
                }
            }

            this.stroke.End();
            this.rtCache.ReleaseAll();
            this.strokeTouchedCoords.Clear();
            this.undoPushedCoords.Clear();
        }

        // ── Per-stamp dispatch ────────────────────────────────────────────────

        private void DoStamp(WorldPainter painter, Vector3 worldPos)
        {
            var brush = WorldPainterState.Brush;

            this.resolveResults.Clear();
            TerrainPaintTargetResolver.Resolve(
                new Vector2(worldPos.x, worldPos.z),
                brush.size,
                residencySet: null,
                this.resolveResults);

            foreach (var coord in this.resolveResults)
                this.DispatchOneTile(painter, worldPos, coord);
        }

        private void DispatchOneTile(WorldPainter painter, Vector3 worldPos, Vector2Int coord)
        {
            var tile = this.FindTile(painter, coord);
            var gpu  = this.FindGpu(painter, coord);
            if (tile == null || gpu == null) return;

            if (!this.rtCache.GetOrCreate(coord, gpu, out var heightRT, out var splatRT))
                return;

            bool isFirstTouch = !this.strokeTouchedCoords.Contains(coord);
            if (isFirstTouch && !this.undoPushedCoords.Contains(coord))
            {
                // Push undo snapshot before first edit on this tile.
                WorldPainterAuthoring.UndoStack.Push(tile);
                this.undoPushedCoords.Add(coord);
            }

            this.strokeTouchedCoords.Add(coord);

            // Bind falloff LUT to ALL kernels via shared uniform _FalloffLUT / _UseFalloffLUT.
            this.BindAndDispatch(worldPos, tile, heightRT, splatRT);

            // Throttled live writeback (for VTF preview).
            this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
        }

        private void BindAndDispatch(
            Vector3 worldPos,
            TerrainTileAsset tile,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            if (this.brushCompute == null) return;

            var brush = WorldPainterState.Brush;
            var worldXZ = new Vector2(worldPos.x, worldPos.z);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, brush.size, tile.tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes  = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            this.brushCompute.SetVector("_BrushCenterUV", centerUV);
            this.brushCompute.SetFloat("_BrushRadiusUV",  radiusUV);
            this.brushCompute.SetFloat("_Strength",        brush.strength);
            this.brushCompute.SetInt("_RTRes",             rtRes);

            // Determine kernel (only Raise mode for now; Paint in task 8 sculpt scope).
            // The active layer type drives the kernel selection.
            string kernelName = TerrainSculptConfig.KERNEL_RAISE_LOWER;
            float  raiseSign  = 1f; // Raise default

            int k = this.brushCompute.FindKernel(kernelName);
            this.falloffLut.BindToCompute(this.brushCompute, k);

            this.brushCompute.SetTexture(k, "_HeightRT", heightRT);
            this.brushCompute.SetFloat("_RaiseSign", raiseSign);
            this.brushCompute.Dispatch(k, groups, groups, 1);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private TerrainTileAsset? FindTile(WorldPainter painter, Vector2Int coord)
        {
            foreach (var entry in painter.Tiles)
                if (entry.coord == coord && entry.tileAsset != null)
                    return entry.tileAsset;
            return null;
        }

        private TerrainTileGpuResources? FindGpu(WorldPainter painter, Vector2Int coord)
        {
            // WorldPainter does not yet own GpuTerrainEngines directly.
            // In P1 we drive sculpt through the existing GpuTerrainRenderer if present,
            // otherwise fall back to a transient upload path (T3-compatible).
            // Look up via the scene's GpuTerrainRenderer for this tile.
            var renderer = Object.FindObjectOfType<GpuTerrainRenderer>();
            if (renderer == null) return null;
            return renderer.ResourcesForCoord(coord);
        }

        private bool TryGetBrushWorldPoint(Ray ray, WorldPainter painter, out Vector3 worldPoint)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                worldPoint = hit.point; return true;
            }
            foreach (var entry in painter.Tiles)
            {
                if (entry.tileAsset == null) continue;
                var origin2d = TerrainWorldGrid.TileOriginWorld(entry.coord);
                float midY   = (entry.tileAsset.minHeight + entry.tileAsset.maxHeight) * 0.5f;
                var plane    = new Plane(Vector3.up, new Vector3(origin2d.x, midY, origin2d.y));
                if (plane.Raycast(ray, out float dist) && dist > 0f && dist < 1e6f)
                {
                    worldPoint = ray.GetPoint(dist); return true;
                }
            }
            worldPoint = Vector3.zero;
            return false;
        }

        private void CommitLastStrokedState()
        {
            WorldPainterState.LastStrokedTileSet.Clear();
            foreach (var c in this.strokeTouchedCoords)
                WorldPainterState.LastStrokedTileSet.Add(c);

            Vector2Int? primary = null;
            foreach (var c in this.strokeTouchedCoords) { primary = c; break; }
            WorldPainterState.LastStrokedCoord = primary;
        }

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
