#nullable enable
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Biome composite stamp — one spacing-stamp fans out to all enabled channels
    /// (height-delta, splat-paint, grass-density, prop-emit) in a single dispatch group.
    ///
    /// Per-channel mute is driven by <see cref="WorldPainterBiomeContributionToggles"/>
    /// via the <see cref="BiomeChannelMask"/> flags enum. A muted channel is completely
    /// omitted from the dispatch — no per-payload branching, zero GPU cost.
    ///
    /// Reuses existing kernels and emitters from P1–P4:
    ///   - Height:  TerrainBrush.compute kernel "RaiseLower"
    ///   - Splat:   TerrainBrush.compute kernel "PaintSplat"
    ///   - Density: TerrainBrush.compute kernel "PaintDensity"
    ///   - Props:   <see cref="WorldPainterPropStampEmitter"/>
    ///
    /// Design §5.4 / Phase 5 task 2.
    /// </summary>
    internal sealed class WorldPainterBiomeStamp
    {
        // ── Deps (injected) ───────────────────────────────────────────────────

        private readonly WorldPainterPropStampEmitter propEmitter;
        private readonly WorldPainterDensityEncoder   densityEncoder;
        private readonly BrushFalloffLut              falloffLut;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterBiomeStamp(
            WorldPainterPropStampEmitter propEmitter,
            WorldPainterDensityEncoder   densityEncoder,
            BrushFalloffLut              falloffLut)
        {
            this.propEmitter    = propEmitter;
            this.densityEncoder = densityEncoder;
            this.falloffLut     = falloffLut;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatch one biome stamp at <paramref name="worldPos"/> for a single tile.
        ///
        /// Each enabled (and un-muted) channel is dispatched using the pre-existing
        /// compute kernels and prop emitter.  Returns the set of RTs that were written
        /// (caller handles writeback throttling).
        /// </summary>
        public void Stamp(
            BiomePreset          preset,
            BiomeChannelMask     mute,
            Vector3              worldPos,
            TerrainTileAsset     tile,
            RenderTexture        heightRT,
            RenderTexture        splatRT,
            ComputeShader        brushCompute,
            float                brushSize,
            float                brushStrength,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            if (preset == null) return;

            // Compute shared UV params once.
            var worldXZ = new Vector2(worldPos.x, worldPos.z);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, brushSize, tile.tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes  = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = UnityEngine.Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            brushCompute.SetVector("_BrushCenterUV", centerUV);
            brushCompute.SetFloat("_BrushRadiusUV",  radiusUV);
            brushCompute.SetInt("_RTRes",             rtRes);

            // ── Channel 1: Height ─────────────────────────────────────────────
            if (preset.HeightEnabled && !mute.HasFlag(BiomeChannelMask.Height))
            {
                brushCompute.SetFloat("_Strength", brushStrength * preset.HeightStrength);
                int k = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER);
                this.falloffLut.BindToCompute(brushCompute, k);
                brushCompute.SetTexture(k, "_HeightRT", heightRT);
                brushCompute.SetFloat("_RaiseSign", Mathf.Sign(preset.HeightDeltaM));
                brushCompute.Dispatch(k, groups, groups, 1);
            }

            // ── Channel 2: Splat ──────────────────────────────────────────────
            if (preset.SplatEnabled && !mute.HasFlag(BiomeChannelMask.Splat))
            {
                brushCompute.SetFloat("_Strength", brushStrength * preset.SplatWeight);
                int k = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
                this.falloffLut.BindToCompute(brushCompute, k);
                brushCompute.SetTexture(k, "_SplatRT", splatRT);
                brushCompute.SetInt("_SplatLayer",
                    Mathf.Clamp(preset.SplatLayerIndex, 0, TerrainSculptConfig.MAX_SPLAT_LAYERS - 1));
                brushCompute.Dispatch(k, groups, groups, 1);
            }

            // ── Channel 3: Grass density ──────────────────────────────────────
            if (preset.GrassEnabled && preset.GrassLayer != null
                && !mute.HasFlag(BiomeChannelMask.Grass))
            {
                var dRT = this.GetOrCreateDensityRT(preset.GrassLayer);
                if (dRT != null)
                {
                    brushCompute.SetFloat("_Strength", brushStrength * preset.GrassDensity);
                    int k = brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_DENSITY);
                    this.falloffLut.BindToCompute(brushCompute, k);
                    brushCompute.SetTexture(k, "_DensityRT", dRT);
                    brushCompute.SetInt("_DensityMode", 0);
                    brushCompute.Dispatch(k, groups, groups, 1);
                    this.densityEncoder.RequestAsync(preset.GrassLayer, dRT);
                }
            }

            // ── Channel 4: Props ──────────────────────────────────────────────
            if (preset.PropEnabled && preset.PropLayer != null
                && !mute.HasFlag(BiomeChannelMask.Props))
            {
                var authored = preset.PropLayer.AuthoredInstances;
                if (authored != null)
                {
                    int key = preset.PropLayer.GetInstanceID();
                    if (!WorldPainterAuthoring.UndoStack.CanUndoRecords(key))
                        WorldPainterAuthoring.UndoStack.PushRecords(authored, key);

                    this.propEmitter.Emit(
                        preset.PropLayer,
                        worldPos,
                        brushRadius: brushSize * 0.5f,
                        deleteMode:  false,
                        surfaceSampler: surfaceSampler);
                }
            }
        }

        // ── Density RT cache (one per grass layer, kept for stroke duration) ──

        private System.Collections.Generic.Dictionary<int, RenderTexture>? densityRTs;

        private RenderTexture? GetOrCreateDensityRT(DensityScatterLayer layer)
        {
            this.densityRTs ??= new();
            int id = layer.GetInstanceID();
            if (this.densityRTs.TryGetValue(id, out var rt) && rt != null)
                return rt;

            var map = layer.DensityMap;
            if (map == null) return null;

            int res = Mathf.Max(map.width, map.height);
            res = Mathf.Max(res, TerrainSculptConfig.BRUSH_RT_RES);
            rt = new RenderTexture(res, res, 0, RenderTextureFormat.RFloat)
            {
                enableRandomWrite = true,
                name = $"BiomeDensityRT_{layer.name}",
            };
            rt.Create();
            this.densityRTs[id] = rt;
            return rt;
        }

        /// <summary>Release all density RTs (call at end of each biome stroke).</summary>
        public void ReleaseDensityRTs()
        {
            if (this.densityRTs == null) return;
            foreach (var kv in this.densityRTs)
                kv.Value?.Release();
            this.densityRTs.Clear();
        }
    }
}
