#nullable enable
using UnityEditor;
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// World-space brush decal cursor for terrain sculpt strokes.
    ///
    /// Mirrors the GrassInteract <c>ScatterBrushPreview</c> pattern:
    /// — The tool pushes brush state each frame via <see cref="Set"/>.
    /// — Actual drawing happens from <see cref="SceneView.duringSceneGui"/> during
    ///   <see cref="EventType.Repaint"/> (so DrawMeshNow renders in world space, not
    ///   in the Scene-view corner like it would from OnToolGUI).
    /// — Auto-hides when <see cref="Set"/> stops being called (freshness timeout).
    ///
    /// Visual: translucent filled disc + bright rim + center dot, tinted by sculpt mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainBrushPreview
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float  Y_OFFSET      = 0.05f;
        private const int    DISC_TEX_SIZE = 64;
        private const double FRESH_SECONDS = 0.25;
        private const string DECAL_SHADER  = "Unlit/Transparent";

        // ── Cached resources ──────────────────────────────────────────────────

        private static Mesh?      discMesh;
        private static Material?  discMaterial;
        private static Texture2D? discTexture;
        private static bool       warnLogged;

        // ── Brush state (pushed by tool) ──────────────────────────────────────

        private static Vector3 hitPoint;
        private static Vector3 hitNormal = Vector3.up;
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
            DestroyResources();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes the current brush state. Call from <c>TerrainSculptTool.OnToolGUI</c>
        /// every event the brush is hovering a surface.
        /// </summary>
        internal static void Set(Vector3 worldPoint, Vector3 normal, float radius, Color tint)
        {
            hitPoint  = worldPoint;
            hitNormal = normal;
            brushRadius  = radius;
            tintColor    = tint;
            lastSetTime  = EditorApplication.timeSinceStartup;
        }

        // ── Scene draw ────────────────────────────────────────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            if (EditorApplication.timeSinceStartup - lastSetTime > FRESH_SECONDS) return;
            if (Event.current.type != EventType.Repaint) return;

            EnsureResources();
            if (discMaterial == null || discMesh == null) return;

            Quaternion rot = ComputeDiscRotation(hitNormal);
            float diameter = brushRadius * 2f;
            Matrix4x4 matrix = Matrix4x4.TRS(
                hitPoint + hitNormal * Y_OFFSET,
                rot,
                new Vector3(diameter, diameter, 1f));

            discMaterial.mainTexture = EnsureDiscTexture();
            discMaterial.color       = tintColor;
            discMaterial.SetPass(0);
            Graphics.DrawMeshNow(discMesh, matrix);

            sceneView.Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static Quaternion ComputeDiscRotation(Vector3 normal)
        {
            normal = normal.normalized;
            Vector3 refAxis = Mathf.Abs(Vector3.Dot(normal, Vector3.right)) < 0.9f
                ? Vector3.right : Vector3.forward;
            Vector3 tangent = Vector3.Cross(normal, refAxis).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                return Quaternion.FromToRotation(Vector3.forward, normal);
            return Quaternion.LookRotation(normal, tangent);
        }

        private static void EnsureResources()
        {
            discMesh ??= CreateUnitQuad();
            if (discMaterial == null && !warnLogged)
                discMaterial = CreateMaterial();
        }

        private static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "TerrainBrushDecalQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices  = new[] {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };
            mesh.uv = new[] {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Material? CreateMaterial()
        {
            Shader? shader = Shader.Find(DECAL_SHADER);
            if (shader == null)
            {
                warnLogged = true;
                Debug.LogWarning("[TerrainBrushPreview] Shader 'Unlit/Transparent' not found.");
                return null;
            }
            var mat = new Material(shader) { name = "TerrainBrushDecalMat",
                                             hideFlags = HideFlags.HideAndDontSave };
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }

        private static Texture2D EnsureDiscTexture()
        {
            if (discTexture != null) return discTexture;

            const int N = DISC_TEX_SIZE;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name = "TerrainBrushDisc", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[N * N];
            for (int y = 0; y < N; ++y)
            {
                for (int x = 0; x < N; ++x)
                {
                    float u    = (x + 0.5f) / N - 0.5f;
                    float v    = (y + 0.5f) / N - 0.5f;
                    float dist = Mathf.Sqrt(u * u + v * v) * 2f;
                    // Translucent fill in inner 80%; sharp bright rim at 80-100%.
                    float fill = Mathf.Clamp01(1f - Mathf.SmoothStep(0.6f, 0.8f, dist)) * 0.3f;
                    float rim  = Mathf.Clamp01(Mathf.SmoothStep(0.78f, 0.82f, dist) -
                                               Mathf.SmoothStep(0.96f, 1.0f, dist));
                    float dot  = Mathf.Clamp01(1f - dist / 0.08f);
                    float alpha = Mathf.Clamp01(fill + rim + dot * 0.8f);
                    px[y * N + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            discTexture = tex;
            return tex;
        }

        private static void DestroyResources()
        {
            if (discMesh      != null) { Object.DestroyImmediate(discMesh);      discMesh      = null; }
            if (discMaterial  != null) { Object.DestroyImmediate(discMaterial);  discMaterial  = null; }
            if (discTexture   != null) { Object.DestroyImmediate(discTexture);   discTexture   = null; }
            warnLogged = false;
        }
    }
}
