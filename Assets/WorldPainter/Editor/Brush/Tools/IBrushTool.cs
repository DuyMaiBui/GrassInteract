#nullable enable
namespace WorldPainter.Editor
{
    /// <summary>
    /// A single draw operation the sculpt tool can perform on one tile-stamp
    /// (e.g. height Raise, splat Erase, density Smooth, instance Place).
    ///
    /// Tools are stateless singletons held by <see cref="BrushToolRegistry"/>; per-stamp
    /// data flows in through <see cref="BrushToolContext"/>. This is the seam that replaces
    /// the former hardcoded if/else chain in <c>WorldPainterSculptTool.BindAndDispatch</c>
    /// so new brush behaviours can be added without editing the dispatcher.
    /// </summary>
    internal interface IBrushTool
    {
        /// <summary>Stable identifier, e.g. <c>"height.raise"</c>. Persisted as the active-tool selection.</summary>
        string Id { get; }

        /// <summary>Short UI label shown on the tool-palette button, e.g. <c>"Raise"</c>.</summary>
        string Label { get; }

        /// <summary>The layer type this tool serves. Used by the palette to group tools per layer.</summary>
        LayerType LayerType { get; }

        /// <summary>Performs the draw for one stamp on one tile. Common brush uniforms are
        /// already set on <see cref="BrushToolContext.Compute"/> by the caller.</summary>
        void Apply(in BrushToolContext ctx);
    }
}
