#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Translates Scene-view mouse events into compute-brush dispatches on the
    /// per-tile working RenderTextures.
    ///
    /// Stroke flow:
    ///   MouseDown  → take undo snapshot, begin stroke
    ///   MouseDrag  → dispatch compute kernel (throttled by STROKE_STEP_M)
    ///   MouseUp    → request async RT→R16 writeback, end stroke
    ///
    /// Only the GPU RT is modified during the stroke; writeback to the tile asset
    /// happens once per stroke (mouse-up) via <see cref="TerrainSculptRtWriteback"/>.
    /// </summary>
    public sealed class TerrainBrushController : IDisposable
    {
        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly TerrainSculptUndo undo;
        private readonly TerrainSculptRtWriteback writeback;
        private readonly ComputeShader brushCompute;

        // ── Active stroke state ───────────────────────────────────────────────

        private bool inStroke;
        private Vector2 lastSampleWorldXZ;

        // ── Brush parameter cache (set by window each frame) ──────────────────

        public float BrushSizeM    { get; set; } = TerrainSculptConfig.DEFAULT_BRUSH_SIZE_M;
        public float BrushStrength { get; set; } = TerrainSculptConfig.DEFAULT_BRUSH_STRENGTH;
        public SculptMode Mode     { get; set; } = SculptMode.Raise;
        public int SplatLayer      { get; set; } = 0;
        public float FlattenTarget { get; set; } = 0.5f; // normalized [0,1]

        // ── Kernel handles ────────────────────────────────────────────────────

        private int kernelRaiseLower;
        private int kernelSmooth;
        private int kernelFlatten;
        private int kernelPaintSplat;

        // ── Constructor ───────────────────────────────────────────────────────

        public TerrainBrushController(
            TerrainSculptUndo undo,
            TerrainSculptRtWriteback writeback,
            ComputeShader brushCompute)
        {
            this.undo         = undo         ?? throw new ArgumentNullException(nameof(undo));
            this.writeback    = writeback    ?? throw new ArgumentNullException(nameof(writeback));
            this.brushCompute = brushCompute ?? throw new ArgumentNullException(nameof(brushCompute));

            this.kernelRaiseLower = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER);
            this.kernelSmooth     = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_SMOOTH);
            this.kernelFlatten    = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_FLATTEN);
            this.kernelPaintSplat = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
        }

        // ── Scene-view event handling ──────────────────────────────────────────

        /// <summary>
        /// Process a SceneView event. Call from TerrainSculptWindow.OnSceneGUI().
        /// Returns true if the event was consumed (caller should call Use()).
        /// </summary>
        public bool HandleSceneEvent(
            Event evt,
            SceneView sceneView,
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT,
            RenderTexture splatRT,
            Vector2Int tileCoord)
        {
            if (evt == null || tile == null) return false;

            // Ray cast to get world XZ (assume Y=0 terrain plane for simplicity).
            Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            float t = ray.origin.y / -ray.direction.y;
            if (t < 0f) return false;
            Vector3 worldPos = ray.origin + ray.direction * t;
            Vector2 worldXZ  = new Vector2(worldPos.x, worldPos.z);

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                this.BeginStroke(tile, worldXZ, tileCoord, heightRT, splatRT);
                this.DispatchKernel(tile, worldXZ, tileCoord, heightRT, splatRT);
                return true;
            }

            if (evt.type == EventType.MouseDrag && evt.button == 0 && this.inStroke)
            {
                float dist = Vector2.Distance(worldXZ, this.lastSampleWorldXZ);
                if (dist >= TerrainSculptConfig.STROKE_STEP_M)
                {
                    this.DispatchKernel(tile, worldXZ, tileCoord, heightRT, splatRT);
                    this.lastSampleWorldXZ = worldXZ;
                }
                return true;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && this.inStroke)
            {
                this.EndStroke(tile, gpu, heightRT, splatRT);
                return true;
            }

            return false;
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            this.inStroke = false;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void BeginStroke(
            TerrainTileAsset tile,
            Vector2 worldXZ,
            Vector2Int tileCoord,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            this.undo.Push(tile);
            this.inStroke = true;
            this.lastSampleWorldXZ = worldXZ;
        }

        private void EndStroke(
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            this.inStroke = false;
            // Async writeback once per stroke end — no per-sample stalls.
            this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
        }

        private void DispatchKernel(
            TerrainTileAsset tile,
            Vector2 worldXZ,
            Vector2Int tileCoord,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, this.BrushSizeM, tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            this.brushCompute.SetVector("_BrushCenterUV", centerUV);
            this.brushCompute.SetFloat("_BrushRadiusUV", radiusUV);
            this.brushCompute.SetFloat("_Strength", this.BrushStrength);
            this.brushCompute.SetInt("_RTRes", rtRes);

            switch (this.Mode)
            {
                case SculptMode.Raise:
                    this.brushCompute.SetTexture(this.kernelRaiseLower, "_HeightRT", heightRT);
                    this.brushCompute.SetFloat("_RaiseSign", 1f);
                    this.brushCompute.Dispatch(this.kernelRaiseLower, groups, groups, 1);
                    break;
                case SculptMode.Lower:
                    this.brushCompute.SetTexture(this.kernelRaiseLower, "_HeightRT", heightRT);
                    this.brushCompute.SetFloat("_RaiseSign", -1f);
                    this.brushCompute.Dispatch(this.kernelRaiseLower, groups, groups, 1);
                    break;
                case SculptMode.Smooth:
                    this.brushCompute.SetTexture(this.kernelSmooth, "_HeightRT", heightRT);
                    this.brushCompute.Dispatch(this.kernelSmooth, groups, groups, 1);
                    break;
                case SculptMode.Flatten:
                    this.brushCompute.SetTexture(this.kernelFlatten, "_HeightRT", heightRT);
                    this.brushCompute.SetFloat("_FlattenTarget", this.FlattenTarget);
                    this.brushCompute.Dispatch(this.kernelFlatten, groups, groups, 1);
                    break;
                case SculptMode.Paint:
                    this.brushCompute.SetTexture(this.kernelPaintSplat, "_SplatRT", splatRT);
                    this.brushCompute.SetInt("_SplatLayer", this.SplatLayer);
                    this.brushCompute.Dispatch(this.kernelPaintSplat, groups, groups, 1);
                    break;
            }
        }
    }

    /// <summary>Sculpt/paint modes available in the editor window.</summary>
    public enum SculptMode
    {
        Raise,
        Lower,
        Smooth,
        Flatten,
        Paint,
    }
}
