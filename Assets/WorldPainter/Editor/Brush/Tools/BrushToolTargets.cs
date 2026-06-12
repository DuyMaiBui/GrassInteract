#nullable enable
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Resolves the scatter-layer target for the active selection, supporting BOTH selection
    /// paths: the P5 SSOT (<c>ActiveLayerId</c>, set by the splat/scatter palette) and the
    /// legacy index path (<c>ActiveScatterIndex</c>, set by the layer stack). Prefers the id
    /// match, then falls back to the index — mirroring the original BindAndDispatch lookups.
    /// </summary>
    internal static class BrushToolTargets
    {
        /// <summary>The active density (meadow) scatter layer, or null when none/other.</summary>
        public static DensityScatterLayer? ResolveDensityLayer(WorldPainter painter)
        {
            string id = WorldPainterState.ActiveLayerId;
            foreach (var layer in painter.ScatterLayers)
                if (layer is DensityScatterLayer dl && dl.name == id)
                    return dl;

            int idx = WorldPainterState.ActiveScatterIndex(painter);
            if (idx >= 0 && idx < painter.ScatterLayers.Count)
                return painter.ScatterLayers[idx] as DensityScatterLayer;

            return null;
        }

        /// <summary>The active instance (prop) scatter layer, or null when none/other.</summary>
        public static InstanceScatterLayer? ResolveInstanceLayer(WorldPainter painter)
        {
            string id = WorldPainterState.ActiveLayerId;
            foreach (var layer in painter.ScatterLayers)
                if (layer is InstanceScatterLayer il && il.name == id)
                    return il;

            int idx = WorldPainterState.ActiveScatterIndex(painter);
            if (idx >= 0 && idx < painter.ScatterLayers.Count)
                return painter.ScatterLayers[idx] as InstanceScatterLayer;

            return null;
        }
    }
}
