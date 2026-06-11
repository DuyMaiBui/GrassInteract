#nullable enable
using UnityEditor;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>Sculpt/paint modes available in the editor tool.</summary>
    public enum SculptMode
    {
        Raise,
        Lower,
        Smooth,
        Flatten,
        Paint,
    }

    /// <summary>
    /// Encapsulates the compute-kernel dispatch and throttled live-writeback for a
    /// single brush stroke. Used by <see cref="TerrainSculptTool"/>.
    ///
    /// Stroke flow:
    ///   BeginStroke  → take undo snapshot, reset writeback timer
    ///   Dispatch     → select kernel, set parameters, dispatch compute
    ///   ThrottledWriteback → live RT→asset upload, throttled to READBACK_INTERVAL
    ///   EndStroke    → final unconditional writeback
    /// </summary>
    internal sealed class TerrainBrushStroke
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const double READBACK_INTERVAL = 0.15;

        // ── Dependencies ──────────────────────────────────────────────────────

        private readonly TerrainSculptUndo       undo;
        private readonly TerrainSculptRtWriteback writeback;

        // ── Stroke state ──────────────────────────────────────────────────────

        public  bool   InStroke         { get; private set; }
        private double lastWritebackTime;

        // ── Constructor ───────────────────────────────────────────────────────

        internal TerrainBrushStroke(
            TerrainSculptUndo undo,
            TerrainSculptRtWriteback writeback)
        {
            this.undo      = undo;
            this.writeback = writeback;
        }

        // ── Stroke lifecycle ──────────────────────────────────────────────────

        internal void BeginStroke(TerrainTileAsset tile)
        {
            this.undo.Push(tile);
            this.InStroke          = true;
            this.lastWritebackTime = 0;
        }

        /// <summary>
        /// Begin a stroke without pushing an undo snapshot (caller already pushed per-tile).
        /// Used by P2 multi-tile path where undo snapshots are pushed before dispatch.
        /// </summary>
        internal void BeginStroke(System.Collections.Generic.IReadOnlyList<Vector2Int> _)
        {
            this.InStroke          = true;
            this.lastWritebackTime = 0;
        }

        /// <summary>
        /// Mark stroke as ended without triggering writeback (P2 multi-tile path calls
        /// writeback per-tile before calling this).
        /// </summary>
        internal void EndInStroke() => this.InStroke = false;

        internal void EndStroke(
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            this.InStroke = false;
            this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
        }

        internal void ThrottledWriteback(
            bool force,
            TerrainTileAsset tile,
            TerrainTileGpuResources gpu,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!force && now - this.lastWritebackTime < READBACK_INTERVAL) return;
            this.lastWritebackTime = now;
            this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
        }

        // ── Compute kernel dispatch ────────────────────────────────────────────

        /// <summary>
        /// Dispatches the appropriate compute kernel for the current
        /// <see cref="TerrainSculptState.EffectiveMode"/> using brush state from
        /// <see cref="TerrainSculptState"/>.
        /// </summary>
        internal void Dispatch(
            Vector3 worldPoint,
            TerrainTileAsset tile,
            ComputeShader brushCompute,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            var worldXZ = new Vector2(worldPoint.x, worldPoint.z);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, TerrainSculptState.BrushSize, tile.tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes  = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            brushCompute.SetVector("_BrushCenterUV", centerUV);
            brushCompute.SetFloat("_BrushRadiusUV",  radiusUV);
            brushCompute.SetFloat("_Strength",        TerrainSculptState.BrushStrength);
            brushCompute.SetInt("_RTRes",             rtRes);

            switch (TerrainSculptState.EffectiveMode)
            {
                case SculptMode.Raise:
                    DispatchWithFloat(brushCompute, TerrainSculptConfig.KERNEL_RAISE_LOWER,
                        "_HeightRT", heightRT, "_RaiseSign", 1f, groups);
                    break;
                case SculptMode.Lower:
                    DispatchWithFloat(brushCompute, TerrainSculptConfig.KERNEL_RAISE_LOWER,
                        "_HeightRT", heightRT, "_RaiseSign", -1f, groups);
                    break;
                case SculptMode.Smooth:
                {
                    int k = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_SMOOTH);
                    brushCompute.SetTexture(k, "_HeightRT", heightRT);
                    brushCompute.Dispatch(k, groups, groups, 1);
                    break;
                }
                case SculptMode.Flatten:
                {
                    int k = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_FLATTEN);
                    brushCompute.SetTexture(k, "_HeightRT", heightRT);
                    brushCompute.SetFloat("_FlattenTarget", TerrainSculptState.FlattenTarget);
                    brushCompute.Dispatch(k, groups, groups, 1);
                    break;
                }
                case SculptMode.Paint:
                {
                    int k = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
                    brushCompute.SetTexture(k, "_SplatRT", splatRT);
                    brushCompute.SetInt("_SplatLayer", TerrainSculptState.SplatLayer);
                    brushCompute.Dispatch(k, groups, groups, 1);
                    break;
                }
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static void DispatchWithFloat(
            ComputeShader cs, string kernelName,
            string rtProp, RenderTexture rt,
            string floatProp, float floatVal, int groups)
        {
            int k = cs.FindKernel(kernelName);
            cs.SetTexture(k, rtProp, rt);
            cs.SetFloat(floatProp, floatVal);
            cs.Dispatch(k, groups, groups, 1);
        }
    }
}
