#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Scene-view brush EditorTool for GPU terrain sculpt and paint.
    ///
    /// Mirrors the GrassInteract <c>DensityPaintTool</c> UX pattern:
    ///   — AddDefaultControl claims scene clicks from the scene view.
    ///   — Raycasts for world brush point (physics collider first, tile-plane fallback).
    ///   — MouseDown/Drag/Up stroke lifecycle delegated to <see cref="TerrainBrushStroke"/>.
    ///   — Live writeback throttled to 0.15 s (mirrors DensityPaintTool.READBACK_INTERVAL).
    ///   — World-space decal via <see cref="TerrainBrushPreview"/> tinted by sculpt mode.
    ///
    /// Activated via the "Activate Sculpt Tool" button in <see cref="TerrainTileAssetEditor"/>
    /// or the Tools overlay. Brush settings live in the shared <see cref="TerrainSculptState"/>.
    /// </summary>
    [EditorTool("Terrain Sculpt")]
    internal sealed class TerrainSculptTool : EditorTool
    {
        private TerrainTileAsset?        activeTile;
        private TerrainTileGpuResources? activeGpu;
        private RenderTexture?           heightRT;
        private RenderTexture?           splatRT;
        private ComputeShader?           brushCompute;

        private readonly TerrainSculptUndo       undo      = new TerrainSculptUndo();
        private readonly TerrainSculptRtWriteback writeback = new TerrainSculptRtWriteback();
        private TerrainBrushStroke? stroke;

        public override GUIContent toolbarIcon =>
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolRaise");

        public override void OnActivated()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainBrush.compute");
            this.stroke = new TerrainBrushStroke(this.undo, this.writeback);
            EditorApplication.update += this.OnEditorUpdate;
            this.BindTile(TerrainSculptState.ActiveTile);
        }

        public override void OnWillBeDeactivated()
        {
            EditorApplication.update -= this.OnEditorUpdate;
            this.ReleaseTileResources();
        }

        private void OnEditorUpdate() => this.writeback.Tick();
        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;

            // Re-bind if the tile changed in the inspector.
            if (this.activeTile != TerrainSculptState.ActiveTile)
                this.BindTile(TerrainSculptState.ActiveTile);

            if (this.activeTile == null || this.brushCompute == null ||
                this.heightRT == null || this.splatRT == null ||
                this.stroke == null || this.activeGpu == null) return;

            Event e         = Event.current;
            int   controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            // Resolve brush world point (collider first, tile-plane fallback).
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = this.TryGetBrushWorldPoint(ray, out Vector3 worldPoint, out Vector3 normal);

            if (hasHit)
            {
                TerrainBrushPreview.Set(worldPoint, normal,
                    TerrainSculptState.BrushSize,
                    TerrainSculptState.BrushColor());
                HandleUtility.Repaint();
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                    GUIUtility.hotControl = controlId;
                    this.stroke.BeginStroke(this.activeTile);
                    this.stroke.Dispatch(worldPoint, this.activeTile,
                        this.brushCompute, this.heightRT, this.splatRT);
                    this.stroke.ThrottledWriteback(true, this.activeTile,
                        this.activeGpu, this.heightRT, this.splatRT);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.stroke.InStroke:
                    if (hasHit)
                    {
                        this.stroke.Dispatch(worldPoint, this.activeTile,
                            this.brushCompute, this.heightRT, this.splatRT);
                        this.stroke.ThrottledWriteback(false, this.activeTile,
                            this.activeGpu, this.heightRT, this.splatRT);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.stroke.InStroke:
                    this.stroke.EndStroke(this.activeTile, this.activeGpu,
                        this.heightRT, this.splatRT);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            this.DrawHud();
        }

        /// <summary>Physics raycast first; falls back to tile mid-height XZ plane.</summary>
        private bool TryGetBrushWorldPoint(Ray ray, out Vector3 worldPoint, out Vector3 normal)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                worldPoint = hit.point;
                normal     = hit.normal;
                return true;
            }

            if (this.activeTile != null)
            {
                Vector2 origin2d = TerrainWorldGrid.TileOriginWorld(this.activeTile.tileCoord);
                float   midY     = (this.activeTile.minHeight + this.activeTile.maxHeight) * 0.5f;
                var     plane    = new Plane(Vector3.up, new Vector3(origin2d.x, midY, origin2d.y));
                if (plane.Raycast(ray, out float dist))
                {
                    worldPoint = ray.GetPoint(dist);
                    normal     = Vector3.up;
                    return true;
                }
            }

            worldPoint = Vector3.zero;
            normal     = Vector3.up;
            return false;
        }

        internal void BindTile(TerrainTileAsset? tile)
        {
            this.ReleaseTileResources();
            this.activeTile = tile;
            if (tile == null) return;

            if (!tile.IsHeightValid) tile.heightData = new byte[tile.ExpectedHeightBytes];
            if (!tile.IsSplatValid)  tile.splatData  = new byte[tile.ExpectedSplatBytes];

            int res       = TerrainSculptConfig.BRUSH_RT_RES;
            this.heightRT = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
                { name = "TerrainToolHeightRT", enableRandomWrite = true };
            this.heightRT.Create();

            this.splatRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat)
                { name = "TerrainToolSplatRT", enableRandomWrite = true };
            this.splatRT.Create();

            this.activeGpu = new TerrainTileGpuResources();
            this.activeGpu.Upload(tile);
        }

        private void ReleaseTileResources()
        {
            if (this.heightRT != null)
            {
                this.heightRT.Release();
                Object.DestroyImmediate(this.heightRT);
                this.heightRT = null;
            }
            if (this.splatRT != null)
            {
                this.splatRT.Release();
                Object.DestroyImmediate(this.splatRT);
                this.splatRT = null;
            }
            this.activeGpu?.Dispose();
            this.activeGpu  = null;
            this.activeTile = null;
        }

        private void DrawHud()
        {
            Handles.BeginGUI();
            var area = new Rect(8, 8, 210, 52);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Terrain Sculpt", EditorStyles.boldLabel);
            string modeName = TerrainSculptState.PaintMode
                ? $"Paint – Layer {TerrainSculptState.SplatLayer}"
                : TerrainSculptState.SculptSubMode.ToString();
            GUILayout.Label($"Mode: {modeName}", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
