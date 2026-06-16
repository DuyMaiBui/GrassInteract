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

            // Seam-aware smoothing: bind any adjacent tile's working RT so the box blur reads
            // across the shared edge instead of clamping at the tile border. Without this each
            // tile smooths its edge from only its own interior, so the two tiles' shared edge
            // heights diverge → a kink/crack at the seam. Unbound directions get a placeholder +
            // flag 0 (never sampled) so every declared texture stays bound for the dispatch.
            var coord = ctx.Tile.tileCoord;
            BindNeighbor(in ctx, k, "_NeighborPosX", "_HasNeighborPosX", new Vector2Int(coord.x + 1, coord.y));
            BindNeighbor(in ctx, k, "_NeighborNegX", "_HasNeighborNegX", new Vector2Int(coord.x - 1, coord.y));
            BindNeighbor(in ctx, k, "_NeighborPosZ", "_HasNeighborPosZ", new Vector2Int(coord.x, coord.y + 1));
            BindNeighbor(in ctx, k, "_NeighborNegZ", "_HasNeighborNegZ", new Vector2Int(coord.x, coord.y - 1));

            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }

        /// <summary>
        /// Binds a neighbour tile's working height RT (same <c>BRUSH_RT_RES</c>, aligned grid) to
        /// the Smooth kernel when that tile is part of the current stroke; otherwise binds a
        /// placeholder and clears the flag so the kernel never reads it. The neighbour RT is
        /// guaranteed to exist before dispatch because <c>DoStamp</c> pre-creates every overlapped
        /// tile's RT.
        /// </summary>
        private static void BindNeighbor(
            in BrushToolContext ctx, int kernel, string texProp, string flagProp, Vector2Int neighborCoord)
        {
            if (ctx.Tool.rtCache.TryGet(neighborCoord, out var neighborRT) && neighborRT != null)
            {
                ctx.Compute.SetTexture(kernel, texProp, neighborRT);
                ctx.Compute.SetInt(flagProp, 1);
            }
            else
            {
                // Placeholder: this tile's own working RT (same format/resolution). Never sampled
                // (flag 0) — bound only to satisfy the kernel's texture-binding requirement.
                ctx.Compute.SetTexture(kernel, texProp, ctx.HeightRT);
                ctx.Compute.SetInt(flagProp, 0);
            }
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

            // Set Height target: normalize the world-Y target into THIS tile's [min,max] height
            // range so the kernel can clamp in normalized space. Raise uses it as a ceiling,
            // Lower as a floor (the kernel branches on _RaiseSign).
            float range  = ctx.Tile.maxHeight - ctx.Tile.minHeight;
            float target = range > 1e-4f
                ? Mathf.Clamp01((WorldPainterState.Brush.setHeight - ctx.Tile.minHeight) / range)
                : 0f;
            ctx.Compute.SetFloat("_SetHeightTarget", target);

            ctx.Compute.Dispatch(k, ctx.Groups, ctx.Groups, 1);
        }
    }
}
