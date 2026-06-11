#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// EditorWindow for GPU terrain sculpt + paint (Phase 5).
    ///
    /// Scatter-Studio-style layout:
    ///   Left panel:  mode toggle (sculpt / paint), layer picker, undo/redo.
    ///   Right panel: brush sliders, active-tile readout.
    ///
    /// Opens via Window > GPU Terrain > Sculpt + Paint.
    /// </summary>
    public sealed class TerrainSculptWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private TerrainTileAsset?         activeTile;
        private TerrainTileGpuResources?  activeGpu;
        private RenderTexture?            heightRT;
        private RenderTexture?            splatRT;

        private TerrainSculptUndo       undo      = new TerrainSculptUndo();
        private TerrainSculptRtWriteback writeback = new TerrainSculptRtWriteback();
        private ComputeShader?           brushCompute;
        private TerrainBrushController?  controller;

        // ── UI state ──────────────────────────────────────────────────────────

        private bool paintMode;
        private int  activeSplatLayer;
        private SculptMode sculpt = SculptMode.Raise;

        // Slider values.
        private float brushSize     = TerrainSculptConfig.DEFAULT_BRUSH_SIZE_M;
        private float brushStrength = TerrainSculptConfig.DEFAULT_BRUSH_STRENGTH;
        private float flattenTarget = 0.5f;

        private static readonly string[] LAYER_LABELS = { "Layer 0 (Base)", "Layer 1 (Grass)",
                                                           "Layer 2 (Rock)",  "Layer 3 (Path)" };
        private static readonly string[] SCULPT_LABELS = { "Raise", "Lower", "Smooth", "Flatten" };

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Window/GPU Terrain/Sculpt + Paint")]
        public static void Open()
        {
            var win = GetWindow<TerrainSculptWindow>("Terrain Sculpt");
            win.minSize = new Vector2(300f, 350f);
            win.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainBrush.compute");

            SceneView.duringSceneGui += this.OnSceneGUI;
            EditorApplication.update += this.OnEditorUpdate;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= this.OnSceneGUI;
            EditorApplication.update -= this.OnEditorUpdate;
            this.controller?.Dispose();
            this.writeback.Dispose();
            this.ReleaseRTs();
        }

        // ── Editor update (pump async writeback) ──────────────────────────────

        private void OnEditorUpdate()
        {
            this.writeback.Tick();
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);

            // Active tile picker.
            EditorGUI.BeginChangeCheck();
            var newTile = (TerrainTileAsset?)EditorGUILayout.ObjectField(
                "Active Tile", this.activeTile, typeof(TerrainTileAsset), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
                this.SetActiveTile(newTile);

            if (this.activeTile == null)
            {
                EditorGUILayout.HelpBox("Select a TerrainTileAsset to sculpt.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Tile coord: {this.activeTile.tileCoord}", EditorStyles.miniLabel);
            EditorGUILayout.Space(4f);

            // Mode toggle.
            bool newPaint = GUILayout.Toolbar(this.paintMode ? 1 : 0,
                new[] { "Sculpt", "Paint" }) == 1;
            if (newPaint != this.paintMode)
            {
                this.paintMode = newPaint;
                this.UpdateControllerMode();
            }

            EditorGUILayout.Space(4f);

            if (this.paintMode)
                this.DrawPaintPanel();
            else
                this.DrawSculptPanel();

            EditorGUILayout.Space(8f);

            // Brush sliders.
            this.brushSize = EditorGUILayout.Slider(
                "Brush Size (m)", this.brushSize, 1f, 512f);
            this.brushStrength = EditorGUILayout.Slider(
                "Strength", this.brushStrength, 0.001f, 1f);

            if (this.controller != null)
            {
                this.controller.BrushSizeM    = this.brushSize;
                this.controller.BrushStrength = this.brushStrength;
            }

            EditorGUILayout.Space(8f);

            // Undo / redo row.
            EditorGUILayout.BeginHorizontal();
            bool canUndo = this.activeTile != null &&
                           this.undo.CanUndo(this.activeTile.tileCoord);
            EditorGUI.BeginDisabledGroup(!canUndo);
            if (GUILayout.Button("Undo"))
                this.PerformUndo();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Save Now"))
                this.ForceSave();
            EditorGUILayout.EndHorizontal();

            // Undo depth readout.
            if (this.activeTile != null)
            {
                int depth = this.undo.DepthForTile(this.activeTile.tileCoord);
                EditorGUILayout.LabelField(
                    $"Undo depth: {depth}/{TerrainSculptConfig.UNDO_DEPTH}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawSculptPanel()
        {
            int idx = GUILayout.Toolbar((int)this.sculpt, SCULPT_LABELS);
            this.sculpt = (SculptMode)idx;

            if (this.sculpt == SculptMode.Flatten)
            {
                this.flattenTarget = EditorGUILayout.Slider(
                    "Flatten Height [0-1]", this.flattenTarget, 0f, 1f);
            }

            if (this.controller != null)
            {
                this.controller.Mode         = this.sculpt;
                this.controller.FlattenTarget = this.flattenTarget;
            }
        }

        private void DrawPaintPanel()
        {
            this.activeSplatLayer = GUILayout.SelectionGrid(
                this.activeSplatLayer, LAYER_LABELS, 2);

            if (this.controller != null)
            {
                this.controller.Mode       = SculptMode.Paint;
                this.controller.SplatLayer = this.activeSplatLayer;
            }
        }

        // ── Scene GUI ─────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (this.activeTile == null || this.controller == null ||
                this.heightRT == null || this.splatRT == null) return;

            Event evt = Event.current;
            bool consumed = this.controller.HandleSceneEvent(
                evt, sceneView,
                this.activeTile, this.activeGpu!,
                this.heightRT, this.splatRT,
                this.activeTile.tileCoord);

            if (consumed)
            {
                evt.Use();
                sceneView.Repaint();
                this.Repaint();
            }
        }

        // ── Tile management ───────────────────────────────────────────────────

        private void SetActiveTile(TerrainTileAsset? tile)
        {
            this.ReleaseRTs();
            this.controller?.Dispose();
            this.controller = null;

            this.activeTile = tile;
            if (tile == null) return;

            // Ensure asset data is valid.
            if (!tile.IsHeightValid)
            {
                tile.heightData = new byte[tile.ExpectedHeightBytes];
                EditorUtility.SetDirty(tile);
            }
            if (!tile.IsSplatValid)
            {
                tile.splatData = new byte[tile.ExpectedSplatBytes];
                EditorUtility.SetDirty(tile);
            }

            // Create working RTs.
            int res = TerrainSculptConfig.BRUSH_RT_RES;
            this.heightRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
            {
                name = "TerrainBrushHeightRT", enableRandomWrite = true
            };
            this.heightRT.Create();

            this.splatRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
            {
                name = "TerrainBrushSplatRT", enableRandomWrite = true
            };
            this.splatRT.Create();

            // Seed RTs from asset data (upload → blit via GPU resources).
            this.activeGpu = new TerrainTileGpuResources();
            this.activeGpu.Upload(tile);

            if (this.brushCompute != null)
            {
                this.controller = new TerrainBrushController(
                    this.undo, this.writeback, this.brushCompute);
            }
            else
            {
                Debug.LogWarning("[TerrainSculptWindow] TerrainBrush.compute not found.");
            }
        }

        private void PerformUndo()
        {
            if (this.activeTile == null || this.activeGpu == null) return;
            var snap = this.undo.Pop(this.activeTile);
            if (snap == null) return;

            // Re-upload GPU resources from restored bytes.
            this.activeGpu.Upload(this.activeTile);
            EditorUtility.SetDirty(this.activeTile);
        }

        private void ForceSave()
        {
            if (this.activeTile == null || this.activeGpu == null ||
                this.heightRT == null || this.splatRT == null) return;

            this.writeback.ExecuteSync(
                this.activeTile, this.activeGpu, this.heightRT, this.splatRT);
            AssetDatabase.SaveAssetIfDirty(this.activeTile);
        }

        private void UpdateControllerMode()
        {
            if (this.controller == null) return;
            if (this.paintMode)
            {
                this.controller.Mode       = SculptMode.Paint;
                this.controller.SplatLayer = this.activeSplatLayer;
            }
            else
            {
                this.controller.Mode = this.sculpt;
            }
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
