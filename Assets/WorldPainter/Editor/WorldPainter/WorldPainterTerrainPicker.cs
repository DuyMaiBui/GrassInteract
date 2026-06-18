#nullable enable
using WorldPainter;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Click the GPU-drawn terrain in the Scene view to select the <see cref="WorldPainter"/>
    /// GameObject.
    ///
    /// The terrain renders via <c>Graphics.RenderMeshIndirect</c> with no selectable GameObject, so
    /// Unity's native picking can never hit it. This persistent <see cref="InitializeOnLoadAttribute"/>
    /// hook subscribes to <see cref="SceneView.duringSceneGui"/> independently of the WorldPainter
    /// inspector (whose own scene-GUI hook only lives while that inspector is shown) — so the user can
    /// click the terrain to select WorldPainter even when another object is currently selected.
    ///
    /// Gating (all must hold): a WorldPainter exists in the scene; paint mode is OFF and no click-only
    /// (place/select) tool is active; the default/View transform tool is active (so it never fights a
    /// Move/Rotate/Scale gizmo drag). Real objects keep priority via
    /// <see cref="HandleUtility.PickGameObject(Vector2, bool)"/>; the WorldPainter is selected ONLY on
    /// a confirmed terrain hit, and an empty-space click is never consumed (normal deselect still works).
    /// </summary>
    [InitializeOnLoad]
    internal static class WorldPainterTerrainPicker
    {
        // Cached scene WorldPainter — invalidated on hierarchy change (add/remove/load) and on a
        // Unity-null (destroyed) cache. Avoids an O(scene) FindFirstObjectByType on every click.
        private static WorldPainter? cachedPainter;

        static WorldPainterTerrainPicker()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.hierarchyChanged -= InvalidateCache;
            EditorApplication.hierarchyChanged += InvalidateCache;
        }

        private static void InvalidateCache() => cachedPainter = null;

        private static WorldPainter? ResolvePainter()
        {
            // Unity-null check: a destroyed painter compares == null and forces a refresh.
            if (cachedPainter != null) return cachedPainter;
            cachedPainter = Object.FindFirstObjectByType<WorldPainter>(FindObjectsInactive.Exclude);
            return cachedPainter;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt) return;

            // Default/View tool only — don't hijack Move/Rotate/Scale gizmo interactions, nor a
            // space-drag pan / RMB-fly view override (viewToolActive).
            if (Tools.current != Tool.View && Tools.current != Tool.None) return;
            if (Tools.viewToolActive) return;

            // Not while WorldPainter is actively driving scene input (brush stroke OR a click-only
            // place/select tool). PaintModeActive covers continuous strokes; IsClickOnlyTool covers
            // the prop place/select tools that bypass paint mode.
            if (WorldPainterState.PaintModeActive) return;
            if (WorldPainterState.ActivePainter != null &&
                WorldPainterState.IsClickOnlyTool(WorldPainterState.ActiveBrushToolId))
                return;

            WorldPainter? painter = ResolvePainter();
            if (painter == null) return;

            // Defer to native picking — if a real object is under the cursor, let Unity select it.
            if (HandleUtility.PickGameObject(e.mousePosition, false) != null) return;

            // Analytic terrain raycast (SSOT with the brush) — select ONLY on a confirmed hit so an
            // empty-space click still falls through to Unity's normal deselect.
            Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!WorldPainterTerrainRaycast.TryPick(worldRay, painter, out _)) return;

            Selection.activeGameObject = painter.gameObject;
            e.Use();
        }
    }
}
