#nullable enable
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Closes the edit-mode "black terrain after script recompile / asset import" gap.
    ///
    /// In edit mode the terrain + grass/prop scatter are submitted ONLY from
    /// <c>WorldPainter.OnBeginCameraRenderingEdit</c> (the <c>beginCameraRendering</c> callback),
    /// which fires only when a SceneView/Game camera actively renders a frame. After a domain
    /// reload (script recompile) or an asset (re)import, <c>WorldPainter.OnEnable</c> rebuilds the
    /// GPU engines — so the data is correct — but nothing forces a repaint, unlike the
    /// <c>WorldPainter.Map</c> setter which calls <c>SceneView.RepaintAll()</c>. An idle SceneView
    /// therefore never issues a camera render and keeps showing the last (black) frame until the
    /// user interacts — which is why the manual "Rebuild" context-menu appears to fix it.
    ///
    /// This driver fires on BOTH triggers (domain reload via <see cref="InitializeOnLoadAttribute"/>,
    /// asset import via <see cref="WorldPainterImportRebuildHook"/>) and routes through
    /// <see cref="WorldPainterRebuildScheduler"/>, whose per-frame <c>Flush</c> coalesces work and
    /// ends with a single <c>SceneView.RepaintAll()</c>. Work is always deferred to a clean editor
    /// tick via <c>EditorApplication.delayCall</c> — never run inside the asset-postprocess callback,
    /// where the AssetDatabase is mid-batch.
    /// </summary>
    [InitializeOnLoad]
    internal static class WorldPainterAutoRebuild
    {
        /// <summary>
        /// Re-entrancy guard. Set true by <see cref="WorldPainterRebuildScheduler"/>.Flush for the
        /// duration of a rebuild so any asset write a rebuild path might emit (SetDirty/SaveAssets)
        /// cannot re-enter <see cref="WorldPainterImportRebuildHook"/> and spin an infinite loop.
        /// </summary>
        internal static bool SuppressImportRebuild;

        static WorldPainterAutoRebuild()
        {
            // The static ctor runs after every domain reload (script recompile / enter-play).
            // Defer so the scene and every WorldPainter.OnEnable have finished first.
            EditorApplication.delayCall += ScheduleRepaint;
        }

        /// <summary>
        /// Asks the rebuild scheduler to repaint every WorldPainter in the open scenes on the next
        /// editor tick. Deferred entry point shared by the domain-reload ctor and the import hook.
        /// </summary>
        internal static void ScheduleRepaint()
        {
            // Play mode (and the transition into it) owns its own build path via
            // WorldPainter.OnEnable.TryBuild + LateUpdate — leave it alone.
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            var painters = Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            for (int i = 0; i < painters.Length; ++i)
            {
                WorldPainter p = painters[i];
                if (p != null) WorldPainterRebuildScheduler.MarkTerrainRepaint(p);
            }
        }
    }

    /// <summary>
    /// Top-level <see cref="AssetPostprocessor"/> (Unity does not reliably discover nested
    /// postprocessors). Fires after every asset-import batch and schedules a deferred repaint
    /// through <see cref="WorldPainterAutoRebuild"/>, guarded against rebuild re-entrancy.
    /// </summary>
    internal sealed class WorldPainterImportRebuildHook : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (WorldPainterAutoRebuild.SuppressImportRebuild) return;
            if (imported.Length == 0 && moved.Length == 0 && deleted.Length == 0) return;

            // delayCall, never inline: the AssetDatabase is mid-batch here, so FindObjectsByType
            // and any GPU work must run on the next clean editor tick.
            EditorApplication.delayCall += WorldPainterAutoRebuild.ScheduleRepaint;
        }
    }
}
#endif
