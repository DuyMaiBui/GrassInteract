#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// World-space brush cursor for terrain sculpt strokes.
    ///
    /// Renders a CONSTANT-world-size disc gizmo (radius = brush size in metres) via
    /// <see cref="Handles"/>, so it projects through the scene camera correctly and scales
    /// naturally with zoom — exactly like any 3D object that occupies a fixed area of ground.
    ///
    /// History: the previous implementation baked world-space vertices into a mesh and drew it
    /// with immediate-mode <c>Graphics.DrawMeshNow(mesh, identity)</c> from
    /// <see cref="SceneView.duringSceneGui"/>. That path mis-projected against the scene-view GL
    /// state and inflated the disc at close zoom (≈190 m at a 12 m brush when zoomed in, correct
    /// ≈24 m when zoomed out). Handles render through Unity's gizmo pipeline and do not have that
    /// problem, so the disc now holds a constant world size at every zoom level.
    ///
    /// The tool pushes brush state each frame via <see cref="Set"/>; drawing happens from
    /// <see cref="SceneView.duringSceneGui"/> on <see cref="EventType.Repaint"/>. The cursor
    /// auto-hides when <see cref="Set"/> stops being called (freshness timeout).
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainBrushPreview
    {
        /// <summary>
        /// Per-vertex terrain height query. Retained for call-site compatibility; the disc is now
        /// a flat world gizmo so the height callback is not used.
        /// </summary>
        internal delegate bool HeightFn(float worldX, float worldZ, out float worldY);

        // ── Constants ─────────────────────────────────────────────────────────

        // Lift the disc slightly above the surface so it isn't z-fought / occluded by the
        // GPU-rendered terrain. Scales with brush radius (bigger brush ⇒ coarser surrounding LOD
        // ⇒ larger surface dip), with a floor.
        private const float  Y_OFFSET_MIN      = 0.15f; // metres (floor for small brushes)
        private const float  Y_OFFSET_FRACTION = 0.15f; // × brushRadius
        // Reject hover points farther than 1e6 m from origin (|p|² > 1e12) — anything larger is a
        // degenerate fallback-plane pick, not a real terrain hit.
        private const float  MAX_HIT_SQR   = 1e12f;
        private const double FRESH_SECONDS = 0.25;

        // ── Brush state (pushed by tool) ──────────────────────────────────────

        private static Vector3 hitPoint;
        private static float   brushRadius;
        private static Color   tintColor;
        private static double  lastSetTime = -1000d;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        static TerrainBrushPreview()
        {
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        private static void Cleanup()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current brush state. Call from the sculpt tool's <c>OnToolGUI</c> each event
        /// the brush is hovering a surface. <paramref name="radius"/> is the brush radius in
        /// world-space metres; the disc holds that size at any zoom. <paramref name="height"/> is
        /// retained for call-site compatibility and currently unused.
        /// </summary>
        internal static void Set(Vector3 worldPoint, float radius, Color tint, HeightFn? height)
        {
            hitPoint    = worldPoint;
            brushRadius = radius;
            tintColor   = tint;
            lastSetTime = EditorApplication.timeSinceStartup;
        }

        // ── Scene draw ────────────────────────────────────────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            if (EditorApplication.timeSinceStartup - lastSetTime > FRESH_SECONDS) return;
            if (Event.current.type != EventType.Repaint) return;

            // Guard against an invalid hover point (e.g. fallback-plane ray nearly parallel to the
            // plane → astronomical dist → huge worldPoint).
            if (!IsFinite(hitPoint) || hitPoint.sqrMagnitude > MAX_HIT_SQR ||
                !IsFinite(brushRadius) || brushRadius <= 0f)
                return;

            float   lift   = Mathf.Max(Y_OFFSET_MIN, brushRadius * Y_OFFSET_FRACTION);
            Vector3 center = new Vector3(hitPoint.x, hitPoint.y + lift, hitPoint.z);

            Color prevColor = Handles.color;
            var   prevZTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always; // draw over the terrain

            // Translucent fill — a constant brushRadius-metre world disc (scales with zoom).
            Handles.color = new Color(tintColor.r, tintColor.g, tintColor.b, 0.15f);
            Handles.DrawSolidDisc(center, Vector3.up, brushRadius);

            // Bright rim.
            Handles.color = new Color(tintColor.r, tintColor.g, tintColor.b, 0.95f);
            Handles.DrawWireDisc(center, Vector3.up, brushRadius);

            // Center dot (proportional to radius so it scales with the disc).
            Handles.color = new Color(tintColor.r, tintColor.g, tintColor.b, 0.9f);
            Handles.DrawSolidDisc(center, Vector3.up, Mathf.Max(0.05f, brushRadius * 0.04f));

            Handles.zTest = prevZTest;
            Handles.color = prevColor;

            sceneView.Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static bool IsFinite(Vector3 v) =>
            IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
