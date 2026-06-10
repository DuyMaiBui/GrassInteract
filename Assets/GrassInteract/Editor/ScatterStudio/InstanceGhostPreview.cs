#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Draws a semi-transparent mesh ghost at the cursor position during Place mode.
    /// Uses LOD0 mesh from <see cref="InstanceScatterLayer.Render"/> with ~50% alpha.
    ///
    /// Render architecture (mirrors <see cref="ScatterBrushPreview"/>):
    /// The tool calls <see cref="Set"/> each frame to push ghost state into static fields.
    /// The actual draw happens inside a <c>SceneView.duringSceneGui</c> callback during
    /// <c>EventType.Repaint</c>, using <c>mat.SetPass(0)</c> + <c>Graphics.DrawMeshNow</c>
    /// — the only path that reliably renders world-space meshes in the SceneView under SRP.
    ///
    /// Tint (green/red) is baked directly into the material instance before each
    /// <c>SetPass(0)</c> call because <c>DrawMeshNow</c> does not support
    /// <see cref="MaterialPropertyBlock"/>.
    ///
    /// Resources (materials) are cached across frames and destroyed on
    /// <see cref="AssemblyReloadEvents.beforeAssemblyReload"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class InstanceGhostPreview
    {
        // ── Colors ─────────────────────────────────────────────────────────────

        private static readonly Color COLOR_OK  = new(0.3f, 1f, 0.3f, 0.5f);
        private static readonly Color COLOR_BAD = new(1f, 0.2f, 0.2f, 0.5f);

        // ── Pushed state (set each frame by the tool) ─────────────────────────

        private static InstanceScatterLayer? ghostLayer;
        private static Vector3               ghostHitPoint;
        private static Vector3               ghostHitNormal = Vector3.up;
        private static bool                  ghostSpacingOk;
        private static bool                  ghostVisible;

        // ── Cached resources ──────────────────────────────────────────────────

        /// <summary>
        /// Clone of the layer's render material, made transparent.
        /// Null when the source material is unknown-shader or null.
        /// </summary>
        private static Material?  previewMaterial;

        /// <summary>Source material <see cref="previewMaterial"/> was cloned from.</summary>
        private static Material?  previewSourceMaterial;

        /// <summary>
        /// Fallback <c>Unlit/Transparent</c> green material used when the layer material
        /// cannot be made transparent or is null.
        /// </summary>
        private static Material?  fallbackMaterial;

        // ── Lifecycle (InitializeOnLoad) ──────────────────────────────────────

        static InstanceGhostPreview()
        {
            SceneView.duringSceneGui               += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        private static void Cleanup()
        {
            SceneView.duringSceneGui               -= OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            ghostVisible = false;
            ghostLayer   = null;
            DestroyResources();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pushes ghost state for this frame. Call from the tool's <c>OnToolGUI</c> every
        /// event while in Place mode with a valid hit. The actual draw fires from
        /// <see cref="OnSceneGui"/> during the SceneView Repaint phase.
        /// </summary>
        /// <param name="layer">Layer to read LOD0 mesh and material from.</param>
        /// <param name="hitPoint">World-space cursor position (raycast hit).</param>
        /// <param name="hitNormal">Surface normal at hit.</param>
        /// <param name="spacingOk">True → green ghost; false → red (spacing rejected).</param>
        /// <param name="visible">Pass <c>false</c> to hide the ghost this frame.</param>
        internal static void Set(
            InstanceScatterLayer layer,
            Vector3              hitPoint,
            Vector3              hitNormal,
            bool                 spacingOk,
            bool                 visible)
        {
            ghostLayer     = layer;
            ghostHitPoint  = hitPoint;
            ghostHitNormal = hitNormal;
            ghostSpacingOk = spacingOk;
            ghostVisible   = visible;
        }

        /// <summary>Hides the ghost. Call when leaving Place mode or losing the raycast hit.</summary>
        internal static void Clear()
        {
            ghostVisible = false;
            ghostLayer   = null;
        }

        // ── SceneView draw (world-space Repaint context) ──────────────────────

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!ghostVisible || ghostLayer == null) return;
            if (Event.current.type != EventType.Repaint) return;

            ScatterRenderConfig render = ghostLayer.Render;
            ScatterLod[]        lods   = render.Lods;

            // Guard: no LOD0 mesh → hide ghost; caller's wire-disc fallback shows instead.
            if (lods.Length == 0 || lods[0].mesh == null)
            {
                ghostVisible = false;
                return;
            }

            Mesh mesh = lods[0].mesh!;

            // Mirror BuildRecord transform minus randomness:
            //   pos   = ghostHitPoint
            //   rot   = AlignToNormal ? FromToRotation(up, normal) : identity  (yaw = 0)
            //   scale = midpoint of [ScaleMin, ScaleMax]
            bool  alignToNormal = ScatterAuthoringState.I.AlignToNormal;
            float scaleMin      = ScatterAuthoringState.I.PlaceScaleMin;
            float scaleMax      = ScatterAuthoringState.I.PlaceScaleMax;

            Quaternion rot   = alignToNormal
                ? Quaternion.FromToRotation(Vector3.up, ghostHitNormal)
                : Quaternion.identity;
            float scale = (scaleMin + scaleMax) * 0.5f;
            if (scale <= 0f) scale = 1f;

            Matrix4x4 matrix = Matrix4x4.TRS(ghostHitPoint, rot, Vector3.one * scale);

            Color    tint = ghostSpacingOk ? COLOR_OK : COLOR_BAD;
            Material mat  = ResolveDrawMaterial(render.Material, tint);

            // Bake tint directly into the material before SetPass — DrawMeshNow does NOT
            // support MaterialPropertyBlock, so per-call colour must be on the mat instance.
            mat.SetColor("_BaseColor", tint);
            mat.SetColor("_Color",     tint);

            if (!mat.SetPass(0)) return;
            Graphics.DrawMeshNow(mesh, matrix);
        }

        // ── Material helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a draw-ready material with <paramref name="tint"/> applied.
        /// Prefers a clone of the layer material made transparent; falls back to
        /// the guaranteed <c>Unlit/Transparent</c> green material.
        /// </summary>
        private static Material ResolveDrawMaterial(Material? sourceMat, Color tint)
        {
            if (sourceMat != null)
            {
                // Re-clone if the source changed.
                if (!ReferenceEquals(sourceMat, previewSourceMaterial))
                    ReclonePreviewMaterial(sourceMat);

                // previewMaterial may be null if the shader is unrecognised — fall through.
                if (previewMaterial != null)
                    return previewMaterial;
            }

            // No source, or shader not recognisable — use guaranteed green fallback.
            return EnsureFallbackMaterial();
        }

        /// <summary>
        /// Clones <paramref name="source"/> and enables full URP + built-in transparency.
        /// If the shader has neither <c>_Surface</c> nor <c>_Mode</c>, sets
        /// <see cref="previewMaterial"/> to null so the caller falls back to the green material.
        /// </summary>
        private static void ReclonePreviewMaterial(Material source)
        {
            if (previewMaterial != null)
                Object.DestroyImmediate(previewMaterial);

            previewMaterial       = null;
            previewSourceMaterial = source;

            bool hasUrp      = source.HasProperty("_Surface");
            bool hasBuiltIn  = source.HasProperty("_Mode");

            if (!hasUrp && !hasBuiltIn)
            {
                // Unknown shader — cannot guarantee transparency; fall back to green.
                return;
            }

            var clone = new Material(source)
            {
                name      = "GhostPreviewMat",
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (hasUrp)
            {
                // URP Lit / Unlit: full transparent surface setup.
                clone.SetFloat("_Surface", 1f);                  // 1 = Transparent
                clone.SetFloat("_Blend",   0f);                  // 0 = Alpha blending
                clone.SetInt("_SrcBlend",  (int)BlendMode.SrcAlpha);
                clone.SetInt("_DstBlend",  (int)BlendMode.OneMinusSrcAlpha);
                clone.SetInt("_ZWrite",    0);
                clone.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                clone.renderQueue = (int)RenderQueue.Transparent;
            }
            else // hasBuiltIn
            {
                // Built-in Standard / Unlit.
                clone.SetFloat("_Mode", 3f); // 3 = Transparent
                clone.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                clone.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                clone.SetInt("_ZWrite",   0);
                clone.EnableKeyword("_ALPHABLEND_ON");
                clone.renderQueue = (int)RenderQueue.Transparent;
            }

            clone.SetOverrideTag("RenderType", "Transparent");
            previewMaterial = clone;
        }

        /// <summary>
        /// Returns (creating on first call) the <c>Unlit/Transparent</c> green fallback
        /// material. This shader ships with every Unity Editor and is intrinsically
        /// alpha-blended (ZWrite Off, Blend SrcAlpha OneMinusSrcAlpha) — no extra setup.
        /// </summary>
        private static Material EnsureFallbackMaterial()
        {
            if (fallbackMaterial != null) return fallbackMaterial;

            Shader? shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Hidden/Internal-Colored");

            fallbackMaterial = new Material(shader != null ? shader : Shader.Find("Unlit/Color")!)
            {
                name      = "GhostFallbackMat",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return fallbackMaterial;
        }

        // ── Resource cleanup ──────────────────────────────────────────────────

        private static void DestroyResources()
        {
            if (previewMaterial  != null) { Object.DestroyImmediate(previewMaterial);  previewMaterial  = null; }
            if (fallbackMaterial != null) { Object.DestroyImmediate(fallbackMaterial); fallbackMaterial = null; }
            previewSourceMaterial = null;
        }
    }
}
