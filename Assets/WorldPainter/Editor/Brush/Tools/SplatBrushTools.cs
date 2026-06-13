#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Splat-layer brush tools: Paint (add weight) and Erase (subtract weight) on the active
    /// splat channel. Both share the PaintSplat kernel; <c>_SplatMode</c> selects add vs subtract.
    /// </summary>
    internal sealed class SplatPaintTool : IBrushTool
    {
        public string Id => "splat.paint";
        public string Label => "Paint";
        public LayerType LayerType => LayerType.Splat;
        public void Apply(in BrushToolContext ctx) => SplatDispatch.Run(in ctx, mode: 0);
    }

    internal sealed class SplatEraseTool : IBrushTool
    {
        public string Id => "splat.erase";
        public string Label => "Erase";
        public LayerType LayerType => LayerType.Splat;
        public void Apply(in BrushToolContext ctx) => SplatDispatch.Run(in ctx, mode: 1);
    }

    /// <summary>Shared splat dispatch. mode 0 = paint (add), 1 = erase (subtract).</summary>
    internal static class SplatDispatch
    {
        public static void Run(in BrushToolContext ctx, int mode)
        {
            int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
            ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
            ctx.Compute.SetTexture(k, "_SplatRT", ctx.SplatRT);

            int channel = ResolveChannel(ctx.Painter);
            ctx.Compute.SetInt("_SplatLayer", Mathf.Clamp(channel, 0, TerrainSculptConfig.MAX_SPLAT_LAYERS - 1));
            ctx.Compute.SetInt("_SplatMode", mode);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }

        /// <summary>
        /// Resolves the splat channel [0..3] to paint.
        ///
        /// Phase 3 unified path: when <see cref="WorldPainterState.ActiveLayerKind"/> is Splat
        /// and <see cref="WorldPainterState.ActiveSplatChannel"/> is set (≥ 0), use it directly.
        ///
        /// Legacy fallback: derive the channel from the active layer's display-index position
        /// via <see cref="WorldPainterState.ActiveLayerType"/> (legacy index path, height fallback).
        /// </summary>
        internal static int ResolveChannel(WorldPainter painter)
        {
            // Unified path: inspector-selected albedo slot → channel.
            if (WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Splat &&
                WorldPainterState.ActiveSplatChannel >= 0)
            {
                return WorldPainterState.ActiveSplatChannel;
            }

            // Legacy path: index arithmetic (still used when no unified splat layer is active).
            WorldPainterState.ActiveLayerType(painter, out int channel);
            return channel < 0 ? 0 : channel;
        }
    }
}
