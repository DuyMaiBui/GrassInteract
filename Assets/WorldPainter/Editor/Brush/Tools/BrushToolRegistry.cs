#nullable enable
using System.Collections.Generic;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Immutable catalogue of brush tools keyed by <see cref="LayerType"/>, plus active-tool
    /// resolution. The contextual tool palette (<c>WorldPainterBrushDock</c>) renders
    /// <see cref="ToolsFor"/>; <c>BindAndDispatch</c> invokes <see cref="ResolveActiveTool"/>.
    ///
    /// The stored selection (<c>WorldPainterState.ActiveBrushToolId</c>) is mapped onto the
    /// current layer's tool set by <see cref="ResolveActiveTool"/> — when the stored id does
    /// not belong to the active kind, the kind's first tool is used as the default. This means
    /// switching layers needs no reset and "remembers" a tool until the user picks another.
    /// </summary>
    internal static class BrushToolRegistry
    {
        private static readonly IBrushTool[] HeightTools =
        {
            new HeightRaiseTool(),
            new HeightLowerTool(),
            new HeightSmoothTool(),
            new HeightFlattenTool(),
        };

        // Phase 2b: TerrainPalette path (per-tile alphamaps + Unity TerrainLayer assets).
        // The legacy SplatPaintTool/SplatEraseTool entries are still defined in
        // SplatBrushTools.cs but are no longer wired here — they get deleted in Phase 3.
        private static readonly IBrushTool[] SplatTools =
        {
            new TerrainLayerPaintTool(),
            new TerrainLayerEraseTool(),
            new TerrainLayerSmoothTool(),
        };

        private static readonly IBrushTool[] DensityTools =
        {
            new DensityPaintTool(),
            new DensityEraseTool(),
            new DensitySmoothTool(),
        };

        private static readonly IBrushTool[] InstanceTools =
        {
            new InstancePlaceTool(),
            new InstanceEraseTool(),
            new InstanceSelectTool(),
        };

        private static readonly IBrushTool[] NoTools = System.Array.Empty<IBrushTool>();

        /// <summary>Returns the ordered tool set for <paramref name="kind"/> (empty for unknown kinds).</summary>
        public static IReadOnlyList<IBrushTool> ToolsFor(LayerType kind) => kind switch
        {
            LayerType.Height => HeightTools,
            LayerType.Splat  => SplatTools,
            LayerType.Grass  => DensityTools,
            LayerType.Props  => InstanceTools,
            _                => NoTools,
        };

        /// <summary>
        /// Resolves the active tool for <paramref name="kind"/>: the tool whose
        /// <see cref="IBrushTool.Id"/> equals <paramref name="activeId"/>, else the kind's
        /// first (default) tool. Returns null when the kind has no tools.
        /// </summary>
        public static IBrushTool? ResolveActiveTool(LayerType kind, string activeId)
        {
            var tools = ToolsFor(kind);
            if (tools.Count == 0) return null;

            for (int i = 0; i < tools.Count; ++i)
                if (tools[i].Id == activeId) return tools[i];

            return tools[0];
        }

        /// <summary>Returns the default (first) tool id for a kind, or empty when none.</summary>
        public static string DefaultToolId(LayerType kind)
        {
            var tools = ToolsFor(kind);
            return tools.Count > 0 ? tools[0].Id : string.Empty;
        }
    }
}
