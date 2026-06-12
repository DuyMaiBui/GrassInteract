#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// World-space brush cursor for terrain sculpt strokes.
    ///
    /// The cursor is a plain hidden <see cref="GameObject"/> with a <see cref="MeshRenderer"/> —
    /// a normal 3D object in the world. Its transform is moved/scaled to follow the brush each
    /// frame (scale = brush diameter in metres), so it holds a constant WORLD size and the camera
    /// projects it exactly like any other scene object: it scales naturally with zoom, never
    /// inflates. The decal shader draws it over the terrain (ZTest Always).
    ///
    /// The tool pushes brush state via <see cref="Set"/> while hovering a surface; the object is
    /// hidden again when <see cref="Set"/> stops being called (freshness timeout).
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainBrushPreview
    {
        /// <summary>Retained for call-site compatibility; the flat quad does not use it.</summary>
        internal delegate bool HeightFn(float worldX, float worldZ, out float worldY);

        // ── Constants ─────────────────────────────────────────────────────────

        private const float  Y_OFFSET_MIN      = 0.15f; // metres (floor for small brushes)
        private const float  Y_OFFSET_FRACTION = 0.15f; // × brushRadius
        private const float  MAX_HIT_SQR       = 1e12f;
        private const double FRESH_SECONDS     = 0.25;
        private const int    TEX_SIZE          = 64;
        private const string DECAL_SHADER      = "WorldPainter/BrushDecal";

        // ── Cached scene object + resources ───────────────────────────────────

        private static GameObject?   previewGo;
        private static MeshRenderer? previewRenderer;
        private static Material?     material;
        private static Mesh?         quadMesh;
        private static Texture2D?    discTexture;
        private static bool          warnLogged;

        // ── Brush state (pushed by tool) ──────────────────────────────────────

        private static double lastSetTime = -1000d;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        static TerrainBrushPreview()
        {
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        private static void Cleanup()
        {
            EditorApplication.update -= OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            DestroyResources();
        }

        // Hide the cursor object once the brush stops hovering (Set went stale).
        private static void OnEditorUpdate()
        {
            if (previewGo == null) return;
            bool fresh = EditorApplication.timeSinceStartup - lastSetTime <= FRESH_SECONDS;
            if (previewGo.activeSelf != fresh)
                previewGo.SetActive(fresh);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current brush state. Call from the sculpt tool's <c>OnToolGUI</c> each event
        /// the brush hovers a surface. <paramref name="radius"/> is the brush radius in world-space
        /// metres. <paramref name="height"/> is retained for compatibility and unused.
        /// </summary>
        internal static void Set(Vector3 worldPoint, float radius, Color tint, HeightFn? height)
        {
            lastSetTime = EditorApplication.timeSinceStartup;

            if (!IsFinite(worldPoint) || worldPoint.sqrMagnitude > MAX_HIT_SQR ||
                !IsFinite(radius) || radius <= 0f)
                return;

            if (!EnsureResources()) return;

            float   lift   = Mathf.Max(Y_OFFSET_MIN, radius * Y_OFFSET_FRACTION);
            float   diameter = radius * 2f;

            // A normal 3D object: position at the hit point, lie flat in XZ, scale to the brush
            // diameter in metres. The camera handles the rest — constant world size at any zoom.
            previewGo!.transform.position   = new Vector3(worldPoint.x, worldPoint.y + lift, worldPoint.z);
            previewGo.transform.rotation    = Quaternion.identity;
            previewGo.transform.localScale  = new Vector3(diameter, 1f, diameter);

            material!.color = tint;
            previewGo.SetActive(true);

            // Nudge the scene view to repaint so the cursor tracks smoothly.
            SceneView.RepaintAll();
        }

        // ── Resource setup ────────────────────────────────────────────────────

        private static bool EnsureResources()
        {
            if (previewGo != null && material != null) return true;
            if (warnLogged) return false;

            Shader? shader = Shader.Find(DECAL_SHADER);
            if (shader == null)
            {
                warnLogged = true;
                Debug.LogWarning($"[TerrainBrushPreview] Shader '{DECAL_SHADER}' not found.");
                return false;
            }

            quadMesh ??= CreateFlatQuad();
            discTexture ??= CreateDiscTexture();

            material = new Material(shader)
            {
                name = "TerrainBrushDecalMat", hideFlags = HideFlags.HideAndDontSave,
            };
            material.mainTexture = discTexture;

            previewGo = new GameObject("WorldPainterBrushPreview")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var mf = previewGo.AddComponent<MeshFilter>();
            mf.sharedMesh = quadMesh;
            previewRenderer = previewGo.AddComponent<MeshRenderer>();
            previewRenderer.sharedMaterial   = material;
            previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewRenderer.receiveShadows    = false;
            previewGo.SetActive(false);
            return true;
        }

        /// <summary>A unit quad (1×1) in the XZ plane, +Y normal, UV 0..1. Double-sided shader.</summary>
        private static Mesh CreateFlatQuad()
        {
            var mesh = new Mesh { name = "BrushQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f), new Vector3(-0.5f, 0f,  0.5f),
            });
            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Radial brush texture: translucent fill, bright rim, center dot.</summary>
        private static Texture2D CreateDiscTexture()
        {
            const int N = TEX_SIZE;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "TerrainBrushDisc", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[N * N];
            for (int y = 0; y < N; ++y)
            for (int x = 0; x < N; ++x)
            {
                float u    = (x + 0.5f) / N - 0.5f;
                float v    = (y + 0.5f) / N - 0.5f;
                float dist = Mathf.Sqrt(u * u + v * v) * 2f;
                float fill = Mathf.Clamp01(1f - Mathf.SmoothStep(0.6f, 0.8f, dist)) * 0.3f;
                float rim  = Mathf.Clamp01(Mathf.SmoothStep(0.78f, 0.82f, dist) -
                                           Mathf.SmoothStep(0.96f, 1.0f, dist));
                float dot  = Mathf.Clamp01(1f - dist / 0.08f);
                float alpha = Mathf.Clamp01(fill + rim + dot * 0.8f);
                px[y * N + x] = new Color(1f, 1f, 1f, alpha);
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private static void DestroyResources()
        {
            if (previewGo   != null) { Object.DestroyImmediate(previewGo);   previewGo   = null; }
            if (material    != null) { Object.DestroyImmediate(material);    material    = null; }
            if (quadMesh    != null) { Object.DestroyImmediate(quadMesh);    quadMesh    = null; }
            if (discTexture != null) { Object.DestroyImmediate(discTexture); discTexture = null; }
            previewRenderer = null;
            warnLogged      = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private static bool IsFinite(Vector3 v) =>
            IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }
}
