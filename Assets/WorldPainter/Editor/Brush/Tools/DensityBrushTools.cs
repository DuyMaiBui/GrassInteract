#nullable enable
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
            // Target = the active GrassLayer variant's density map (unified SurfaceLayers) if one is
            // selected, else the legacy DensityScatterLayer's density map.
            var target = BrushToolTargets.ResolveDensityTarget(ctx.Painter);
            if (target == null) return;

            var dRT = ctx.Tool.GetOrCreateDensityRT(target);
            if (dRT == null) return;

            int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_DENSITY);
            ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
            ctx.Compute.SetTexture(k, "_DensityRT", dRT);
            ctx.Compute.SetInt("_DensityMode", mode);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);

            // Queue throttled async writeback to the density target (matches legacy dispatch).
            if (ctx.Tool.activeDensityMap != null)
                ctx.Tool.densityEncoder.RequestAsync(ctx.Tool.activeDensityMap, dRT);
        }
    }
}
