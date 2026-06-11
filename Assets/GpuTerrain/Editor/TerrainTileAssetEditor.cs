#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="TerrainTileAsset"/>.
    ///
    /// Replaces <c>TerrainSculptWindow</c> by embedding the full brush UI directly
    /// in the Inspector, mirroring how GrassInteract's Scatter Studio drives its
    /// density paint tool from the Inspector rather than a floating window.
    ///
    /// Sections:
    ///   1. Read-only tile summary (coord, resolutions, height range, validity, byte sizes).
    ///   2. Full brush UI ported from TerrainSculptWindow.OnGUI (mode, sliders, undo, save).
    ///   3. "Activate Sculpt Tool" button → ToolManager.SetActiveTool{TerrainSculptTool}.
    ///
    /// All brush state is stored in <see cref="TerrainSculptState"/> (shared SSOT with
    /// the EditorTool so both always reflect the same values).
    /// </summary>
    [CustomEditor(typeof(TerrainTileAsset))]
    public sealed class TerrainTileAssetEditor : UnityEditor.Editor
    {
        private static readonly string[] SCULPT_LABELS  = { "Raise", "Lower", "Smooth", "Flatten" };
        private static readonly string[] LAYER_LABELS   = { "Layer 0", "Layer 1", "Layer 2", "Layer 3" };

        // ── Shared undo + writeback (one per inspector session) ───────────────

        private readonly TerrainSculptUndo       undo      = new TerrainSculptUndo();
        private readonly TerrainSculptRtWriteback writeback = new TerrainSculptRtWriteback();

        private TerrainTileGpuResources? activeGpu;
        private RenderTexture?           heightRT;
        private RenderTexture?           splatRT;
        private ComputeShader?           brushCompute;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainBrush.compute");
            EditorApplication.update += this.OnEditorUpdate;

            // Bind active tile into shared state when this inspector is selected.
            var tile = (TerrainTileAsset)this.target;
            TerrainSculptState.ActiveTile = tile;
            this.EnsureRTs(tile);
        }

        private void OnDisable()
        {
            EditorApplication.update -= this.OnEditorUpdate;
            this.writeback.Dispose();
            this.ReleaseRTs();
        }

        private void OnEditorUpdate() => this.writeback.Tick();

        // ── Inspector GUI ──────────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            var tile = (TerrainTileAsset)this.target;

            // Re-bind if a different tile is selected.
            if (TerrainSculptState.ActiveTile != tile)
            {
                TerrainSculptState.ActiveTile = tile;
                this.EnsureRTs(tile);
            }

            DrawTileSummary(tile);
            EditorGUILayout.Space(6f);
            this.DrawBrushUI(tile);
            EditorGUILayout.Space(6f);
            DrawActivateButton();
        }

        // ── Section 1: read-only tile summary ─────────────────────────────────

        private static void DrawTileSummary(TerrainTileAsset tile)
        {
            EditorGUILayout.LabelField("Tile Info", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.LabelField("Coord",
                    $"({tile.tileCoord.x}, {tile.tileCoord.y})");
                EditorGUILayout.LabelField("Height Res",
                    $"{tile.heightRes} × {tile.heightRes}");
                EditorGUILayout.LabelField("Splat Res",
                    $"{tile.splatRes} × {tile.splatRes}");
                EditorGUILayout.LabelField("Height Range",
                    $"{tile.minHeight:F1} → {tile.maxHeight:F1} m");
                EditorGUILayout.LabelField("Height Data",
                    tile.IsHeightValid
                        ? $"{tile.heightData.Length:N0} bytes (valid)"
                        : "INVALID");
                EditorGUILayout.LabelField("Splat Data",
                    tile.IsSplatValid
                        ? $"{tile.splatData.Length:N0} bytes (valid)"
                        : "INVALID");
            }
        }

        // ── Section 2: brush UI ────────────────────────────────────────────────

        private void DrawBrushUI(TerrainTileAsset tile)
        {
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);

            // Mode toggle: Sculpt | Paint
            int modeIdx = TerrainSculptState.PaintMode ? 1 : 0;
            int newMode = GUILayout.Toolbar(modeIdx, new[] { "Sculpt", "Paint" });
            TerrainSculptState.PaintMode = newMode == 1;

            EditorGUILayout.Space(4f);

            if (TerrainSculptState.PaintMode)
                DrawPaintPanel();
            else
                DrawSculptPanel();

            EditorGUILayout.Space(6f);

            // Brush sliders.
            TerrainSculptState.BrushSize = EditorGUILayout.Slider(
                "Brush Size (m)", TerrainSculptState.BrushSize, 1f, 512f);
            TerrainSculptState.BrushStrength = EditorGUILayout.Slider(
                "Strength", TerrainSculptState.BrushStrength, 0.001f, 1f);

            EditorGUILayout.Space(6f);

            // Undo / Save row.
            EditorGUILayout.BeginHorizontal();
            bool canUndo = this.undo.CanUndo(tile.tileCoord);
            using (new EditorGUI.DisabledGroupScope(!canUndo))
            {
                if (GUILayout.Button("Undo"))
                    this.PerformUndo(tile);
            }
            if (GUILayout.Button("Save Now"))
                this.ForceSave(tile);
            EditorGUILayout.EndHorizontal();

            // Undo depth readout.
            int depth = this.undo.DepthForTile(tile.tileCoord);
            EditorGUILayout.LabelField(
                $"Undo depth: {depth}/{TerrainSculptConfig.UNDO_DEPTH}",
                EditorStyles.miniLabel);
        }

        private static void DrawSculptPanel()
        {
            int idx = GUILayout.Toolbar((int)TerrainSculptState.SculptSubMode, SCULPT_LABELS);
            TerrainSculptState.SculptSubMode = (SculptMode)idx;

            if (TerrainSculptState.SculptSubMode == SculptMode.Flatten)
            {
                TerrainSculptState.FlattenTarget = EditorGUILayout.Slider(
                    "Flatten Height [0-1]", TerrainSculptState.FlattenTarget, 0f, 1f);
            }
        }

        private static void DrawPaintPanel()
        {
            TerrainSculptState.SplatLayer = GUILayout.SelectionGrid(
                TerrainSculptState.SplatLayer, LAYER_LABELS, 2);
        }

        // ── Section 3: activate button ────────────────────────────────────────

        private static void DrawActivateButton()
        {
            bool isActive = ToolManager.activeToolType == typeof(TerrainSculptTool);
            GUI.color = isActive ? Color.green : Color.white;
            if (GUILayout.Button(isActive ? "Sculpt Tool Active" : "Activate Sculpt Tool",
                GUILayout.Height(28f)))
            {
                if (!isActive)
                    ToolManager.SetActiveTool<TerrainSculptTool>();
                else
                    ToolManager.RestorePreviousTool();
            }
            GUI.color = Color.white;
        }

        // ── Undo / Save ────────────────────────────────────────────────────────

        private void PerformUndo(TerrainTileAsset tile)
        {
            var snap = this.undo.Pop(tile);
            if (snap == null || this.activeGpu == null) return;
            this.activeGpu.Upload(tile);
            EditorUtility.SetDirty(tile);
        }

        private void ForceSave(TerrainTileAsset tile)
        {
            if (this.activeGpu == null || this.heightRT == null || this.splatRT == null) return;
            this.writeback.ExecuteSync(tile, this.activeGpu, this.heightRT, this.splatRT);
            AssetDatabase.SaveAssetIfDirty(tile);
        }

        // ── RT management ──────────────────────────────────────────────────────

        private void EnsureRTs(TerrainTileAsset tile)
        {
            this.ReleaseRTs();

            if (!tile.IsHeightValid) tile.heightData = new byte[tile.ExpectedHeightBytes];
            if (!tile.IsSplatValid)  tile.splatData  = new byte[tile.ExpectedSplatBytes];

            int res      = TerrainSculptConfig.BRUSH_RT_RES;
            this.heightRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
                { name = "InspectorHeightRT", enableRandomWrite = true };
            this.heightRT.Create();

            this.splatRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
                { name = "InspectorSplatRT", enableRandomWrite = true };
            this.splatRT.Create();

            this.activeGpu = new TerrainTileGpuResources();
            this.activeGpu.Upload(tile);
        }

        private void ReleaseRTs()
        {
            if (this.heightRT != null)
            {
                this.heightRT.Release();
                DestroyImmediate(this.heightRT);
                this.heightRT = null;
            }
            if (this.splatRT != null)
            {
                this.splatRT.Release();
                DestroyImmediate(this.splatRT);
                this.splatRT = null;
            }
            this.activeGpu?.Dispose();
            this.activeGpu = null;
        }
    }
}
