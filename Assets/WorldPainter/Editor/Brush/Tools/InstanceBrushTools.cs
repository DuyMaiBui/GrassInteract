#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Instance (prop) scatter brush tools: Place (scatter N per stamp along the stroke),
    /// Erase (delete instances under the brush), Single (place exactly one at the cursor).
    /// These drive the CPU <see cref="WorldPainterPropStampEmitter"/> — no GPU kernel.
    ///
    /// Routing: when a unified <see cref="PropLayer"/> is active
    /// (<see cref="BrushToolTargets.ResolvePropLayer"/> returns non-null) the emitter overloads
    /// for <see cref="PropLayer"/> are called.
    /// </summary>
    internal sealed class InstancePlaceTool : IBrushTool
    {
        public string Id => "instance.place";
        public string Label => "Place";
        public LayerType LayerType => LayerType.Props;

        public void Apply(in BrushToolContext ctx)
        {
            var propLayer = BrushToolTargets.ResolvePropLayer(ctx.Painter);
            if (propLayer == null) return;

            bool deleteMode = Event.current != null && Event.current.shift;
            InstanceUndo.PushOnce(propLayer);
            ctx.Tool.propEmitter.Emit(
                propLayer, ctx.WorldPos, WorldPainterState.Brush.size * 0.5f,
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
            var propLayer = BrushToolTargets.ResolvePropLayer(ctx.Painter);
            if (propLayer == null) return;

            InstanceUndo.PushOnce(propLayer);
            ctx.Tool.propEmitter.Emit(
                propLayer, ctx.WorldPos, WorldPainterState.Brush.size * 0.5f,
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
            var propLayer = BrushToolTargets.ResolvePropLayer(ctx.Painter);
            if (propLayer == null) return;

            InstanceUndo.PushOnce(propLayer);
            ctx.Tool.propEmitter.EmitSingle(propLayer, ctx.WorldPos, surfaceSampler: null);
        }
    }

    /// <summary>Pushes one undo snapshot per <see cref="PropLayer"/> per stroke.</summary>
    internal static class InstanceUndo
    {
        public static void PushOnce(PropLayer propLayer)
        {
            var authored = propLayer.AuthoredInstances;
            if (authored == null) return;

            int key = propLayer.GetInstanceID();
            if (!WorldPainterAuthoring.UndoStack.CanUndoRecords(key))
                WorldPainterAuthoring.UndoStack.PushRecords(authored, key);
        }
    }
}
