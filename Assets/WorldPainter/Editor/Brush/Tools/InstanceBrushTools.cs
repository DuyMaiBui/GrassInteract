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

            // Shift = quick-erase the cursor area.
            bool deleteMode = Event.current != null && Event.current.shift;
            InstanceUndo.PushOnce(propLayer);

            if (deleteMode)
            {
                ctx.Tool.propEmitter.Emit(
                    propLayer, ctx.WorldPos, WorldPainterState.Brush.size * 0.5f,
                    deleteMode: true, surfaceSampler: ctx.Sampler);
                return;
            }

            // Single instance at the exact cursor (matches the LOD 0 ghost preview).
            // Spacing-stamping along drag still produces one instance per stamp interval.
            ctx.Tool.propEmitter.EmitExactlyOneAt(propLayer, ctx.WorldPos, ctx.Sampler);
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
                deleteMode: true, surfaceSampler: ctx.Sampler);
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
            // EmitExactlyOneAt preserves the raycast Y instead of resampling the heightmap,
            // so the instance lands exactly where the LOD 0 ghost was drawn.
            ctx.Tool.propEmitter.EmitExactlyOneAt(propLayer, ctx.WorldPos, ctx.Sampler);
        }
    }

    /// <summary>
    /// Selection / transform tool. No stamp behaviour — the sculpt tool detects this Id and
    /// hands SceneView input to <see cref="WorldPainterPropTransformEdit.OnSceneGUI"/> so the
    /// user can pick instances and drag a Position / Rotation / Scale gizmo.
    /// </summary>
    internal sealed class InstanceSelectTool : IBrushTool
    {
        public string Id => "instance.select";
        public string Label => "Select";
        public LayerType LayerType => LayerType.Props;
        public void Apply(in BrushToolContext ctx) { /* handled in OnSceneGui */ }
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
