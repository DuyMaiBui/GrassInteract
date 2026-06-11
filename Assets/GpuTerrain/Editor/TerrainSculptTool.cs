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
    ///   — MouseDown/Drag/Up stroke lifecycle dispatches compute kernels directly.
    ///   — Live writeback throttled to 0.15 s (mirrors DensityPaintTool.READBACK_INTERVAL).
    ///   — World-space decal via <see cref="TerrainBrushPreview"/> tinted by sculpt mode.
    ///
    /// Activated via the "Activate Sculpt Tool" button in <see cref="TerrainTileAssetEditor"/>
    /// or the Tools overlay. Brush settings live in the shared <see cref="TerrainSculptState"/>.
    /// </summary>
    [EditorTool("Terrain Sculpt")]
    internal sealed class TerrainSculptTool : EditorTool
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const double READBACK_INTERVAL = 0.15;

        // ── Per-activation resources ──────────────────────────────────────────

        private TerrainTileAsset?        activeTile;
        private TerrainTileGpuResources? activeGpu;
        private RenderTexture?           heightRT;
        private RenderTexture?           splatRT;
        private ComputeShader?           brushCompute;

        private readonly TerrainSculptUndo       undo      = new TerrainSculptUndo();
        private readonly TerrainSculptRtWriteback writeback = new TerrainSculptRtWriteback();

        // ── Stroke state ──────────────────────────────────────────────────────

        private bool   inStroke;
        private double lastWritebackTime;

        // ── Toolbar icon ──────────────────────────────────────────────────────

        public override GUIContent toolbarIcon =>
            EditorGUIUtility.IconContent("TerrainInspector.TerrainToolRaise");

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public override void OnActivated()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/GpuTerrain/Shaders/TerrainBrush.compute");
            EditorApplication.update += this.OnEditorUpdate;
            this.BindTile(TerrainSculptState.ActiveTile);
        }

        public override void OnWillBeDeactivated()
        {
            EditorApplication.update -= this.OnEditorUpdate;
            this.ReleaseTileResources();
        }

        private void OnEditorUpdate() => this.writeback.Tick();

        // ── Main tool GUI ─────────────────────────────────────────────────────

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView) return;

            // Re-bind if the tile changed in the inspector.
            if (this.activeTile != TerrainSculptState.ActiveTile)
                this.BindTile(TerrainSculptState.ActiveTile);

            if (this.activeTile == null || this.brushCompute == null ||
                this.heightRT == null || this.splatRT == null) return;

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
                    this.BeginStroke();
                    this.Dispatch(worldPoint);
                    this.ThrottledWriteback(force: true);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.inStroke:
                    if (hasHit)
                    {
                        this.Dispatch(worldPoint);
                        this.ThrottledWriteback(force: false);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.inStroke:
                    this.EndStroke();
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            this.DrawHud();
        }

        // ── World point resolution ─────────────────────────────────────────────

        /// <summary>
        /// Tries to resolve the brush world position from the ray.
        /// 1. Physics.Raycast (catches any terrain collider in the scene).
        /// 2. Intersect the ray with the tile's mid-height XZ plane (fallback).
        /// </summary>
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

        // ── Stroke helpers ─────────────────────────────────────────────────────

        private void BeginStroke()
        {
            if (this.activeTile != null)
                this.undo.Push(this.activeTile);
            this.inStroke          = true;
            this.lastWritebackTime = 0;
        }

        private void EndStroke()
        {
            this.inStroke = false;
            // Final writeback, unconditional.
            if (this.activeTile != null && this.activeGpu != null &&
                this.heightRT != null && this.splatRT != null)
            {
                this.writeback.RequestAsync(
                    this.activeTile, this.activeGpu, this.heightRT, this.splatRT);
            }
        }

        private void ThrottledWriteback(bool force)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && now - this.lastWritebackTime < READBACK_INTERVAL) return;
            this.lastWritebackTime = now;

            if (this.activeTile != null && this.activeGpu != null &&
                this.heightRT != null && this.splatRT != null)
            {
                this.writeback.RequestAsync(
                    this.activeTile, this.activeGpu, this.heightRT, this.splatRT);
            }
        }

        // ── Compute kernel dispatch ────────────────────────────────────────────

        private void Dispatch(Vector3 worldPoint)
        {
            if (this.activeTile == null || this.brushCompute == null ||
                this.heightRT == null || this.splatRT == null) return;

            var worldXZ = new Vector2(worldPoint.x, worldPoint.z);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, TerrainSculptState.BrushSize, this.activeTile.tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes  = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            this.brushCompute.SetVector("_BrushCenterUV", centerUV);
            this.brushCompute.SetFloat("_BrushRadiusUV",  radiusUV);
            this.brushCompute.SetFloat("_Strength",        TerrainSculptState.BrushStrength);
            this.brushCompute.SetInt("_RTRes",             rtRes);

            SculptMode mode = TerrainSculptState.EffectiveMode;
            switch (mode)
            {
                case SculptMode.Raise:
                    DispatchKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER, "_HeightRT",
                        this.heightRT, "_RaiseSign", 1f, groups);
                    break;
                case SculptMode.Lower:
                    DispatchKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER, "_HeightRT",
                        this.heightRT, "_RaiseSign", -1f, groups);
                    break;
                case SculptMode.Smooth:
                {
                    int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_SMOOTH);
                    this.brushCompute.SetTexture(k, "_HeightRT", this.heightRT);
                    this.brushCompute.Dispatch(k, groups, groups, 1);
                    break;
                }
                case SculptMode.Flatten:
                {
                    int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_FLATTEN);
                    this.brushCompute.SetTexture(k, "_HeightRT", this.heightRT);
                    this.brushCompute.SetFloat("_FlattenTarget", TerrainSculptState.FlattenTarget);
                    this.brushCompute.Dispatch(k, groups, groups, 1);
                    break;
                }
                case SculptMode.Paint:
                {
                    int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
                    this.brushCompute.SetTexture(k, "_SplatRT", this.splatRT);
                    this.brushCompute.SetInt("_SplatLayer", TerrainSculptState.SplatLayer);
                    this.brushCompute.Dispatch(k, groups, groups, 1);
                    break;
                }
            }
        }

        private void DispatchKernel(string kernelName, string rtProp, RenderTexture rt,
                                    string floatProp, float floatVal, int groups)
        {
            int k = this.brushCompute!.FindKernel(kernelName);
            this.brushCompute.SetTexture(k, rtProp, rt);
            this.brushCompute.SetFloat(floatProp, floatVal);
            this.brushCompute.Dispatch(k, groups, groups, 1);
        }

        // ── Tile bind / release ────────────────────────────────────────────────

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

        // ── In-scene HUD ───────────────────────────────────────────────────────

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
