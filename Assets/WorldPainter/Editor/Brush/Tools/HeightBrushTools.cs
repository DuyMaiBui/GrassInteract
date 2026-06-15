#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Height-layer brush tools: Raise, Lower, Smooth, Flatten. All operate on the per-tile
    /// height RenderTexture via <c>TerrainBrush.compute</c>. Raise/Lower share the RaiseLower
    /// kernel (sign flip); Smooth/Flatten use their own kernels.
    /// </summary>
    internal sealed class HeightRaiseTool : IBrushTool
    {
        public string Id => "height.raise";
        public string Label => "Raise";
        public LayerType LayerType => LayerType.Height;
        public void Apply(in BrushToolContext ctx) => HeightDispatch.RaiseLower(in ctx, sign: 1f);
    }

    internal sealed class HeightLowerTool : IBrushTool
    {
        public string Id => "height.lower";
        public string Label => "Lower";
        public LayerType LayerType => LayerType.Height;
        public void Apply(in BrushToolContext ctx) => HeightDispatch.RaiseLower(in ctx, sign: -1f);
    }

    internal sealed class HeightSmoothTool : IBrushTool
    {
        public string Id => "height.smooth";
        public string Label => "Smooth";
        public LayerType LayerType => LayerType.Height;

        public void Apply(in BrushToolContext ctx)
        {
            int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_SMOOTH);
            ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
            BrushMaskBinder.BindToCompute(ctx.Compute, k);
            ctx.Compute.SetTexture(k, "_HeightRT", ctx.HeightRT);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }
    }

    internal sealed class HeightFlattenTool : IBrushTool
    {
        public string Id => "height.flatten";
        public string Label => "Flatten";
        public LayerType LayerType => LayerType.Height;

        public void Apply(in BrushToolContext ctx)
        {
            // No target captured (cursor height sample failed at mouse-down) → skip rather than
            // flatten toward the tile floor, which would be a destructive silent surprise.
            if (!ctx.Tool.flattenTargetValid) return;

            int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_FLATTEN);
            ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
            BrushMaskBinder.BindToCompute(ctx.Compute, k);
            ctx.Compute.SetTexture(k, "_HeightRT", ctx.HeightRT);

            // _FlattenTarget is normalized [0,1] in this tile's height range. The world-Y target
            // is captured once per stroke (cursor height at mouse-down) by the sculpt tool.
            float range  = ctx.Tile.maxHeight - ctx.Tile.minHeight;
            float target = range > 1e-4f
                ? Mathf.Clamp01((ctx.Tool.flattenTargetWorldY - ctx.Tile.minHeight) / range)
                : 0f;
            ctx.Compute.SetFloat("_FlattenTarget", target);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }
    }

    /// <summary>Shared RaiseLower dispatch (sign distinguishes raise from lower).</summary>
    internal static class HeightDispatch
    {
        public static void RaiseLower(in BrushToolContext ctx, float sign)
        {
            int k = ctx.Compute.FindKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER);
            ctx.Tool.falloffLut.BindToCompute(ctx.Compute, k);
            BrushMaskBinder.BindToCompute(ctx.Compute, k);
            ctx.Compute.SetTexture(k, "_HeightRT", ctx.HeightRT);
            ctx.Compute.SetFloat("_RaiseSign", sign);
            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }
    }
}
