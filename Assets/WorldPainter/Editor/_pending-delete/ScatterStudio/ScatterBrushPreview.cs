#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Normal-aligned textured brush decal cursor for density paint strokes.
    ///
    /// Renders a single quad oriented FLAT on the surface (local +Z aligned to the hit normal), sized
    /// to brush radius, textured with the resolved stamp (procedural soft disc when stamp is
    /// <c>null</c>), mode-tinted, alpha scaled by falloff.
    ///
    /// <para>
    /// Render path: the decal is drawn from a <see cref="SceneView.duringSceneGui"/> callback during
    /// the <see cref="EventType.Repaint"/> phase — the SAME mechanism as
    /// <see cref="ScatterDensityOverlay"/>. <see cref="Graphics.DrawMeshNow"/> only renders in world
    /// space when called from this context; calling it from an <c>EditorTool.OnToolGUI</c> pass draws
    /// in GUI space (pinned to the Scene-view corner) instead. The tool therefore PUSHES the current
    /// brush state via <see cref="Set"/> each frame and this class owns the actual draw.
    /// </para>
    ///
    /// <para>
    /// The cursor auto-hides when <see cref="Set"/> stops being called (mouse leaves the scene or the
    /// tool deactivates) — gated by a freshness timestamp, so no explicit clear is required.
    /// </para>
    ///
    /// SSOT contract: this class does NOT re-resolve <see cref="StampRef"/>. The caller
    /// (<see cref="DensityPaintTool"/>) passes the already-resolved <see cref="Texture2D"/> from
    /// <c>DensityPaintTool.ResolveStamp</c> — there is exactly one resolver.
    ///
    /// Resources (mesh, material, proc-disc texture) are cached across frames and destroyed on
    /// <see cref="AssemblyReloadEvents.beforeAssemblyReload"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScatterBrushPreview
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>World-units offset along the surface normal to avoid z-fighting.</summary>
        private const float Y_OFFSET = 0.02f;

        /// <summary>Resolution of the procedural soft-disc fallback texture (square, power-of-two).</summary>
        private const int PROC_DISC_SIZE = 64;

        /// <summary>Shader used for the decal material. Always present in an editor Unity install.</summary>
        private const string DECAL_SHADER = "Unlit/Transparent";

        /// <summary>
        /// Seconds the brush state stays "fresh". If the tool stops calling <see cref="Set"/> for
        /// longer than this, the cursor stops drawing (mouse left the scene / tool deactivated).
        /// </summary>
        private const double FRESH_SECONDS = 0.25;

        // ── Cached resources ──────────────────────────────────────────────────

        private static Mesh?      quadMesh;
        private static Material?  decalMaterial;
        private static Texture2D? procDiscTexture;
        private static bool       materialWarningLogged;

        // ── Brush state (pushed by the tool via Set) ───────────────────────────

        private static Vector3    hitPoint;
        private static Vector3    hitNormal = Vector3.up;
        private static float      radius;
        private static Texture2D? stampTex;
        private static Color      tint = Color.white;
        private static float      alpha;
        private static double     lastSetTime = -1000d;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        static ScatterBrushPreview()
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
        /// Pushes the current brush state for this frame. Call from the tool's <c>OnToolGUI</c> on
        /// every event where the brush is hovering a surface. The actual draw happens in
        /// <see cref="OnSceneGui"/> during the Repaint phase (world-space context).
        /// </summary>
        /// <param name="hitPoint">World-space brush centre (raycast hit point).</param>
        /// <param name="hitNormal">Surface normal at the hit point (used for orientation).</param>
        /// <param name="radius">World-space brush radius.</param>
        /// <param name="stampTex">Resolved stamp texture, or <c>null</c> for the procedural soft disc.</param>
        /// <param name="tint">Brush mode colour (green = paint, red = erase, yellow = smooth).</param>
        /// <param name="alpha">Overall opacity of the decal (typically the falloff/opacity value).</param>
        internal static void Set(
            Vector3   hitPoint,
            Vector3   hitNormal,
            float     radius,
            Texture2D? stampTex,
            Color     tint,
            float     alpha)
        {
            ScatterBrushPreview.hitPoint    = hitPoint;
            ScatterBrushPreview.hitNormal   = hitNormal;
            ScatterBrushPreview.radius      = radius;
            ScatterBrushPreview.stampTex    = stampTex;
            ScatterBrushPreview.tint        = tint;
            ScatterBrushPreview.alpha       = alpha;
            ScatterBrushPreview.lastSetTime = EditorApplication.timeSinceStartup;
        }

        // ── Scene draw (world space — same context as ScatterDensityOverlay) ───

        private static void OnSceneGui(SceneView sceneView)
        {
            // Only draw if the tool pushed fresh state this frame and only during Repaint.
            if (EditorApplication.timeSinceStartup - lastSetTime > FRESH_SECONDS) return;
            if (Event.current.type != EventType.Repaint) return;

            EnsureResources();
            if (decalMaterial == null || quadMesh == null) return;

            // Orientation: align the quad's local +Z (its face normal) to the surface normal so the
            // quad lies FLAT on the surface.
            Quaternion rot = ComputeDecalRotation(hitNormal);

            float diameter = radius * 2f;
            Matrix4x4 matrix = Matrix4x4.TRS(
                hitPoint + hitNormal * Y_OFFSET,
                rot,
                new Vector3(diameter, diameter, 1f));

            Texture2D tex = stampTex != null ? stampTex : EnsureProcDisc();
            decalMaterial.mainTexture = tex;
            decalMaterial.color       = new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(alpha));

            decalMaterial.SetPass(0);
            Graphics.DrawMeshNow(quadMesh, matrix);

            sceneView.Repaint();
        }

        // ── Orientation helper (internal for unit-test access) ─────────────────

        /// <summary>
        /// Computes a <see cref="Quaternion"/> that orients the decal quad so it lies FLAT on the
        /// surface — i.e. aligns the quad's local +Z (its face normal, since the mesh is in the local
        /// XY plane) to <paramref name="normal"/>. The tangent axis (chosen to avoid degeneracy when
        /// the normal is near-parallel to <see cref="Vector3.right"/>) becomes the quad's up axis.
        ///
        /// Exposed as <c>internal</c> so P6 EditMode tests can exercise the up-normal edge case.
        /// </summary>
        internal static Quaternion ComputeDecalRotation(Vector3 normal)
        {
            normal = normal.normalized;

            // Choose the world reference axis least parallel to 'normal'.
            Vector3 refAxis = Mathf.Abs(Vector3.Dot(normal, Vector3.right)) < 0.9f
                ? Vector3.right
                : Vector3.forward;

            Vector3 tangent = Vector3.Cross(normal, refAxis).normalized;

            // Guard: degenerate tangent → align +Z to the normal directly with arbitrary roll.
            if (tangent.sqrMagnitude < 0.0001f)
                return Quaternion.FromToRotation(Vector3.forward, normal);

            // forward = normal → the quad's face normal points out of the surface (quad lies flat);
            // up = tangent → an in-plane axis.
            return Quaternion.LookRotation(normal, tangent);
        }

        // ── Resource helpers ──────────────────────────────────────────────────

        private static void EnsureResources()
        {
            if (quadMesh == null)
                quadMesh = CreateUnitQuad();

            if (decalMaterial == null && !materialWarningLogged)
                decalMaterial = CreateDecalMaterial();
        }

        private static Mesh CreateUnitQuad()
        {
            // Unit quad in the local XY plane, centred at origin; scaled via the TRS matrix at draw
            // time. DOUBLE-SIDED: both winding orders are emitted so the decal is visible regardless of
            // which way the flat quad's face ends up pointing relative to the camera (no culling gaps).
            var mesh = new Mesh { name = "BrushDecalQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices  = new[] {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            };
            mesh.uv        = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[]
            {
                0, 2, 1, 1, 2, 3, // front face
                0, 1, 2, 1, 3, 2, // back face (reversed winding)
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Material? CreateDecalMaterial()
        {
            Shader? shader = Shader.Find(DECAL_SHADER);
            if (shader == null)
            {
                materialWarningLogged = true;
                Debug.LogWarning($"[ScatterBrushPreview] Shader '{DECAL_SHADER}' not found — " +
                                 "brush decal preview disabled.");
                return null;
            }

            var mat = new Material(shader)
            {
                name      = "BrushDecalMat",
                hideFlags = HideFlags.HideAndDontSave,
            };
            // "Unlit/Transparent" already has ZWrite Off and Blend SrcAlpha OneMinusSrcAlpha. The mesh
            // is double-sided so culling cannot hide the flat decal; setting _Cull is a harmless
            // belt-and-suspenders no-op if the shader has no such property.
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }

        private static Texture2D EnsureProcDisc()
        {
            if (procDiscTexture != null) return procDiscTexture;

            const int N = PROC_DISC_SIZE;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                name      = "BrushProcDisc",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode  = TextureWrapMode.Clamp,
            };

            var pixels = new Color[N * N];
            for (int y = 0; y < N; ++y)
            {
                for (int x = 0; x < N; ++x)
                {
                    float u = (x + 0.5f) / N - 0.5f; // [-0.5, 0.5]
                    float v = (y + 0.5f) / N - 0.5f;
                    float dist = Mathf.Sqrt(u * u + v * v) * 2f; // [0,1] at radius
                    // Smooth-step falloff from centre (1) to edge (0).
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.4f, 1f, dist));
                    pixels[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            procDiscTexture = tex;
            return tex;
        }

        private static void DestroyResources()
        {
            if (quadMesh != null)        { Object.DestroyImmediate(quadMesh);        quadMesh        = null; }
            if (decalMaterial != null)   { Object.DestroyImmediate(decalMaterial);   decalMaterial   = null; }
            if (procDiscTexture != null) { Object.DestroyImmediate(procDiscTexture); procDiscTexture = null; }
            materialWarningLogged = false;
        }
    }
}
