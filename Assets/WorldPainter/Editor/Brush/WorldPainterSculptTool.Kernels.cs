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
            this.brushCompute.SetInt("_BrushShape",        (int)brush.shape);

            // Determine kernel by active layer (P5 SSOT: ActiveLayerKind / ActiveLayerId).
            // Meadow → PaintDensity on the active density scatter layer.
            // Splat  → PaintSplat on the active splat channel.
            // Prop   → CPU prop stamp emitter (no GPU kernel).
            // Height (default) → RaiseLower.
            var layerKind = WorldPainterState.ActiveLayerKind;
            var layerId   = WorldPainterState.ActiveLayerId;

            // Legacy fallback: also check the older LayerType API so existing strokes
            // that were started before the P5 API continue to work correctly.
            LayerType activeType = LayerType.Height;
            int splatChannel = -1;
            if (painter != null)
                activeType = WorldPainterState.ActiveLayerType(painter, out splatChannel);

            if (layerKind == WorldPainterState.PaintLayerKind.Splat || activeType == LayerType.Splat)
            {
                // Prefer P5 channel resolution; fall back to legacy splatChannel.
                int channel = splatChannel;
                this.DispatchSplatKernel(groups, channel, splatRT);
            }
            else if ((layerKind == WorldPainterState.PaintLayerKind.Meadow) && painter != null)
            {
                // P5 path: find the DensityScatterLayer by ActiveLayerId.
                DensityScatterLayer? scatterLayer = null;
                foreach (var layer in painter.ScatterLayers)
                {
                    if (layer is DensityScatterLayer dl && dl.name == layerId)
                    {
                        scatterLayer = dl;
                        break;
                    }
                }
                if (scatterLayer == null)
                {
                    // Fallback: use ActiveScatterIndex (legacy).
                    int scatterIdx = WorldPainterState.ActiveScatterIndex(painter);
                    if (scatterIdx >= 0 && scatterIdx < painter.ScatterLayers.Count)
                        scatterLayer = painter.ScatterLayers[scatterIdx] as DensityScatterLayer;
                }
                if (scatterLayer != null)
                {
                    var dRT = this.GetOrCreateDensityRT(scatterLayer);
                    if (dRT != null)
                        this.DispatchDensityKernel(groups, dRT);
                }
            }
            else if (activeType == LayerType.Grass && painter != null
                     && layerKind == WorldPainterState.PaintLayerKind.None)
            {
                // Legacy path when P5 API hasn't been set (no palette card selected yet).
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
            else if ((layerKind == WorldPainterState.PaintLayerKind.Prop
                      || activeType == LayerType.Props) && painter != null)
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
