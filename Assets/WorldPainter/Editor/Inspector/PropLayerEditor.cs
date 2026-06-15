#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    [CustomEditor(typeof(PropLayer))]
    public sealed class PropLayerEditor : UnityEditor.Editor
    {
        // ── Box section name constants ───────────────────────────────────────

        const string SECTION_IDENTITY  = "Identity";
        const string SECTION_ANCHOR    = "Anchor";
        const string SECTION_PLACEMENT = "Placement";
        const string SECTION_RENDER    = "Render";

        // ── Preview constants ─────────────────────────────────────────────────

        const int   PREVIEW_WIDTH  = 200;
        const int   PREVIEW_HEIGHT = 150;
        const float PREVIEW_FOV    = 35f;
        const float AXIS_LENGTH    = 0.1f;

        // ── Serialized properties ─────────────────────────────────────────────

        SerializedProperty? propRender;
        SerializedProperty? propWind;
        SerializedProperty? propDeform;
        SerializedProperty? propBounds;
        SerializedProperty? propPlacement;
        SerializedProperty? propTilt;
        SerializedProperty? propAnchorOffsetLocal;
        SerializedProperty? propPropGroundSnap;
        SerializedProperty? propPropAlignToNormal;
        SerializedProperty? propOverrideScaleRange;
        SerializedProperty? propScaleRangeOverride;
        SerializedProperty? propAuthoredInstances;
        SerializedProperty? propGenerateColliders;
        SerializedProperty? propMaxCollidersPerFrame;
        SerializedProperty? propDefaultColliderMesh;
        SerializedProperty? propDefaultColliderConvex;
        SerializedProperty? propDefaultColliderMaterial;
        SerializedProperty? propPoolColliders;
        SerializedProperty? propPoolCap;
        SerializedProperty? propCullColliders;
        SerializedProperty? propDefaultColliderScale;

        // ── Preview state ─────────────────────────────────────────────────────

        PreviewRenderUtility? previewUtil;
        Vector2 previewDrag;
        float   previewDistance = 3f;

        void OnEnable()
        {
            this.propRender               = this.serializedObject.FindProperty("render");
            this.propWind                 = this.serializedObject.FindProperty("wind");
            this.propDeform               = this.serializedObject.FindProperty("deform");
            this.propBounds               = this.serializedObject.FindProperty("bounds");
            this.propPlacement            = this.serializedObject.FindProperty("placement");
            this.propTilt                 = this.serializedObject.FindProperty("tilt");
            this.propAnchorOffsetLocal    = this.serializedObject.FindProperty("anchorOffsetLocal");
            this.propPropGroundSnap       = this.serializedObject.FindProperty("propGroundSnap");
            this.propPropAlignToNormal    = this.serializedObject.FindProperty("propAlignToNormal");
            this.propOverrideScaleRange   = this.serializedObject.FindProperty("overrideScaleRange");
            this.propScaleRangeOverride   = this.serializedObject.FindProperty("scaleRangeOverride");
            this.propAuthoredInstances    = this.serializedObject.FindProperty("authoredInstances");
            this.propGenerateColliders    = this.serializedObject.FindProperty("generateColliders");
            this.propMaxCollidersPerFrame  = this.serializedObject.FindProperty("maxCollidersPerFrame");
            this.propDefaultColliderMesh  = this.serializedObject.FindProperty("defaultColliderMesh");
            this.propDefaultColliderConvex= this.serializedObject.FindProperty("defaultColliderConvex");
            this.propDefaultColliderMaterial = this.serializedObject.FindProperty("defaultColliderMaterial");
            this.propPoolColliders        = this.serializedObject.FindProperty("poolColliders");
            this.propPoolCap              = this.serializedObject.FindProperty("poolCap");
            this.propCullColliders        = this.serializedObject.FindProperty("cullColliders");
            this.propDefaultColliderScale = this.serializedObject.FindProperty("defaultColliderScale");
        }

        void OnDisable()
        {
            this.previewUtil?.Cleanup();
            this.previewUtil = null;

            if (lineAxisMesh != null) { Object.DestroyImmediate(lineAxisMesh); lineAxisMesh = null; }
            if (this.lineMaterialRed   != null) { Object.DestroyImmediate(this.lineMaterialRed);   this.lineMaterialRed   = null; }
            if (this.lineMaterialGreen != null) { Object.DestroyImmediate(this.lineMaterialGreen); this.lineMaterialGreen = null; }
            if (this.lineMaterialBlue  != null) { Object.DestroyImmediate(this.lineMaterialBlue);  this.lineMaterialBlue  = null; }
        }

        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            // Prop layer fields are editable only inside the WorldPainter detail card; the
            // standalone sub-asset inspector renders them read-only.
            using (new EditorGUI.DisabledScope(!WorldPainterLayerEditContext.EditingInWorldPainter))
            {
                DrawBox(SECTION_IDENTITY,  this.DrawIdentity);
                DrawBox(SECTION_ANCHOR,    this.DrawAnchor);
                DrawBox(SECTION_PLACEMENT, this.DrawPlacement);
                DrawBox(SECTION_RENDER,    this.DrawRender);
            }

            bool changed = EditorGUI.EndChangeCheck();
            this.serializedObject.ApplyModifiedProperties();

            // Live preview: rebuild ONLY this prop layer's engine on every painter that
            // references the owning map. Other layers stay live → no full-map flicker.
            if (changed && this.target is PropLayer prop)
                RebuildOnAllPainters(prop);
        }

        static void RebuildOnAllPainters(PropLayer layer)
        {
            // Coalesced — see WorldPainterRebuildScheduler. Fast typing in numeric fields can
            // fire EndChangeCheck multiple times per frame; the scheduler collapses them so the
            // game-view render doesn't catch a partially-disposed GPU buffer.
            WorldPainterRebuildScheduler.MarkPropDirty(layer);
        }

        static void DrawBox(string label, System.Action body)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            body();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// Wrap a nested-struct property inside its own sub-box. Iterates visible children
        /// manually so Unity's built-in foldout for expandable structs is skipped — the
        /// outer helpBox is the only container the user sees.
        /// </summary>
        static void DrawNestedBox(string label, SerializedProperty prop)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            DrawChildrenFlat(prop);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(1f);
        }

        /// <summary>
        /// Draws every visible direct child of <paramref name="prop"/> without a parent foldout.
        /// Each child still uses its own default drawer, so primitives, ranges, and arrays look
        /// exactly like they do when shown elsewhere — only the wrapper foldout is suppressed.
        /// </summary>
        static void DrawChildrenFlat(SerializedProperty prop)
        {
            var iter = prop.Copy();
            var end  = prop.GetEndProperty();
            if (!iter.NextVisible(enterChildren: true)) return;
            while (!SerializedProperty.EqualContents(iter, end))
            {
                EditorGUILayout.PropertyField(iter, includeChildren: true);
                if (!iter.NextVisible(enterChildren: false)) break;
            }
        }

        void DrawIdentity()
        {
            // Authored prop instances are populated by the Place / Single brush tools, never by
            // hand — shown read-only (disabled even inside the WorldPainter card) so the list
            // can't be edited directly.
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(this.propAuthoredInstances!);
        }

        void DrawAnchor()
        {
            EditorGUILayout.PropertyField(this.propAnchorOffsetLocal!);

            EditorGUILayout.Space(4f);
            this.DrawPreview();
        }

        void DrawPlacement()
        {
            DrawNestedBox("Placement Config", this.propPlacement!);
            DrawNestedBox("Tilt", this.propTilt!);
            EditorGUILayout.PropertyField(this.propPropGroundSnap!);
            EditorGUILayout.PropertyField(this.propPropAlignToNormal!);
            EditorGUILayout.PropertyField(this.propOverrideScaleRange!);
            if (this.propOverrideScaleRange!.boolValue)
                EditorGUILayout.PropertyField(this.propScaleRangeOverride!);

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Colliders", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(this.propGenerateColliders!);
            if (this.propGenerateColliders!.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(this.propMaxCollidersPerFrame!);
                EditorGUILayout.PropertyField(this.propDefaultColliderMesh!);
                EditorGUILayout.PropertyField(this.propDefaultColliderConvex!);
                EditorGUILayout.PropertyField(this.propDefaultColliderMaterial!);
                EditorGUILayout.PropertyField(this.propDefaultColliderScale!);
                EditorGUILayout.PropertyField(this.propPoolColliders!);
                EditorGUILayout.PropertyField(this.propPoolCap!);
                EditorGUILayout.PropertyField(this.propCullColliders!);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawRender()
        {
            WorldPainterLodGui.DrawScatterLodSection(this.propRender!);
            EditorGUILayout.Space(2f);
            DrawNestedBox("Wind",          this.propWind!);
            DrawNestedBox("Deform",        this.propDeform!);
            DrawNestedBox("Bounds",        this.propBounds!);
        }

        // ── 3-D preview pane ──────────────────────────────────────────────────

        void DrawPreview()
        {
            var layer = (PropLayer)this.target;

            // Resolve LOD-0 mesh (may be null before assigned)
            Mesh? mesh = null;
            var lods = layer.Render.Lods;
            if (lods != null && lods.Length > 0) mesh = lods[0].mesh;

            var previewRect = GUILayoutUtility.GetRect(PREVIEW_WIDTH, PREVIEW_HEIGHT,
                GUILayout.ExpandWidth(false));

            // Handle mouse drag for orbit rotation
            Event evt = Event.current;
            if (evt.type == EventType.MouseDrag && previewRect.Contains(evt.mousePosition))
            {
                this.previewDrag += evt.delta * 0.5f;
                evt.Use();
                this.Repaint();
            }
            if (evt.type == EventType.ScrollWheel && previewRect.Contains(evt.mousePosition))
            {
                this.previewDistance = Mathf.Clamp(
                    this.previewDistance + evt.delta.y * 0.1f, 0.5f, 20f);
                evt.Use();
                this.Repaint();
            }

            if (evt.type != EventType.Repaint) return;

            // Lazy init
            if (this.previewUtil == null)
                this.previewUtil = new PreviewRenderUtility();

            this.previewUtil.BeginPreview(previewRect, GUIStyle.none);

            // Camera setup
            var cam = this.previewUtil.camera;
            cam.fieldOfView        = PREVIEW_FOV;
            cam.nearClipPlane      = 0.01f;
            cam.farClipPlane       = 100f;
            cam.backgroundColor    = new Color(0.15f, 0.15f, 0.15f, 1f);
            cam.clearFlags         = CameraClearFlags.SolidColor;

            var rotation = Quaternion.Euler(this.previewDrag.y, this.previewDrag.x, 0f);
            cam.transform.position = rotation * new Vector3(0f, 0f, -this.previewDistance);
            cam.transform.rotation = rotation;

            // Draw mesh if available
            if (mesh != null)
            {
                var mat = layer.Render.Material;
                if (mat != null)
                    this.previewUtil.DrawMesh(mesh, Matrix4x4.identity, mat, 0);
            }

            // Draw crosshair at the mesh anchor — single source of truth for both placement
            // and deform/wind/interactor sampling.
            Vector3 markerPos = layer.AnchorOffsetLocal;
            this.DrawPreviewCrosshair(markerPos);

            this.previewUtil.camera.Render();

            var texture = this.previewUtil.EndPreview();
            GUI.DrawTexture(previewRect, texture, ScaleMode.StretchToFill, false);

            // Label
            EditorGUI.LabelField(
                new Rect(previewRect.x, previewRect.yMax - 16f, previewRect.width, 16f),
                $"Anchor: {markerPos:F2}",
                EditorStyles.centeredGreyMiniLabel);
        }

        void DrawPreviewCrosshair(Vector3 pos)
        {
            if (this.previewUtil == null) return;

            // We cannot use Handles inside PreviewRenderUtility easily, so we draw
            // thin axis lines by rendering a small GL immediate-mode pass.
            // Use GL.Begin inside BeginPreview/camera.Render block — not supported externally.
            // Instead draw via previewUtil.DrawMesh with a Line primitive workaround:
            // Simplest approach: create a temporary 1-pixel line mesh for each axis.

            this.DrawAxisLineMesh(pos, pos + new Vector3(AXIS_LENGTH, 0f, 0f), Color.red);
            this.DrawAxisLineMesh(pos, pos + new Vector3(0f, AXIS_LENGTH, 0f), Color.green);
            this.DrawAxisLineMesh(pos, pos + new Vector3(0f, 0f, AXIS_LENGTH), Color.blue);
        }

        static Mesh? lineAxisMesh;

        void DrawAxisLineMesh(Vector3 from, Vector3 to, Color color)
        {
            if (lineAxisMesh == null)
            {
                lineAxisMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            }
            lineAxisMesh.SetVertices(new[] { from, to });
            lineAxisMesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);

            var mat = this.GetOrCreateLineMaterial(color);
            if (mat != null)
                this.previewUtil!.DrawMesh(lineAxisMesh, Matrix4x4.identity, mat, 0);
        }

        Material? lineMaterialRed;
        Material? lineMaterialGreen;
        Material? lineMaterialBlue;

        Material? GetOrCreateLineMaterial(Color color)
        {
            if (color == Color.red)
            {
                this.lineMaterialRed ??= CreateLineMaterial(color);
                return this.lineMaterialRed;
            }
            if (color == Color.green)
            {
                this.lineMaterialGreen ??= CreateLineMaterial(color);
                return this.lineMaterialGreen;
            }
            this.lineMaterialBlue ??= CreateLineMaterial(color);
            return this.lineMaterialBlue;
        }

        static Material? CreateLineMaterial(Color color)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return null;
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            mat.SetInt("_ZWrite",   0);
            mat.color = color;
            return mat;
        }
    }
}
