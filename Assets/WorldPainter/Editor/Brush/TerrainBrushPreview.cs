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
        // internal so tests can reference them instead of hardcoding magic numbers.
        internal const int   CIRCLE_SEGMENTS_MIN    = 16;
        internal const int   CIRCLE_SEGMENTS_MAX    = 128;
        internal const int   SQUARE_SAMPLES_PER_EDGE = 24; // points sampled along each of the 4 edges
        private  const float OUTLINE_BLACK_WIDTH    = 8f;  // px, drawn first (halo)
        private  const float OUTLINE_COLOR_WIDTH    = 4f;  // px, drawn over black
        private  const float FILL_ALPHA             = 0.10f;
        private  const float OUTLINE_BLACK_ALPHA    = 0.6f;

        // ── Brush state (pushed by tool) ──────────────────────────────────────

        private static Vector3    hitPoint;
        private static float      brushRadius;
        private static Color      tintColor;
        private static BrushShape brushShape;
        private static HeightFn?  heightAt;
        private static Texture2D? maskTexture;
        private static double     lastSetTime = -1000d;

        // Root TRS (painting → world). Set by the sculpt tool each frame so the ring is drawn
        // under the WorldPainter root's transform. Identity when root is unset (default).
        private static Matrix4x4  rootMatrix  = Matrix4x4.identity;

        // ── Mask-decal resources (lazily created, editor-only) ─────────────────

        private const string DECAL_SHADER = "Hidden/WorldPainter/BrushDecal";
        private const float  DECAL_ALPHA  = 0.30f;
        private static Material? decalMat;
        private static Mesh?     quadMesh;

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
            if (decalMat != null) { Object.DestroyImmediate(decalMat); decalMat = null; }
            if (quadMesh != null) { Object.DestroyImmediate(quadMesh); quadMesh = null; }
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current brush state. Call from <c>OnToolGUI</c> every event the brush
        /// is hovering a surface. <paramref name="height"/> lets the ring conform to the terrain;
        /// pass null to fall back to a flat ring at the hit point.
        /// <para>
        /// <paramref name="paintingToWorld"/> is the WorldPainter root's <c>localToWorldMatrix</c>.
        /// It is applied as <c>Handles.matrix</c> during drawing so the ring renders on the
        /// transformed (world-space) terrain surface. Pass <c>Matrix4x4.identity</c> (or omit)
        /// for an identity root — no visual change.
        /// </para>
        /// </summary>
        internal static void Set(Vector3 paintingPoint, float radius, Color tint, BrushShape shape,
            HeightFn? height, Texture2D? mask = null,
            Matrix4x4 paintingToWorld = default)
        {
            hitPoint    = paintingPoint;
            brushRadius = radius;
            tintColor   = tint;
            brushShape  = shape;
            heightAt    = height;
            maskTexture = mask;
            // A zero (default) matrix is invalid for drawing — fall back to identity so existing
            // callers that omit the parameter get the same behaviour as before.
            rootMatrix  = paintingToWorld.m33 == 0f ? Matrix4x4.identity : paintingToWorld;
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

            // Apply the root TRS so all Handles draw calls map painting-space coords to world.
            // For an identity root, rootMatrix == Matrix4x4.identity and prevMatrix == identity —
            // no visual change. Save/restore to avoid leaking into subsequent scene-view drawers.
            var prevMatrix = Handles.matrix;
            Handles.matrix = rootMatrix;

            // Mask decal first (under everything): shows the brush mask's SHAPE projected on the
            // ground at the footprint. Only when a mask texture is set — plain circle/square brushes
            // keep just the ring below.
            DrawMaskDecal();

            BuildPerimeter();
            if (perimeter.Count < 3)
            {
                Handles.matrix = prevMatrix;
                return;
            }

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

            Handles.matrix = prevMatrix;

            sceneView.Repaint();
        }

        // ── Mask decal ─────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the brush mask as a tinted, semi-transparent quad on the XZ plane at the brush
        /// footprint (size = 2×radius, lifted above the surface), so the artist sees the actual
        /// mask shape while painting. Flat (not terrain-conformed) — a footprint decal, paired with
        /// the conformed ring below. No-op when no mask is set or the shader can't be found.
        /// </summary>
        private static void DrawMaskDecal()
        {
            if (maskTexture == null) return;

            if (decalMat == null)
            {
                var shader = Shader.Find(DECAL_SHADER);
                if (shader == null) return; // shader not imported → skip the decal, keep the ring
                decalMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (quadMesh == null) quadMesh = BuildGroundQuad();

            float lift = Mathf.Max(Y_OFFSET_MIN, brushRadius * Y_OFFSET_FRACTION);
            var color  = tintColor;
            color.a    = DECAL_ALPHA;

            decalMat.SetTexture("_MainTex", maskTexture);
            decalMat.SetColor("_Color", color);

            // Square brush half-extent = radius; circle's bounding box is also 2×radius. The mask
            // texture fills the footprint either way.
            // Pre-multiply by rootMatrix: hitPoint is in painting space; Graphics.DrawMeshNow
            // ignores Handles.matrix, so we bake the root TRS into the draw matrix directly.
            var localMatrix = Matrix4x4.TRS(
                new Vector3(hitPoint.x, hitPoint.y + lift, hitPoint.z),
                Quaternion.identity,
                new Vector3(brushRadius * 2f, 1f, brushRadius * 2f));
            var matrix = rootMatrix * localMatrix;

            if (decalMat.SetPass(0))
                Graphics.DrawMeshNow(quadMesh, matrix);
        }

        /// <summary>Unit quad (1×1) centred on origin, lying flat on the XZ plane, UVs 0..1.</summary>
        private static Mesh BuildGroundQuad()
        {
            var mesh = new Mesh { name = "WorldPainterBrushDecalQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
            };
            mesh.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        // ── Perimeter construction ────────────────────────────────────────────

        // Thin wrapper that feeds the static-state fields into the pure method below
        // and reuses the cached static buffer (no per-frame alloc on the hot OnSceneGui path).
        // Clears the buffer first — BuildPerimeterPoints appends, so without this the static
        // list would accumulate a fresh loop every repaint frame.
        private static void BuildPerimeter()
        {
            perimeter.Clear();
            BuildPerimeterPoints(hitPoint, brushRadius, brushShape, heightAt, perimeter);
        }

        /// <summary>
        /// Pure perimeter builder — no static-state reads. Appends world-space points
        /// (including a closing duplicate of the first point) into <paramref name="output"/>.
        /// The caller is responsible for <c>output.Clear()</c> before calling.
        ///
        /// Square = 4 OBB edges, each sampled <see cref="SQUARE_SAMPLES_PER_EDGE"/> times,
        /// half-extent = <paramref name="radius"/>. Circle = adaptive segment count from
        /// world-space circumference, clamped to [<see cref="CIRCLE_SEGMENTS_MIN"/>,
        /// <see cref="CIRCLE_SEGMENTS_MAX"/>]. Both shapes conform Y via
        /// <paramref name="height"/> + lift offset; null height falls back to
        /// <paramref name="hitPoint"/>.y.
        /// </summary>
        internal static void BuildPerimeterPoints(
            Vector3           hitPoint,
            float             radius,
            BrushShape        shape,
            HeightFn?         height,
            List<Vector3>     output)
        {
            float lift = Mathf.Max(Y_OFFSET_MIN, radius * Y_OFFSET_FRACTION);

            if (shape == BrushShape.Square)
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
                        float   u = s / (float)SQUARE_SAMPLES_PER_EDGE;
                        Vector2 o = Vector2.Lerp(a, b, u);
                        AppendConformed(hitPoint, o.x, o.y, lift, height, output);
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
                    AppendConformed(
                        hitPoint,
                        Mathf.Cos(ang) * radius,
                        Mathf.Sin(ang) * radius,
                        lift, height, output);
                }
            }

            // Close the loop (append first point again so DrawAAPolyLine/fill closes cleanly).
            if (output.Count > 0)
                output.Add(output[0]);
        }

        private static void AppendConformed(
            Vector3       origin,
            float         offX,
            float         offZ,
            float         lift,
            HeightFn?     height,
            List<Vector3> output)
        {
            float wx = origin.x + offX;
            float wz = origin.z + offZ;
            float wy = height != null && height(wx, wz, out float sampled)
                ? sampled
                : origin.y; // off-tile / no sampler → flat fallback at hit height
            output.Add(new Vector3(wx, wy + lift, wz));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static bool IsFinite(Vector3 v) =>
            IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
