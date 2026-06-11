#nullable enable
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Editor-only brush-engine driver entry point for <see cref="WorldPainter"/>.
    ///
    /// This file lives in the GpuTerrain.Editor assembly (Editor-only platform)
    /// and must NEVER be referenced from the GpuTerrain runtime assembly.
    ///
    /// Batch 1: entry STUB only — no stroke dispatch logic yet.
    /// Full brush dispatch wiring is Batch 2 (tasks 6–9).
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

        // ── Lifecycle ─────────────────────────────────────────────────────────

        static WorldPainterAuthoring()
        {
            // Subscribe to editor update loop once at domain reload.
            // Actual stroke dispatch added in Batch 2.
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            // Stub: Batch 2 wires stroke-loop logic here.
            // Keep empty for now — no allocations in the hot path.
        }

        // ── Brush engine entry (stub — Batch 2 fills this in) ─────────────────

        /// <summary>
        /// Called by the scene-view EditorTool when a mouse-drag stroke event fires.
        /// Stub in Batch 1; actual compute dispatch is implemented in Batch 2.
        /// </summary>
        /// <param name="painter">Target painter receiving the stroke.</param>
        /// <param name="worldPos">World-space brush centre this frame.</param>
        internal static void OnStrokeUpdate(WorldPainter painter, Vector3 worldPos)
        {
            // Batch 2: call TerrainPaintTargetResolver → TerrainBrushStroke dispatch.
            _ = painter;
            _ = worldPos;
        }

        /// <summary>
        /// Called on mouse-up to commit the stroke to disk (RT writeback).
        /// Stub in Batch 1; TerrainSculptRtWriteback.ExecuteSync called in Batch 2.
        /// </summary>
        internal static void OnStrokeEnd(WorldPainter painter)
        {
            _ = painter;
        }
    }
}
#endif
