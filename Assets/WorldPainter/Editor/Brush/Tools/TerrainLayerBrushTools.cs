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

    /// <summary>
    /// Smooth (blur) the active palette layer's coverage. Active-channel-only: softens the
    /// selected layer's edge across tile seams without disturbing the other layers' relative
    /// weights. Mirror of <see cref="DensitySmoothTool"/> on the alphamap path.
    /// </summary>
    internal sealed class TerrainLayerSmoothTool : IBrushTool
    {
        public string Id => "palette.smooth";
        public string Label => "Smooth";
        public LayerType LayerType => LayerType.Splat;
        public void Apply(in BrushToolContext ctx) => TerrainLayerDispatch.Run(in ctx, mode: 2);
    }

    /// <summary>Shared TerrainPalette dispatch. mode 0 = paint (add), 1 = erase (subtract), 2 = smooth (blur).</summary>
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

            // Seam-aware Smooth (mode 2): bind any straddled neighbour tile's working alphamap RT
            // (same alphamap index) so the active-channel blur reads across the shared edge.
            // Paint/Erase bind placeholders (flag 0); the kernel references the neighbour textures
            // unconditionally, so every PaintAlphamap dispatch must keep all four slots bound.
            BindAlphaNeighbors(in ctx, k, ctx.Tile.tileCoord, alphaIdx, alphaRT, smooth: mode == 2);

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

        /// <summary>
        /// Binds the four cardinal neighbour alphamap RTs (same alphamap index) for the Smooth
        /// kernel. When <paramref name="smooth"/> and a neighbour tile has the matching alphamap,
        /// its working RT (created on demand, seeded from the committed texture) is bound with
        /// flag 1; otherwise the self RT is bound as an inert placeholder with flag 0. A neighbour
        /// lacking the alphamap is never created (read-only resolve). A neighbour bound here as a
        /// pure read source is NOT painted, so it never enters strokeTouchedCoords and
        /// FlushAllAlphamapRTs skips it — its committed alphamap is left untouched (no RGBA32
        /// quantisation of an unpainted tile, no spurious undo entry).
        /// </summary>
        private static void BindAlphaNeighbors(
            in BrushToolContext ctx, int kernel, Vector2Int coord, int alphaIdx,
            RenderTexture self, bool smooth)
        {
            BindAlphaDir(in ctx, kernel, "_NeighborAlphaPosX", "_HasNeighborAlphaPosX",
                new Vector2Int(coord.x + 1, coord.y), alphaIdx, self, smooth);
            BindAlphaDir(in ctx, kernel, "_NeighborAlphaNegX", "_HasNeighborAlphaNegX",
                new Vector2Int(coord.x - 1, coord.y), alphaIdx, self, smooth);
            BindAlphaDir(in ctx, kernel, "_NeighborAlphaPosZ", "_HasNeighborAlphaPosZ",
                new Vector2Int(coord.x, coord.y + 1), alphaIdx, self, smooth);
            BindAlphaDir(in ctx, kernel, "_NeighborAlphaNegZ", "_HasNeighborAlphaNegZ",
                new Vector2Int(coord.x, coord.y - 1), alphaIdx, self, smooth);
        }

        private static void BindAlphaDir(
            in BrushToolContext ctx, int kernel, string texProp, string flagProp,
            Vector2Int neighborCoord, int alphaIdx, RenderTexture self, bool smooth)
        {
            RenderTexture? rt = null;
            if (smooth)
            {
                var nt = BrushToolTargets.ResolveActiveAlphamap(ctx.Painter, neighborCoord);
                // Same alphamap index guaranteed (palette index is global), but guard anyway.
                if (nt != null && nt.Value.AlphamapIndex == alphaIdx)
                    rt = ctx.Tool.GetOrCreateAlphamapRT(nt.Value.Texture, neighborCoord, alphaIdx);
            }

            if (rt != null && !ReferenceEquals(rt, self))
            {
                ctx.Compute.SetTexture(kernel, texProp, rt);
                ctx.Compute.SetInt(flagProp, 1);
            }
            else
            {
                ctx.Compute.SetTexture(kernel, texProp, self);
                ctx.Compute.SetInt(flagProp, 0);
            }
        }
    }
}
