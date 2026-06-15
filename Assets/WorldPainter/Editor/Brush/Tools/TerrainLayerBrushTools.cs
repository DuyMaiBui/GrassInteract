#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Phase 2b: paint/erase tools for the new TerrainPalette path. Mirror of the legacy
    /// <c>SplatPaintTool</c>/<c>SplatEraseTool</c> shape, but the per-tile target is
    /// resolved via <see cref="BrushToolTargets.ResolveActiveAlphamap"/> against the
    /// active <c>WorldPainterState.ActivePaletteIndex</c>, then the
    /// <c>PaintAlphamap</c> compute kernel writes the chosen RGBA channel of that
    /// alphamap RT. The encoder writes the RT pixels straight back to the bound
    /// <c>Texture2D</c> — same direct-write pattern as grass density (instant feedback).
    ///
    /// LayerType is still <see cref="LayerType.Splat"/> so the BrushDock contextual
    /// palette renders these tools when a splat layer is active. Phase 3 cleanup
    /// deletes the old SplatPaintTool / SplatEraseTool entries from the registry.
    /// </summary>
    internal sealed class TerrainLayerPaintTool : IBrushTool
    {
        public string Id => "palette.paint";
        public string Label => "Paint";
        public LayerType LayerType => LayerType.Splat;
        public void Apply(in BrushToolContext ctx) => TerrainLayerDispatch.Run(in ctx, mode: 0);
    }

    internal sealed class TerrainLayerEraseTool : IBrushTool
    {
        public string Id => "palette.erase";
        public string Label => "Erase";
        public LayerType LayerType => LayerType.Splat;
        public void Apply(in BrushToolContext ctx) => TerrainLayerDispatch.Run(in ctx, mode: 1);
    }

    /// <summary>Shared TerrainPalette dispatch. mode 0 = paint (add), 1 = erase (subtract).</summary>
    internal static class TerrainLayerDispatch
    {
        public static void Run(in BrushToolContext ctx, int mode)
        {
            // Palette-index gate: no active layer → silent no-op (HUD surfaces the warning).
            if (WorldPainterState.ActivePaletteIndex < 0) return;

            var target = BrushToolTargets.ResolveActiveAlphamap(ctx.Painter, ctx.Tile.tileCoord);
            if (target == null) return;
            var (alphaTex, alphaIdx, channel) = target.Value;

            var alphaRT = ctx.Tool.GetOrCreateAlphamapRT(alphaTex, ctx.Tile.tileCoord, alphaIdx);
            if (alphaRT == null) return;

            int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_ALPHAMAP);
            ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
            BrushMaskBinder.BindToCompute(ctx.Compute, k);
            ctx.Compute.SetTexture(k, "_AlphamapRT", alphaRT);
            ctx.Compute.SetInt("_AlphamapChannel", Mathf.Clamp(channel, 0, 3));
            ctx.Compute.SetInt("_AlphamapMode",    mode);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);

            // Live preview: bind the in-flight RT directly to the terrain shader so the drag
            // shows continuous texture coverage instead of sporadic dots gated by the async
            // Texture2D readback. The teardown path (WorldPainterSculptTool.TeardownActiveStroke)
            // restores the committed Texture2D binding after the final flush.
            ctx.Painter.BeginAlphamapPreview(ctx.Tile.tileCoord, alphaIdx, alphaRT);

            // Direct encoder — throttled async readback also persists pixels back to the bound
            // Texture2D so the saved alphamap stays current for selection/save/undo.
            ctx.Tool.alphamapEncoder.RequestAsync(alphaTex, alphaRT);
        }
    }
}
