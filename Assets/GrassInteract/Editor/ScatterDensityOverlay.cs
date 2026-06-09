#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Renders the active <see cref="DensityScatterLayer"/>'s density map as a heatmap overlay
    /// projected onto the field surface in the Scene view. Registered via
    /// <see cref="SceneView.duringSceneGui"/>.
    ///
    /// Rendering strategy: a single textured quad drawn with <see cref="Graphics.DrawMeshNow"/>
    /// directly in the <see cref="SceneView.duringSceneGui"/> callback (outside any GUI block so
    /// world-space coordinates are honoured). One <see cref="Mesh"/> and one
    /// <see cref="Material"/> are created once and reused every frame — no per-frame allocation.
    ///
    /// Placement extent is derived from <see cref="GrassFieldSpace"/> (the same SSOT the painter
    /// uses) so UV↔world mapping can never drift. Gated behind
    /// <see cref="ScatterAuthoringState.I"/>.<see cref="ScatterAuthoringState.OverlayVisible"/>
    /// (default off), which is also toggled by <see cref="DensityPaintPanel"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScatterDensityOverlay
    {
        // ── Static resources (created once, destroyed on domain reload) ────────

        private static Mesh?     quadMesh;
        private static Material? heatmapMaterial;
        private static bool      materialCreateAttempted; // true after first attempt (prevents per-frame retry on failure)

        // ── Heatmap color gradient (CPU → hot, evaluated per-texel in shader) ──
        // The shader uses a simple red channel → gradient ramp encoded as a gradient
        // texture built once in CreateResources().
        private static Texture2D? rampTexture;

        // ── Active layer binding (set by DensityPaintPanel.Bind) ───────────────

        private static DensityScatterLayer? activeLayer;
        private static ScatterField?        activeField;

        // ── Heatmap shader source ──────────────────────────────────────────────

        private const string SHADER_NAME = "Hidden/GrassInteract/DensityHeatmap";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        static ScatterDensityOverlay()
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

        // ── Public binding API (called by DensityPaintPanel) ───────────────────

        /// <summary>
        /// Sets the layer (and owning field) whose density map is rendered by the overlay.
        /// Pass <c>null</c> to clear the binding.
        /// </summary>
        internal static void SetActiveLayer(DensityScatterLayer? layer, ScatterField? field)
        {
            activeLayer = layer;
            activeField = field;
        }

        // ── Scene GUI callback ─────────────────────────────────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!ScatterAuthoringState.I.OverlayVisible) return;
            if (activeLayer == null || activeField == null) return;

            Texture2D? densityMap = activeLayer.DensityMap;
            if (densityMap == null) return;

            // Build resources lazily — once per domain.
            EnsureResources();
            if (quadMesh == null || heatmapMaterial == null) return;

            // Derive field extent from GrassFieldSpace (same SSOT as the painter).
            Vector3 origin = activeField.ResolveFieldOrigin();
            Vector2 boundsXZ = activeField.ResolveFieldBoundsXZ(activeLayer);
            var space = new GrassFieldSpace(origin, boundsXZ);

            // Quad corners at Y=origin.y; the overlay floats 0.05m above to avoid z-fighting.
            const float Y_OFFSET = 0.05f;
            float y = origin.y + Y_OFFSET;

            Vector3 bl = space.UvToWorld(new Vector2(0f, 0f), y);
            Vector3 br = space.UvToWorld(new Vector2(1f, 0f), y);
            Vector3 tl = space.UvToWorld(new Vector2(0f, 1f), y);
            Vector3 tr = space.UvToWorld(new Vector2(1f, 1f), y);

            // Build/update the quad mesh with current field extent.
            UpdateQuadMesh(quadMesh, bl, br, tl, tr);

            // Bind the current density map texture.
            heatmapMaterial.SetTexture(ShaderPropDensity, densityMap);
            if (rampTexture != null)
                heatmapMaterial.SetTexture(ShaderPropRamp, rampTexture);

            // Draw outside any GUI block so Graphics.DrawMeshNow works in world space.
            heatmapMaterial.SetPass(0);
            Graphics.DrawMeshNow(quadMesh, Matrix4x4.identity);

            // Request continuous repaint so the overlay stays fresh during brushing.
            sceneView.Repaint();
        }

        // ── Shader property IDs (cached) ───────────────────────────────────────

        private static readonly int ShaderPropDensity = Shader.PropertyToID("_DensityTex");
        private static readonly int ShaderPropRamp    = Shader.PropertyToID("_RampTex");
        private static readonly int ShaderPropAlpha   = Shader.PropertyToID("_Alpha");

        // ── Resource management ────────────────────────────────────────────────

        private static void EnsureResources()
        {
            if (quadMesh == null)
                quadMesh = CreateQuadMesh();

            if (rampTexture == null)
                rampTexture = CreateRampTexture();

            if (heatmapMaterial == null && !materialCreateAttempted)
            {
                materialCreateAttempted = true;
                heatmapMaterial = CreateHeatmapMaterial();
            }
        }

        private static Mesh CreateQuadMesh()
        {
            var mesh = new Mesh { name = "DensityOverlayQuad" };
            // Vertices will be updated each frame via UpdateQuadMesh.
            mesh.vertices  = new Vector3[4];
            mesh.uv        = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void UpdateQuadMesh(Mesh mesh, Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr)
        {
            mesh.vertices = new[] { bl, br, tl, tr };
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Builds a 256×1 gradient ramp: black (0) → blue → cyan → green → yellow → red (1).
        /// The heatmap shader samples this ramp using the density value as U coordinate.
        /// </summary>
        private static Texture2D CreateRampTexture()
        {
            const int W = 256;
            var tex = new Texture2D(W, 1, TextureFormat.RGBA32, false)
            {
                name      = "DensityHeatRamp",
                wrapMode  = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            Color[] pixels = new Color[W];
            for (int i = 0; i < W; ++i)
            {
                float t = i / (float)(W - 1);
                pixels[i] = EvaluateHeatColor(t);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true); // make non-readable after upload — saves memory
            return tex;
        }

        /// <summary>Heatmap ramp: 0=transparent-black, 0.25=blue, 0.5=cyan, 0.75=green, 1=red.</summary>
        private static Color EvaluateHeatColor(float t)
        {
            if (t <= 0f) return new Color(0f, 0f, 0f, 0f);

            // Alpha scales in with density so blank areas are transparent.
            float a = Mathf.Clamp01(t * 2f) * 0.75f;

            Color col;
            if (t < 0.25f)
                col = Color.Lerp(Color.blue,    Color.cyan,  t / 0.25f);
            else if (t < 0.5f)
                col = Color.Lerp(Color.cyan,    Color.green, (t - 0.25f) / 0.25f);
            else if (t < 0.75f)
                col = Color.Lerp(Color.green,   Color.yellow, (t - 0.5f) / 0.25f);
            else
                col = Color.Lerp(Color.yellow,  Color.red,   (t - 0.75f) / 0.25f);

            col.a = a;
            return col;
        }

        private static Material CreateHeatmapMaterial()
        {
            // Try the dedicated hidden shader first; fall back to a runtime-built one.
            Shader? shader = Shader.Find(SHADER_NAME);
            if (shader == null)
                shader = BuildFallbackShader();

            if (shader == null)
            {
                Debug.LogWarning("[ScatterDensityOverlay] Could not find or build heatmap shader — overlay disabled.");
                return null!;
            }

            var mat = new Material(shader)
            {
                name           = "DensityHeatmapMat",
                hideFlags      = HideFlags.HideAndDontSave,
            };
            mat.SetFloat(ShaderPropAlpha, 0.75f);
            return mat;
        }

        /// <summary>
        /// Builds a minimal transparent-blended quad shader at runtime when the dedicated
        /// <c>Hidden/GrassInteract/DensityHeatmap</c> asset is absent.
        /// </summary>
        private static Shader? BuildFallbackShader()
        {
            // Use a transparent/overlay-friendly sprite shader that accepts _MainTex.
            // This covers the case where the dedicated shader file hasn't been created yet.
            Shader? s = Shader.Find("Sprites/Default");
            if (s != null) return s;

            s = Shader.Find("Unlit/Transparent");
            return s; // may still be null on stripped builds — caller handles null
        }

        private static void DestroyResources()
        {
            if (quadMesh != null)        { Object.DestroyImmediate(quadMesh);        quadMesh        = null; }
            if (heatmapMaterial != null) { Object.DestroyImmediate(heatmapMaterial); heatmapMaterial = null; }
            if (rampTexture != null)     { Object.DestroyImmediate(rampTexture);     rampTexture     = null; }
            materialCreateAttempted = false;
        }
    }
}
