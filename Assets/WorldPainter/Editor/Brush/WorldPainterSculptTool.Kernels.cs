#nullable enable
using WorldPainter;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Layer-routing half of <see cref="WorldPainterSculptTool"/> (partial).
    ///
    /// <see cref="BindAndDispatch"/> sets the common brush uniforms, then either routes a Biome
    /// layer to the composite <see cref="WorldPainterBiomeStamp"/> (unchanged), or resolves the
    /// active <see cref="IBrushTool"/> for the effective layer kind and invokes it. The per-kernel
    /// dispatch logic lives in the tool implementations (Editor/Brush/Tools/), not here — adding a
    /// new brush behaviour no longer means editing this dispatcher.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // ── Layer routing → active brush tool ─────────────────────────────────

        private void BindAndDispatch(
            Vector3 worldPos,
            TerrainTileAsset tile,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            if (this.brushCompute == null) return;

            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            var brush   = WorldPainterState.Brush;
            var worldXZ = new Vector2(worldPos.x, worldPos.z);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, brush.size, tile.tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes  = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            // Common brush uniforms — shared by every kernel; tools set only their own params.
            this.brushCompute.SetVector("_BrushCenterUV", centerUV);
            this.brushCompute.SetFloat("_BrushRadiusUV",  radiusUV);
            this.brushCompute.SetFloat("_Strength",        brush.strength);
            this.brushCompute.SetInt("_RTRes",             rtRes);
            this.brushCompute.SetInt("_BrushShape",        (int)brush.shape);
            // Reset splat mode to Paint each dispatch. SplatEraseTool re-sets it to 1 just before
            // its own dispatch; this default keeps the biome composite splat path (which never
            // sets _SplatMode) from inheriting a stale Erase from a prior splat-erase stroke.
            this.brushCompute.SetInt("_SplatMode", 0);

            LayerType activeType = WorldPainterState.EffectiveLayerType(painter);

            // Biome composite stamp keeps its own multi-channel path (not a brush tool).
            if (activeType == LayerType.Biome && this.biomeStamp != null)
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
                return;
            }

            // Generic brush-tool dispatch (height / splat / density / instance).
            var tool = BrushToolRegistry.ResolveActiveTool(
                activeType, WorldPainterState.ActiveBrushToolId);
            if (tool == null) return;

            var ctx = new BrushToolContext(
                this, painter, worldPos, tile, heightRT, splatRT, this.brushCompute, groups);
            tool.Apply(in ctx);
        }
    }
}
