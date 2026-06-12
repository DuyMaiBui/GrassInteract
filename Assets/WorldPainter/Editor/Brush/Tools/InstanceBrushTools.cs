#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Instance (prop) scatter brush tools: Place (scatter N per stamp along the stroke),
    /// Erase (delete instances under the brush), Single (place exactly one at the cursor).
    /// These drive the CPU <see cref="WorldPainterPropStampEmitter"/> — no GPU kernel.
    /// </summary>
    internal sealed class InstancePlaceTool : IBrushTool
    {
        public string Id => "instance.place";
        public string Label => "Place";
        public LayerType LayerType => LayerType.Props;

        public void Apply(in BrushToolContext ctx)
        {
            var layer = BrushToolTargets.ResolveInstanceLayer(ctx.Painter);
            if (layer == null) return;

            // Shift still forces delete on the Place tool (legacy quick-erase modifier).
            bool deleteMode = Event.current != null && Event.current.shift;
            InstanceUndo.PushOnce(layer);
            ctx.Tool.propEmitter.Emit(
                layer, ctx.WorldPos, WorldPainterState.Brush.size * 0.5f,
                deleteMode: deleteMode, surfaceSampler: null);
        }
    }

    internal sealed class InstanceEraseTool : IBrushTool
    {
        public string Id => "instance.erase";
        public string Label => "Erase";
        public LayerType LayerType => LayerType.Props;

        public void Apply(in BrushToolContext ctx)
        {
            var layer = BrushToolTargets.ResolveInstanceLayer(ctx.Painter);
            if (layer == null) return;

            InstanceUndo.PushOnce(layer);
            ctx.Tool.propEmitter.Emit(
                layer, ctx.WorldPos, WorldPainterState.Brush.size * 0.5f,
                deleteMode: true, surfaceSampler: null);
        }
    }

    internal sealed class InstanceSingleTool : IBrushTool
    {
        public string Id => "instance.single";
        public string Label => "Single";
        public LayerType LayerType => LayerType.Props;

        public void Apply(in BrushToolContext ctx)
        {
            var layer = BrushToolTargets.ResolveInstanceLayer(ctx.Painter);
            if (layer == null) return;

            InstanceUndo.PushOnce(layer);
            ctx.Tool.propEmitter.EmitSingle(layer, ctx.WorldPos, surfaceSampler: null);
        }
    }

    /// <summary>Pushes one undo snapshot per layer per stroke (matches legacy prop branch).</summary>
    internal static class InstanceUndo
    {
        public static void PushOnce(InstanceScatterLayer layer)
        {
            var authored = layer.AuthoredInstances;
            if (authored == null) return;

            int key = layer.GetInstanceID();
            if (!WorldPainterAuthoring.UndoStack.CanUndoRecords(key))
                WorldPainterAuthoring.UndoStack.PushRecords(authored, key);
        }
    }
}
