#nullable enable
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// 220px LOD0 orbit preview for the selected scatter layer.
    /// Wraps <c>AnchorPreviewPanel</c>'s <see cref="PreviewRenderUtility"/> pattern:
    /// <c>BeginPreview</c> / <c>DrawMesh(LodMeshes[0])</c> / <c>EndPreview</c> → <c>GUI.DrawTexture</c>.
    ///
    /// Drag = orbit · scroll = zoom. Preview texture is provided by
    /// <see cref="WorldPainterPreviewCache"/> so re-paints do not re-render.
    ///
    /// Design §4.1 / §6 — Phase 3 task 2.
    /// </summary>
    internal sealed class WorldPainterLodPreviewPanel
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const float PANEL_HEIGHT = 220f;
        private const float DEFAULT_YAW   = 130f;
        private const float DEFAULT_PITCH = -18f;
        private const float DEFAULT_DIST  = 2.4f;
        private const float MIN_DIST      = 0.4f;
        private const float MAX_DIST      = 8f;

        // ── State ─────────────────────────────────────────────────────────────

        private float yaw   = DEFAULT_YAW;
        private float pitch = DEFAULT_PITCH;
        private float dist  = DEFAULT_DIST;

        private PreviewRenderUtility? pru;
        private Material? meshMat;

        // Last rendered values — if these change we re-render (bypass cache for orbit).
        private Mesh? lastMesh;
        private float lastYaw;
        private float lastPitch;
        private float lastDist;
        private Texture2D? rendered;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public WorldPainterLodPreviewPanel()
        {
            AssemblyReloadEvents.beforeAssemblyReload += this.Cleanup;
        }

        public void Cleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= this.Cleanup;
            this.pru?.Cleanup();
            this.pru = null;
            if (this.meshMat != null) Object.DestroyImmediate(this.meshMat);
            this.meshMat = null;
            if (this.rendered != null) Object.DestroyImmediate(this.rendered);
            this.rendered = null;
        }

        // ── Draw ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the 220px preview for <paramref name="layer"/>'s LOD0 mesh.
        /// Call from an IMGUI context (OnInspectorGUI / IMGUIContainer).
        /// </summary>
        public void Draw(ScatterLayer? layer)
        {
            if (layer == null)
            {
                EditorGUILayout.HelpBox("Select a scatter layer to preview it.", MessageType.Info);
                return;
            }

            Mesh[] lodMeshes = layer.Render.LodMeshes;
            Mesh? mesh = lodMeshes.Length > 0 ? lodMeshes[0] : null;
            if (mesh == null)
            {
                EditorGUILayout.HelpBox("Layer has no LOD0 mesh.", MessageType.Warning);
                return;
            }

            this.EnsureResources();

            Rect rect = GUILayoutUtility.GetRect(10f, 100000f, PANEL_HEIGHT, PANEL_HEIGHT);
            this.HandleInteraction(rect);

            if (Event.current.type == EventType.Repaint)
                this.RenderIfDirty(rect, mesh);

            // Show from cache.
            if (this.rendered != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(rect, this.rendered, ScaleMode.StretchToFill, false);

            // Hint label.
            if (Event.current.type == EventType.Repaint)
            {
                var hint = new Rect(rect.x + 4f, rect.yMax - 18f, rect.width - 8f, 16f);
                GUI.Label(hint, "Drag = orbit  ·  scroll = zoom  ·  LOD0", EditorStyles.miniLabel);
            }

            // Reset button.
            using var row = new EditorGUILayout.HorizontalScope();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset View", GUILayout.Width(84f)))
            {
                this.yaw = DEFAULT_YAW; this.pitch = DEFAULT_PITCH; this.dist = DEFAULT_DIST;
                this.lastMesh = null; // force re-render
            }
        }

        // ── Interaction ───────────────────────────────────────────────────────

        private void HandleInteraction(Rect rect)
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && rect.Contains(e.mousePosition):
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl != 0:
                    this.yaw   += e.delta.x * 0.6f;
                    this.pitch  = Mathf.Clamp(this.pitch + e.delta.y * 0.6f, -89f, 89f);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.ScrollWheel when rect.Contains(e.mousePosition):
                    this.dist = Mathf.Clamp(this.dist + e.delta.y * 0.05f, MIN_DIST, MAX_DIST);
                    e.Use();
                    break;
            }
        }

        // ── Render ────────────────────────────────────────────────────────────

        private void RenderIfDirty(Rect rect, Mesh mesh)
        {
            bool orbitChanged = Mathf.Abs(this.yaw   - this.lastYaw)   > 0.001f
                             || Mathf.Abs(this.pitch - this.lastPitch) > 0.001f
                             || Mathf.Abs(this.dist  - this.lastDist)  > 0.001f;

            if (mesh == this.lastMesh && !orbitChanged && this.rendered != null) return;

            if (this.pru == null || this.meshMat == null) return;

            // Re-render.
            this.pru.BeginPreview(rect, GUIStyle.none);

            Quaternion rot  = Quaternion.Euler(this.pitch, this.yaw, 0f);
            Bounds     b    = mesh.bounds;
            float      rad  = Mathf.Max(0.05f, b.extents.magnitude);
            float      camD = this.dist * rad * 2f;

            this.pru.camera.transform.position = b.center + rot * new Vector3(0f, 0f, -camD);
            this.pru.camera.transform.rotation = rot;
            this.pru.camera.nearClipPlane      = 0.01f;
            this.pru.camera.farClipPlane       = 100f;
            this.pru.camera.fieldOfView        = 30f;

            if (this.pru.lights.Length > 0)
            {
                this.pru.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                this.pru.lights[0].intensity = 1.1f;
            }
            this.pru.ambientColor = new Color(0.30f, 0.30f, 0.32f, 1f);

            this.pru.DrawMesh(mesh, Matrix4x4.identity, this.meshMat, 0);
            this.pru.camera.Render();
            Texture src = this.pru.EndPreview();

            // Blit to owned Texture2D.
            int pw = Mathf.Max(1, (int)rect.width);
            int ph = Mathf.Max(1, (int)rect.height);

            if (this.rendered != null &&
                (this.rendered.width != pw || this.rendered.height != ph))
            {
                Object.DestroyImmediate(this.rendered);
                this.rendered = null;
            }

            if (this.rendered == null)
                this.rendered = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
                    { hideFlags = HideFlags.HideAndDontSave };

            RenderTexture prev = RenderTexture.active;
            var tempRt = RenderTexture.GetTemporary(pw, ph, 0);
            Graphics.Blit(src, tempRt);
            RenderTexture.active = tempRt;
            this.rendered.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
            this.rendered.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tempRt);

            this.lastMesh  = mesh;
            this.lastYaw   = this.yaw;
            this.lastPitch = this.pitch;
            this.lastDist  = this.dist;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void EnsureResources()
        {
            this.pru ??= new PreviewRenderUtility();

            if (this.meshMat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")!;
                this.meshMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                SetColorAny(this.meshMat, new Color(0.62f, 0.62f, 0.66f));
            }
        }

        private static void SetColorAny(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        }
    }
}
