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

            // Channel comes from the active layer's stack position (Splat rows map to channels 0..3).
            WorldPainterState.ActiveLayerType(ctx.Painter, out int channel);
            if (channel < 0) channel = 0;
            ctx.Compute.SetInt("_SplatLayer", Mathf.Clamp(channel, 0, TerrainSculptConfig.MAX_SPLAT_LAYERS - 1));
            ctx.Compute.SetInt("_SplatMode", mode);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }
    }
}
