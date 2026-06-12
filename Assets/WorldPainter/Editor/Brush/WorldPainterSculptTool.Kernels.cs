#nullable enable
using WorldPainter;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Kernel-selection and per-layer dispatch half of <see cref="WorldPainterSculptTool"/> (partial).
    ///
    /// Contains: <see cref="BindAndDispatch"/> (reads active layer type and routes to the
    /// correct GPU kernel or CPU emitter), plus the three kernel methods:
    /// <see cref="DispatchHeightKernel"/>, <see cref="DispatchSplatKernel"/>,
    /// <see cref="DispatchDensityKernel"/>.
    ///
    /// Stamp-path dispatch (<see cref="WorldPainterSculptTool.Stroke.cs"/>) calls
    /// <see cref="BindAndDispatch"/> after resolving which tiles to touch.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // ── Kernel selection / layer routing ──────────────────────────────────

        private void BindAndDispatch(
            Vector3 worldPos,
            TerrainTileAsset tile,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            if (this.brushCompute == null) return;

            var painter = WorldPainterState.ActivePainter;

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

            // Determine kernel by active layer type.
            // Height → RaiseLower, Splat → PaintSplat, Grass → PaintDensity.
            LayerType activeType = LayerType.Height;
            int splatChannel = -1;
            if (painter != null)
                activeType = WorldPainterState.ActiveLayerType(painter, out splatChannel);

            if (activeType == LayerType.Splat)
            {
                this.DispatchSplatKernel(groups, splatChannel, splatRT);
            }
            else if (activeType == LayerType.Grass && painter != null)
            {
                int scatterIdx = WorldPainterState.ActiveScatterIndex(painter);
                if (scatterIdx >= 0 && scatterIdx < painter.ScatterLayers.Count)
                {
                    var scatterLayer = painter.ScatterLayers[scatterIdx] as DensityScatterLayer;
                    if (scatterLayer != null)
                    {
                        var dRT = this.GetOrCreateDensityRT(scatterLayer);
                        if (dRT != null)
                            this.DispatchDensityKernel(groups, dRT);
                    }
                }
            }
            else if (activeType == LayerType.Props && painter != null)
            {
                // Props use CPU spacing-stamp emitter (no GPU kernel).
                int scatterIdx = WorldPainterState.ActiveScatterIndex(painter);
                if (scatterIdx >= 0 && scatterIdx < painter.ScatterLayers.Count)
                {
                    var propLayer = painter.ScatterLayers[scatterIdx] as InstanceScatterLayer;
                    if (propLayer != null)
                    {
                        bool isDelete = Event.current != null && Event.current.shift;
                        // Push undo before first emit on this layer this stroke.
                        var authored = propLayer.AuthoredInstances;
                        if (authored != null)
                        {
                            int key = propLayer.GetInstanceID();
                            if (!WorldPainterAuthoring.UndoStack.CanUndoRecords(key))
                                WorldPainterAuthoring.UndoStack.PushRecords(authored, key);
                        }

                        this.propEmitter.Emit(
                            propLayer,
                            worldPos,
                            brush.size * 0.5f,
                            deleteMode: isDelete,
                            surfaceSampler: null);
                    }
                }
            }
            else if (activeType == LayerType.Biome && painter != null && this.biomeStamp != null)
            {
                int biomeIdx = WorldPainterState.ActiveBiomeLayerIndex(painter);
                if (biomeIdx >= 0 && biomeIdx < painter.Biomes.Count)
                {
                    var preset = painter.Biomes[biomeIdx];
                    if (preset != null)
                    {
                        this.biomeStamp.Stamp(
                            preset,
                            this.biomeMuteMask,
                            worldPos,
                            tile,
                            heightRT,
                            splatRT,
                            this.brushCompute!,
                            brush.size,
                            brush.strength,
                            surfaceSampler: null);
                    }
                }
            }
            else
            {
                this.DispatchHeightKernel(groups, heightRT);
            }
        }

        // ── GPU kernel dispatchers ─────────────────────────────────────────────

        private void DispatchHeightKernel(int groups, RenderTexture heightRT)
        {
            if (this.brushCompute == null) return;

            int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER);
            this.falloffLut.BindToCompute(this.brushCompute, k);
            this.brushCompute.SetTexture(k, "_HeightRT", heightRT);
            this.brushCompute.SetFloat("_RaiseSign", 1f);
            this.brushCompute.Dispatch(k, groups, groups, 1);
        }

        private void DispatchSplatKernel(int groups, int splatChannel, RenderTexture splatRT)
        {
            if (this.brushCompute == null) return;

            int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
            this.falloffLut.BindToCompute(this.brushCompute, k);
            this.brushCompute.SetTexture(k, "_SplatRT", splatRT);
            this.brushCompute.SetInt("_SplatLayer", Mathf.Clamp(splatChannel, 0, TerrainSculptConfig.MAX_SPLAT_LAYERS - 1));
            this.brushCompute.Dispatch(k, groups, groups, 1);
        }

        private void DispatchDensityKernel(int groups, RenderTexture densityRT)
        {
            if (this.brushCompute == null) return;

            int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_DENSITY);
            this.falloffLut.BindToCompute(this.brushCompute, k);
            this.brushCompute.SetTexture(k, "_DensityRT", densityRT);
            // Mode 0 = Paint. Erase/smooth extend via different _DensityMode values (P4+).
            this.brushCompute.SetInt("_DensityMode", 0);
            this.brushCompute.Dispatch(k, groups, groups, 1);

            // Queue throttled async writeback to the density layer.
            if (this.activeDensityLayer != null)
                this.densityEncoder.RequestAsync(this.activeDensityLayer, densityRT);
        }
    }
}
