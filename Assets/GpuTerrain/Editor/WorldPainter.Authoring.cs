#nullable enable
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Editor-only brush-engine driver for <see cref="WorldPainter"/>.
    ///
    /// Owns the shared undo stack targeting WorldPainter tile refs (task 9).
    /// The <see cref="WorldPainterSculptTool"/> is the primary dispatch path (task 8);
    /// this class provides singleton plumbing (undo stack, active painter ref,
    /// and the EditorApplication.update tick for async writeback).
    ///
    /// All authoring code stays inside #if UNITY_EDITOR and the Editor asmdef.
    /// WorldPainter.cs (runtime) must NEVER reference this file.
    /// </summary>
    [InitializeOnLoad]
    internal static class WorldPainterAuthoring
    {
        // ── Singleton authoring state ─────────────────────────────────────────

        /// <summary>
        /// Currently inspected WorldPainter component. Set by
        /// <see cref="WorldPainterInspector"/> when its target is selected;
        /// cleared when the selection changes away.
        /// </summary>
        internal static WorldPainter? ActivePainter { get; set; }

        // ── Unified undo stack (task 9) ───────────────────────────────────────

        private static WorldPainterUndo? undoInstance;

        /// <summary>
        /// Shared undo stack for WorldPainter tile snapshots.
        /// Bounded depth 10 / 128 MB cap / evict-oldest (design §7.4).
        /// </summary>
        internal static WorldPainterUndo UndoStack =>
            undoInstance ??= new WorldPainterUndo();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        static WorldPainterAuthoring()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            // No-op tick — kept for future writeback orchestration.
        }

        // ── Brush engine entry ─────────────────────────────────────────────────

        /// <summary>
        /// Called by the scene-view EditorTool when a mouse-drag stroke event fires.
        /// Actual dispatch handled in <see cref="WorldPainterSculptTool"/>.
        /// </summary>
        internal static void OnStrokeUpdate(WorldPainter painter, Vector3 worldPos)
        {
            _ = painter;
            _ = worldPos;
        }

        /// <summary>
        /// Called on mouse-up to commit the stroke to disk (RT writeback).
        /// Actual commit handled in <see cref="WorldPainterSculptTool"/>.
        /// </summary>
        internal static void OnStrokeEnd(WorldPainter painter)
        {
            _ = painter;
        }
    }
}
#endif
