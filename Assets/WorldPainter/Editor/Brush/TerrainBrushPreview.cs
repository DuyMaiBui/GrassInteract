#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// World-space brush cursor for terrain sculpt strokes — pure Handles technique
    /// (MegaWorld-style double-stroke ring + faint convex-poly fill, conformed to terrain
    /// via the existing <see cref="HeightFn"/>).
    ///
    /// — The tool pushes brush state each frame via <see cref="Set"/>.
    /// — Drawing happens from <see cref="SceneView.duringSceneGui"/> during
    ///   <see cref="EventType.Repaint"/>.
    /// — Auto-hides when <see cref="Set"/> stops being called (freshness timeout).
    ///
    /// Shape: Circle = adaptive-segment ring / Square = 4-edge OBB ring.
    /// Both conform Y via <see cref="HeightFn"/> + lift offset.
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainBrushPreview
    {
        /// <summary>
        /// Per-vertex terrain height query. Returns true + world Y when (wx,wz) is on a
        /// loaded tile. Supplied by the sculpt tool so the ring can conform to the surface.
        /// </summary>
        internal delegate bool HeightFn(float worldX, float worldZ, out float worldY);

        // ── Constants ─────────────────────────────────────────────────────────

        // Lift the conformed ring above the surface so it isn't occluded by the GPU-rendered
        // terrain. Scales with brush radius (bigger brush ⇒ coarser surrounding LOD ⇒ larger
        // dip), with a floor.
        private const float  Y_OFFSET_MIN      = 0.15f; // metres (floor for small brushes)
        private const float  Y_OFFSET_FRACTION = 0.15f; // × brushRadius

        // Reject hover points farther than 1e6 m from origin (|p|² > 1e12).
        private const float  MAX_HIT_SQR   = 1e12f;
        private const double FRESH_SECONDS = 0.25;

        // Handles visual parameters (MegaWorld double-stroke technique).
        private const int   CIRCLE_SEGMENTS_MIN   = 16;
        private const int   CIRCLE_SEGMENTS_MAX   = 128;
        private const int   SQUARE_SAMPLES_PER_EDGE = 24; // points sampled along each of the 4 edges
        private const float OUTLINE_BLACK_WIDTH   = 8f;   // px, drawn first (halo)
        private const float OUTLINE_COLOR_WIDTH   = 4f;   // px, drawn over black
        private const float FILL_ALPHA            = 0.10f;
        private const float OUTLINE_BLACK_ALPHA   = 0.6f;

        // ── Brush state (pushed by tool) ──────────────────────────────────────

        private static Vector3    hitPoint;
        private static float      brushRadius;
        private static Color      tintColor;
        private static BrushShape brushShape;
        private static HeightFn?  heightAt;
        private static double     lastSetTime = -1000d;

        // ── Perimeter buffer (reused per frame to avoid per-draw alloc) ────────

        private static readonly List<Vector3> perimeter = new(160);

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
            perimeter.Clear();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current brush state. Call from <c>OnToolGUI</c> every event the brush
        /// is hovering a surface. <paramref name="height"/> lets the ring conform to the terrain;
        /// pass null to fall back to a flat ring at the hit point.
        /// </summary>
        internal static void Set(Vector3 worldPoint, float radius, Color tint, BrushShape shape, HeightFn? height)
        {
            hitPoint    = worldPoint;
            brushRadius = radius;
            tintColor   = tint;
            brushShape  = shape;
            heightAt    = height;
            lastSetTime = EditorApplication.timeSinceStartup;
        }

        // ── Scene draw ────────────────────────────────────────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            if (EditorApplication.timeSinceStartup - lastSetTime > FRESH_SECONDS) return;
            if (Event.current.type != EventType.Repaint) return;

            if (!IsFinite(hitPoint) || hitPoint.sqrMagnitude > MAX_HIT_SQR ||
                !IsFinite(brushRadius) || brushRadius <= 0f)
                return;

            BuildPerimeter();
            if (perimeter.Count < 3) return;

            var pts = perimeter.ToArray();

            // Faint fill first (drawn under the ring). zTest Always so it shows through terrain.
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            var fill = tintColor;
            fill.a = FILL_ALPHA;
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(pts);

            // Ring: double stroke — black halo then tint color (MegaWorld technique).
            var black = new Color(0f, 0f, 0f, OUTLINE_BLACK_ALPHA);
            Handles.color = black;
            Handles.DrawAAPolyLine(OUTLINE_BLACK_WIDTH, pts);
            var ring = tintColor;
            ring.a = 1f;
            Handles.color = ring;
            Handles.DrawAAPolyLine(OUTLINE_COLOR_WIDTH, pts);

            sceneView.Repaint();
        }

        // ── Perimeter construction ────────────────────────────────────────────

        private static void BuildPerimeter()
        {
            perimeter.Clear();
            float radius = brushRadius;
            float lift   = Mathf.Max(Y_OFFSET_MIN, radius * Y_OFFSET_FRACTION);

            if (brushShape == BrushShape.Square)
            {
                // Square OBB in the XZ plane, half-extent = radius (matches the GPU Chebyshev
                // half-extent). Corners CCW: (-r,-r) (+r,-r) (+r,+r) (-r,+r), each edge sampled
                // SQUARE_SAMPLES_PER_EDGE times.
                Vector2[] corners =
                {
                    new(-radius, -radius), new(radius, -radius),
                    new(radius,   radius), new(-radius, radius),
                };
                for (int e = 0; e < 4; ++e)
                {
                    Vector2 a = corners[e];
                    Vector2 b = corners[(e + 1) % 4];
                    for (int s = 0; s < SQUARE_SAMPLES_PER_EDGE; ++s)
                    {
                        float u  = s / (float)SQUARE_SAMPLES_PER_EDGE;
                        Vector2 o = Vector2.Lerp(a, b, u);
                        AppendConformed(o.x, o.y, lift);
                    }
                }
            }
            else
            {
                // Circle: adaptive segment count from world-space circumference (MegaWorld-style).
                float circumference = 2f * Mathf.PI * radius;
                int segments = Mathf.Clamp(
                    Mathf.CeilToInt(circumference / 1.5f), // ~1 point per 1.5 m
                    CIRCLE_SEGMENTS_MIN, CIRCLE_SEGMENTS_MAX);
                for (int i = 0; i < segments; ++i)
                {
                    float ang = (i / (float)segments) * Mathf.PI * 2f;
                    AppendConformed(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, lift);
                }
            }

            // Close the loop.
            if (perimeter.Count > 0)
                perimeter.Add(perimeter[0]);
        }

        private static void AppendConformed(float offX, float offZ, float lift)
        {
            float wx = hitPoint.x + offX;
            float wz = hitPoint.z + offZ;
            float wy = heightAt != null && heightAt(wx, wz, out float sampled)
                ? sampled
                : hitPoint.y; // off-tile / no sampler → flat fallback at hit height
            perimeter.Add(new Vector3(wx, wy + lift, wz));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static bool IsFinite(Vector3 v) =>
            IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
