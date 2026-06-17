#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Density (meadow) scatter brush tools: Paint (add), Erase (subtract), Smooth (blur). All
    /// share the PaintDensity kernel; <c>_DensityMode</c> (0/1/2) selects the operation. The
    /// per-layer density RenderTexture is owned by the sculpt tool and persisted via the encoder.
    /// </summary>
    internal sealed class DensityPaintTool : IBrushTool
    {
        public string Id => "density.paint";
        public string Label => "Paint";
        public LayerType LayerType => LayerType.Grass;
        public void Apply(in BrushToolContext ctx) => DensityDispatch.Run(in ctx, mode: 0);
    }

    internal sealed class DensityEraseTool : IBrushTool
    {
        public string Id => "density.erase";
        public string Label => "Erase";
        public LayerType LayerType => LayerType.Grass;
        public void Apply(in BrushToolContext ctx) => DensityDispatch.Run(in ctx, mode: 1);
    }

    internal sealed class DensitySmoothTool : IBrushTool
    {
        public string Id => "density.smooth";
        public string Label => "Smooth";
        public LayerType LayerType => LayerType.Grass;
        public void Apply(in BrushToolContext ctx) => DensityDispatch.Run(in ctx, mode: 2);
    }

    /// <summary>Shared density dispatch. mode 0 = paint, 1 = erase, 2 = smooth.</summary>
    internal static class DensityDispatch
    {
        public static void Run(in BrushToolContext ctx, int mode)
        {
            // Per-tile path: when a GrassLayer variant is active, resolve the density target
            // from the tile currently under the brush (ctx.Tile.tileCoord).
            if (WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Meadow)
            {
                var tileDensityTex = BrushToolTargets.ResolveGrassDensityForTile(
                    ctx.Painter, ctx.Tile.tileCoord);
                if (tileDensityTex == null)
                {
                    // Auto-create this tile's density texture so grass EXPANDS onto any tile the
                    // artist paints — not just tiles whose density entry was eagerly populated at
                    // layer/tile creation time. Without this, painting a tile that lacks an entry
                    // no-ops and grass only ever appears on the originally-seeded tile(s).
                    var grass = BrushToolTargets.ResolveActiveGrassLayer(ctx.Painter);
                    var map   = ctx.Painter.Map;
                    if (grass == null || map == null) return;
                    tileDensityTex = WorldMapAssetLifecycle.EnsureGrassDensityForTile(
                        map, grass, ctx.Tile.tileCoord);
                    if (tileDensityTex == null) return; // map not a saved asset — skip
                }

                var dRT = ctx.Tool.GetOrCreateDensityRT(tileDensityTex, ctx.Tile.tileCoord);
                if (dRT == null) return;

                int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_DENSITY);
                ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
                BrushMaskBinder.BindToCompute(ctx.Compute, k);
                ctx.Compute.SetTexture(k, "_DensityRT", dRT);
                ctx.Compute.SetInt("_DensityMode", mode);

                // Seam-aware Smooth (mode 2): bind any straddled neighbour tile's working density
                // RT so the box blur reads across the shared edge. Paint/Erase bind placeholders
                // (flag 0) — the kernel references the neighbour textures unconditionally, so every
                // PaintDensity dispatch must keep all four slots bound.
                BindDensityNeighbors(in ctx, k, ctx.Tile.tileCoord, dRT, smooth: mode == 2);

                ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);

                // Queue throttled async writeback to this tile's density texture.
                ctx.Tool.densityEncoder.RequestAsync(tileDensityTex, dRT);
                return;
            }

        }

        /// <summary>
        /// Binds the four cardinal neighbour density RTs for the Smooth kernel. When
        /// <paramref name="smooth"/> and a neighbour tile already has a density texture, its working
        /// RT (created on demand, seeded from the committed map) is bound with flag 1; otherwise the
        /// self RT is bound as an inert placeholder with flag 0 (never sampled). A neighbour without
        /// existing density is never created (read-only resolve), so seam continuity applies only
        /// where the neighbour already has grass. A neighbour bound here as a pure read source is
        /// NOT painted, so it never enters strokeTouchedCoords and FlushAllDensityRTs skips it — its
        /// committed texture is left byte-for-byte untouched (no lossy round-trip, no spurious undo).
        /// </summary>
        private static void BindDensityNeighbors(
            in BrushToolContext ctx, int kernel, Vector2Int coord, RenderTexture self, bool smooth)
        {
            BindDensityDir(in ctx, kernel, "_NeighborDensityPosX", "_HasNeighborDensityPosX",
                new Vector2Int(coord.x + 1, coord.y), self, smooth);
            BindDensityDir(in ctx, kernel, "_NeighborDensityNegX", "_HasNeighborDensityNegX",
                new Vector2Int(coord.x - 1, coord.y), self, smooth);
            BindDensityDir(in ctx, kernel, "_NeighborDensityPosZ", "_HasNeighborDensityPosZ",
                new Vector2Int(coord.x, coord.y + 1), self, smooth);
            BindDensityDir(in ctx, kernel, "_NeighborDensityNegZ", "_HasNeighborDensityNegZ",
                new Vector2Int(coord.x, coord.y - 1), self, smooth);
        }

        private static void BindDensityDir(
            in BrushToolContext ctx, int kernel, string texProp, string flagProp,
            Vector2Int neighborCoord, RenderTexture self, bool smooth)
        {
            RenderTexture? rt = null;
            if (smooth)
            {
                var tex = BrushToolTargets.ResolveGrassDensityForTile(ctx.Painter, neighborCoord);
                if (tex != null)
                    rt = ctx.Tool.GetOrCreateDensityRT(tex, neighborCoord);
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
