#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Isolated in-window 3D preview for editing an <see cref="InstanceScatterLayer"/>'s deform
    /// <see cref="InstanceScatterLayer.AnchorOffsetLocal"/>. Renders ONLY the layer's LOD0 mesh (at the
    /// instance pivot, identity rotation, unit scale) plus a draggable anchor marker — nothing else from
    /// the scene — so the anchor can be placed without visual clutter (the "prefab-mode" feel).
    ///
    /// Interaction (all inside the panel rect):
    /// <list type="bullet">
    ///   <item>Drag the red anchor marker → moves it in the camera view plane → writes anchorOffsetLocal.</item>
    ///   <item>Drag empty space → orbit. Scroll → zoom. The "Reset View"/"Reset Anchor" buttons restore defaults.</item>
    /// </list>
    ///
    /// The reference instance is a UNIT instance (scale 1, identity rotation), so the world anchor equals
    /// the local offset — what you place here is exactly anchorOffsetLocal. At runtime each instance scales
    /// + rotates that offset by its own transform (see the shader / InstanceTiltSimulator).
    ///
    /// Owns a <see cref="PreviewRenderUtility"/> + procedural marker resources; all are released in
    /// <see cref="Cleanup"/> (called on domain reload and by <see cref="InstancePanel"/> teardown).
    /// </summary>
    internal sealed class AnchorPreviewPanel
    {
        // ── Constants ──────────────────────────────────────────────────────────
        private const float PANEL_HEIGHT   = 220f;
        private const float PICK_PIXELS    = 10f;
        private const float DEFAULT_YAW    = 130f;
        private const float DEFAULT_PITCH  = -18f;
        private const float DEFAULT_DIST   = 2.4f; // multiples of mesh radius
        private const float MIN_DIST       = 0.4f;
        private const float MAX_DIST       = 8f;

        // ── Preview + resources ────────────────────────────────────────────────
        private PreviewRenderUtility? preview;
        private Material? meshMat;     // neutral lit material for the LOD0 mesh
        private Material? anchorMat;   // unlit red for the anchor marker
        private Material? pivotMat;    // unlit gray for the pivot + connector
        private Mesh?     cubeMesh;    // procedural marker cube

        // ── Camera orbit state ─────────────────────────────────────────────────
        private float yaw   = DEFAULT_YAW;
        private float pitch = DEFAULT_PITCH;
        private float dist  = DEFAULT_DIST;
        private bool  draggingAnchor;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        internal AnchorPreviewPanel()
        {
            AssemblyReloadEvents.beforeAssemblyReload += this.Cleanup;
        }

        /// <summary>Releases the preview utility + procedural resources. Idempotent.</summary>
        internal void Cleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= this.Cleanup;
            this.preview?.Cleanup();
            this.preview = null;
            DestroyIfSet(ref this.meshMat);
            DestroyIfSet(ref this.anchorMat);
            DestroyIfSet(ref this.pivotMat);
            if (this.cubeMesh != null) { Object.DestroyImmediate(this.cubeMesh); this.cubeMesh = null; }
        }

        // ── Draw (called from an IMGUIContainer; only while Anchor mode is active) ──

        internal void Draw(InstanceScatterLayer? layer, ScatterField? field, int layerIdx)
        {
            if (layer == null)
            {
                EditorGUILayout.HelpBox("Select an instance layer to edit its anchor.", MessageType.Info);
                return;
            }

            Mesh[] lods = layer.Render.LodMeshes;
            Mesh? mesh  = lods.Length > 0 ? lods[0] : null;
            if (mesh == null)
            {
                EditorGUILayout.HelpBox("Layer has no LOD0 mesh — assign one in the Render config.", MessageType.Warning);
                return;
            }

            this.EnsureResources();

            Vector3 anchorLocal = layer.AnchorOffsetLocal;
            Bounds  b           = mesh.bounds;
            Vector3 center      = b.center;
            float   radius      = Mathf.Max(0.05f, b.extents.magnitude);

            // Reserve a full-width fixed-height rect for the 3D viewport.
            Rect rect = GUILayoutUtility.GetRect(10f, 100000f, PANEL_HEIGHT, PANEL_HEIGHT);

            // Resolve the camera basis for this frame (used by both interaction + render).
            Quaternion camRot = Quaternion.Euler(this.pitch, this.yaw, 0f);
            float      camDist = this.dist * radius * 2f;
            Vector3    camPos  = center + camRot * new Vector3(0f, 0f, -camDist);

            this.HandleInteraction(rect, layer, field, layerIdx, anchorLocal, camRot, camPos, radius);

            if (Event.current.type == EventType.Repaint)
                this.RenderViewport(rect, mesh, anchorLocal, camRot, camPos);

            this.DrawOverlay(rect, anchorLocal);
            this.DrawControls(layer, field, layerIdx);
        }

        // ── Interaction (orbit / zoom / anchor drag) ───────────────────────────

        private void HandleInteraction(Rect rect, InstanceScatterLayer layer, ScatterField? field, int layerIdx,
            Vector3 anchorLocal, Quaternion camRot, Vector3 camPos, float radius)
        {
            Event e = Event.current;
            if (this.preview == null) return;

            // Keep the preview camera in sync so WorldToViewportPoint is valid in ALL event phases
            // (not just Repaint) — needed for screen-space hit-testing on MouseDown.
            Camera cam = this.preview.camera;
            cam.transform.position = camPos;
            cam.transform.rotation = camRot;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane  = 100f;
            cam.fieldOfView   = 30f;
            cam.aspect        = rect.width / Mathf.Max(1f, rect.height);

            Vector2 anchorScreen = this.WorldToPanel(rect, anchorLocal);

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && rect.Contains(e.mousePosition):
                    this.draggingAnchor = Vector2.Distance(e.mousePosition, anchorScreen) <= PICK_PIXELS;
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl != 0 && rect.Contains(e.mousePosition):
                    if (this.draggingAnchor)
                    {
                        // Move the anchor in the camera view plane. Convert the pixel delta to a world
                        // delta at the anchor's depth so 1px of mouse ≈ 1px of marker travel.
                        Vector3 vp     = cam.WorldToViewportPoint(anchorLocal);
                        float   depth  = Mathf.Max(0.01f, vp.z);
                        float   wpp    = (2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / rect.height;
                        Vector3 right  = camRot * Vector3.right;
                        Vector3 up     = camRot * Vector3.up;
                        Vector3 world  = right * (e.delta.x * wpp) + up * (-e.delta.y * wpp);
                        this.WriteAnchor(layer, field, layerIdx, anchorLocal + world);
                    }
                    else
                    {
                        // Orbit.
                        this.yaw   += e.delta.x * 0.6f;
                        this.pitch  = Mathf.Clamp(this.pitch + e.delta.y * 0.6f, -89f, 89f);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0:
                    this.draggingAnchor = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.ScrollWheel when rect.Contains(e.mousePosition):
                    this.dist = Mathf.Clamp(this.dist + e.delta.y * 0.05f, MIN_DIST, MAX_DIST);
                    e.Use();
                    break;
            }
        }

        // ── Render the isolated viewport ───────────────────────────────────────

        private void RenderViewport(Rect rect, Mesh mesh, Vector3 anchorLocal, Quaternion camRot, Vector3 camPos)
        {
            if (this.preview == null || this.meshMat == null || this.anchorMat == null ||
                this.pivotMat == null || this.cubeMesh == null)
                return;

            this.preview.BeginPreview(rect, GUIStyle.none);

            Camera cam = this.preview.camera;
            cam.transform.position = camPos;
            cam.transform.rotation = camRot;

            if (this.preview.lights.Length > 0)
            {
                this.preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                this.preview.lights[0].intensity = 1.1f;
            }
            this.preview.ambientColor = new Color(0.32f, 0.32f, 0.35f, 1f);

            // LOD0 mesh at the instance pivot (origin), identity rotation, unit scale.
            this.preview.DrawMesh(mesh, Matrix4x4.identity, this.meshMat, 0);

            // Connector from pivot (origin) to the anchor.
            float len = anchorLocal.magnitude;
            if (len > 1e-4f)
            {
                Quaternion lrot = Quaternion.FromToRotation(Vector3.up, anchorLocal / len);
                var lmat = Matrix4x4.TRS(anchorLocal * 0.5f, lrot, new Vector3(0.01f, len * 0.5f, 0.01f));
                this.preview.DrawMesh(this.cubeMesh, lmat, this.pivotMat, 0);
            }

            // Pivot marker (small gray) + anchor marker (red), sized relative to the mesh.
            float marker = Mathf.Max(0.02f, mesh.bounds.extents.magnitude * 0.06f);
            this.preview.DrawMesh(this.cubeMesh,
                Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * (marker * 0.7f)), this.pivotMat, 0);
            this.preview.DrawMesh(this.cubeMesh,
                Matrix4x4.TRS(anchorLocal, Quaternion.identity, Vector3.one * marker), this.anchorMat, 0);

            this.preview.camera.Render();
            Texture tex = this.preview.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
        }

        // ── 2D overlay (anchor ring + hint) ────────────────────────────────────

        private void DrawOverlay(Rect rect, Vector3 anchorLocal)
        {
            if (Event.current.type != EventType.Repaint) return;

            Vector2 p = this.WorldToPanel(rect, anchorLocal);
            // Selection ring around the projected anchor so it's obvious it's draggable.
            Handles.BeginGUI();
            Color prev = Handles.color;
            Handles.color = this.draggingAnchor ? Color.yellow : new Color(1f, 0.35f, 0.3f, 1f);
            Handles.DrawWireDisc(p, Vector3.forward, PICK_PIXELS);
            Handles.color = prev;
            Handles.EndGUI();

            var hint = new Rect(rect.x + 4f, rect.yMax - 18f, rect.width - 8f, 16f);
            GUI.Label(hint, "Drag red marker = move anchor · drag bg = orbit · scroll = zoom", EditorStyles.miniLabel);
        }

        // ── Controls row (numeric + resets) ────────────────────────────────────

        private void DrawControls(InstanceScatterLayer layer, ScatterField? field, int layerIdx)
        {
            using var row = new EditorGUILayout.HorizontalScope();

            EditorGUI.BeginChangeCheck();
            Vector3 typed = EditorGUILayout.Vector3Field(GUIContent.none, layer.AnchorOffsetLocal);
            if (EditorGUI.EndChangeCheck())
                this.WriteAnchor(layer, field, layerIdx, typed);

            if (GUILayout.Button("Reset Anchor", GUILayout.Width(96f)))
                this.WriteAnchor(layer, field, layerIdx, Vector3.zero);

            if (GUILayout.Button("Reset View", GUILayout.Width(84f)))
            {
                this.yaw = DEFAULT_YAW; this.pitch = DEFAULT_PITCH; this.dist = DEFAULT_DIST;
            }
        }

        // ── Writeback ──────────────────────────────────────────────────────────

        private void WriteAnchor(InstanceScatterLayer layer, ScatterField? field, int layerIdx, Vector3 newLocal)
        {
            var so = new SerializedObject(layer);
            SerializedProperty? prop = so.FindProperty("anchorOffsetLocal");
            if (prop == null) return;
            if (prop.vector3Value == newLocal) return;

            Undo.RegisterCompleteObjectUndo(layer, "Edit Anchor Offset");
            prop.vector3Value = newLocal;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(layer);
            // Re-bake so InstancedPropEngine.Build pushes the new _AnchorOffset to the materials.
            if (field != null && layerIdx >= 0)
                ScatterRebuildScheduler.MarkDirty(field, layerIdx);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Projects a preview-world point to panel (GUI) coordinates inside <paramref name="rect"/>.</summary>
        private Vector2 WorldToPanel(Rect rect, Vector3 world)
        {
            if (this.preview == null) return rect.center;
            Vector3 vp = this.preview.camera.WorldToViewportPoint(world);
            return new Vector2(rect.x + vp.x * rect.width, rect.y + (1f - vp.y) * rect.height);
        }

        private void EnsureResources()
        {
            this.preview ??= new PreviewRenderUtility();

            if (this.meshMat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")!;
                this.meshMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                SetColorAny(this.meshMat, new Color(0.62f, 0.62f, 0.66f, 1f));
            }
            if (this.anchorMat == null)
                this.anchorMat = MakeUnlit(new Color(1f, 0.3f, 0.25f, 1f));
            if (this.pivotMat == null)
                this.pivotMat = MakeUnlit(new Color(0.55f, 0.55f, 0.6f, 1f));
            if (this.cubeMesh == null)
                this.cubeMesh = MakeCube();
        }

        private static Material MakeUnlit(Color c)
        {
            // Prefer the URP unlit shader — the legacy "Unlit/Color" can render magenta under URP.
            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard")!;
            var m = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            SetColorAny(m, c);
            return m;
        }

        private static void SetColorAny(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        }

        private static void DestroyIfSet(ref Material? m)
        {
            if (m != null) { Object.DestroyImmediate(m); m = null; }
        }

        /// <summary>Unit cube centred at origin (procedural so no asset dependency).</summary>
        private static Mesh MakeCube()
        {
            var mesh = new Mesh { name = "AnchorMarkerCube", hideFlags = HideFlags.HideAndDontSave };
            Vector3[] v =
            {
                new(-0.5f,-0.5f,-0.5f), new(0.5f,-0.5f,-0.5f), new(0.5f,0.5f,-0.5f), new(-0.5f,0.5f,-0.5f),
                new(-0.5f,-0.5f, 0.5f), new(0.5f,-0.5f, 0.5f), new(0.5f,0.5f, 0.5f), new(-0.5f,0.5f, 0.5f),
            };
            int[] t =
            {
                0,2,1, 0,3,2, // back
                4,5,6, 4,6,7, // front
                0,1,5, 0,5,4, // bottom
                3,7,6, 3,6,2, // top
                0,4,7, 0,7,3, // left
                1,2,6, 1,6,5, // right
            };
            mesh.vertices  = v;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
