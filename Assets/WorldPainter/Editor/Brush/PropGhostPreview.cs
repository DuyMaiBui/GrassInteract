#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter.Editor
{
    /// <summary>
    /// LOD-0 ghost preview for the prop Place / Single tools. Renders ONLY when the cursor has
    /// a valid raycast hit and a prop layer is active. Uses the SAME place-from-anchor formula
    /// as <see cref="WorldPainterPropStampEmitter.EmitExactlyOneAt"/> so the ghost shows exactly
    /// where the instance will land — no drift between preview and result.
    ///
    /// Render strategy mirrors the proven implementation in commit 35e2def's
    /// <c>InstanceGhostPreview</c>:
    ///   - State is pushed into static fields each event via <see cref="Set"/>.
    ///   - <see cref="Graphics.DrawMeshNow"/> fires inside <see cref="SceneView.duringSceneGui"/>
    ///     during <c>EventType.Repaint</c>. This is the SRP-reliable path; <see cref="Graphics.DrawMesh"/>
    ///     does NOT consistently appear in the Scene view under URP/HDRP.
    ///   - Tint baked into the material instance (DrawMeshNow doesn't honor MaterialPropertyBlock).
    /// </summary>
    [InitializeOnLoad]
    internal static class PropGhostPreview
    {
        // ── Tint ───────────────────────────────────────────────────────────────
        private static readonly Color COLOR_OK  = new Color(0.30f, 1.00f, 0.30f, 0.55f);
        private static readonly Color COLOR_BAD = new Color(1.00f, 0.25f, 0.25f, 0.55f);

        // ── Pushed state (set each event by the sculpt tool) ──────────────────
        private static PropLayer?  ghostLayer;
        private static Vector3     ghostCursor;        // PAINTING space (root-local) — same space the emitter stores.
        private static Transform?  ghostRoot;          // Painting → world. Null = identity root.
        private static bool        ghostValid;
        private static bool        ghostVisible;
        private static float       ghostYawDeg;
        private static float       ghostScaleMul = 1f;
        private static double      lastSet = -1000d;
        private const  double      FRESH_SECONDS = 0.25;

        // ── Cached resources ──────────────────────────────────────────────────
        private static Material? previewMaterial;
        private static Material? previewSourceMaterial;
        private static Material? fallbackMaterial;

        // ── Lifecycle (InitializeOnLoad) ──────────────────────────────────────
        static PropGhostPreview()
        {
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        private static void Cleanup()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            DestroyMaterial(ref previewMaterial);
            DestroyMaterial(ref fallbackMaterial);
            previewSourceMaterial = null;
            ghostLayer = null;
            ghostVisible = false;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Push the ghost state for this event. <paramref name="cursorPainting"/> is in PAINTING
        /// space (root-local) — the SAME space <see cref="WorldPainterPropStampEmitter.EmitExactlyOneAt"/>
        /// receives and stores — so the anchor/align/yaw math runs identically in both paths. The
        /// pivot is then mapped to world for rendering via <paramref name="root"/> (= the WorldPainter
        /// transform), exactly as the runtime shader maps the stored painting pivot to world. Pass a
        /// null root for an identity painter (world == painting).
        /// </summary>
        public static void Draw(PropLayer layer, Vector3 cursorPainting, bool valid, Transform? root,
            float yawDeg = 0f, float scaleMul = 1f)
        {
            ghostLayer    = layer;
            ghostCursor   = cursorPainting;
            ghostRoot     = root;
            ghostValid    = valid;
            ghostYawDeg   = yawDeg;
            ghostScaleMul = scaleMul;
            ghostVisible  = true;
            lastSet       = EditorApplication.timeSinceStartup;
        }

        // ── Scene-view repaint (the only place that actually renders the ghost) ─

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!ghostVisible || ghostLayer == null) return;
            if (EditorApplication.timeSinceStartup - lastSet > FRESH_SECONDS) return;
            if (Event.current.type != EventType.Repaint) return;

            // Painting → world. Both the pivot (rendered below) and the fallback handle map through
            // this so the ghost tracks the rendered prop under a non-identity WorldPainter root.
            Matrix4x4 rootMatrix = ghostRoot != null ? ghostRoot.localToWorldMatrix : Matrix4x4.identity;

            ScatterLod[] lods = ghostLayer.Render.Lods;
            if (lods.Length == 0 || lods[0].mesh == null)
            {
                // No LOD0 — fall back to a wire disc handle so the user sees SOMETHING and the
                // Setup-panel warning will direct them to assign a mesh.
                DrawFallbackHandle(rootMatrix.MultiplyPoint3x4(ghostCursor), ghostValid);
                return;
            }
            Mesh mesh = lods[0].mesh!;

            // Mirror EmitExactlyOneAt EXACTLY — every line must match so the ghost is byte-identical
            // to the placed instance. All math below is in PAINTING space (like the emitter); only
            // the final matrix is mapped to world via rootMatrix.
            Vector2 scaleRange = ghostLayer.OverrideScaleRange
                ? ghostLayer.ScaleRangeOverride
                : new Vector2(1f, 1f);
            float scale = (scaleRange.x + scaleRange.y) * 0.5f;
            if (scale <= 0f) scale = 1f;

            // ── BYTE-IDENTICAL apply block — keep in lockstep with ─────────────
            //    WorldPainterPropStampEmitter.EmitExactlyOneAt. The sticky E/R-adjust yaw/scale and
            //    the align-to-normal compose order (alignRot * yawRot) MUST match there exactly, or
            //    the placed instance drifts from this ghost.
            scale *= ghostScaleMul;

            // Honour PropAlignToNormal so the ghost matches the placed instance on slopes. When the
            // anchor is non-zero, identity-vs-aligned rotation pivots the entire mesh around the
            // anchor, which the user perceives as a horizontal position offset between ghost and
            // landed mesh (~25 cm on a 30° slope with anchor=(0,0.5,0)). Sampling the same surface
            // sampler the emitter uses keeps both paths byte-identical.
            Quaternion alignRot = Quaternion.identity;
            if (ghostLayer.PropAlignToNormal)
            {
                var sampler = WorldPainterState.ActivePainter?.ScatterSampler;
                if (sampler != null &&
                    sampler.TrySample(ghostCursor.x, ghostCursor.z, out var hit) &&
                    hit.Normal.sqrMagnitude > 0.01f)
                {
                    alignRot = Quaternion.FromToRotation(Vector3.up, hit.Normal);
                }
            }
            Quaternion rot = alignRot * Quaternion.Euler(0f, ghostYawDeg, 0f);

            Vector3 anchor = ghostLayer.AnchorOffsetLocal;
            Vector3 pivot  = ghostCursor - rot * (anchor * scale);

            // pivot/rot/scale are painting-space; map to world for rendering exactly as the runtime
            // shader maps the stored painting pivot to world (rootMatrix * paintingTRS).
            Matrix4x4 matrix = rootMatrix * Matrix4x4.TRS(pivot, rot, Vector3.one * scale);
            Color tint = ghostValid ? COLOR_OK : COLOR_BAD;
            Material mat = ResolveDrawMaterial(ghostLayer.Render.Material, tint);

            // Bake the tint per-call — DrawMeshNow doesn't support MaterialPropertyBlock.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     tint);

            if (!mat.SetPass(0)) return;
            Graphics.DrawMeshNow(mesh, matrix);

            // Reset visibility so the ghost auto-hides if Set() stops being called next frame.
            ghostVisible = false;
        }

        // ── Material resolution ───────────────────────────────────────────────

        private static Material ResolveDrawMaterial(Material? sourceMat, Color tint)
        {
            if (sourceMat != null)
            {
                if (!ReferenceEquals(sourceMat, previewSourceMaterial))
                    ReclonePreviewMaterial(sourceMat);
                if (previewMaterial != null) return previewMaterial;
            }
            return EnsureFallbackMaterial();
        }

        private static void ReclonePreviewMaterial(Material source)
        {
            DestroyMaterial(ref previewMaterial);
            previewSourceMaterial = source;

            bool hasUrp     = source.HasProperty("_Surface");
            bool hasBuiltIn = source.HasProperty("_Mode");
            if (!hasUrp && !hasBuiltIn) return; // unknown shader → fallback

            var clone = new Material(source)
            {
                name      = "PropGhostMat",
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (hasUrp)
            {
                clone.SetFloat("_Surface", 1f);
                clone.SetFloat("_Blend",   0f);
                clone.SetInt("_SrcBlend",  (int)BlendMode.SrcAlpha);
                clone.SetInt("_DstBlend",  (int)BlendMode.OneMinusSrcAlpha);
                clone.SetInt("_ZWrite",    0);
                clone.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                clone.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                clone.SetFloat("_Mode", 3f);
                clone.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                clone.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                clone.SetInt("_ZWrite",   0);
                clone.EnableKeyword("_ALPHABLEND_ON");
                clone.renderQueue = (int)RenderQueue.Transparent;
            }
            clone.SetOverrideTag("RenderType", "Transparent");
            previewMaterial = clone;
        }

        private static Material EnsureFallbackMaterial()
        {
            if (fallbackMaterial != null) return fallbackMaterial;
            Shader? shader = Shader.Find("Unlit/Transparent")
                          ?? Shader.Find("Hidden/Internal-Colored")
                          ?? Shader.Find("Unlit/Color");
            fallbackMaterial = new Material(shader!) { name = "PropGhostFallback", hideFlags = HideFlags.HideAndDontSave };
            return fallbackMaterial;
        }

        private static void DestroyMaterial(ref Material? mat)
        {
            if (mat == null) return;
            Object.DestroyImmediate(mat);
            mat = null;
        }

        private static void DrawFallbackHandle(Vector3 worldPos, bool valid)
        {
            Color c = valid
                ? new Color(0.3f, 1f, 0.3f, 0.8f)
                : new Color(1f, 0.2f, 0.2f, 0.8f);
            using (new Handles.DrawingScope(c))
            {
                Handles.DrawWireDisc(worldPos, Vector3.up, 0.5f);
                Handles.DrawLine(worldPos, worldPos + Vector3.up * 1.5f);
            }
        }
    }
}
