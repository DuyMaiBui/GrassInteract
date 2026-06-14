#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    [CustomEditor(typeof(PropLayer))]
    public sealed class PropLayerEditor : UnityEditor.Editor
    {
        // ── Foldout section name constants ───────────────────────────────────

        const string SECTION_IDENTITY  = "Identity";
        const string SECTION_ANCHOR    = "Anchor";
        const string SECTION_PLACEMENT = "Placement";
        const string SECTION_RENDER    = "Render";

        static string PrefKey(string section) =>
            $"WorldPainter.PropLayerEditor.{section}";

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
        SerializedProperty? propPropPivotOffset;
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

        // ── Foldout states ────────────────────────────────────────────────────

        bool foldIdentity;
        bool foldAnchor;
        bool foldPlacement;
        bool foldRender;

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
            this.propPropPivotOffset      = this.serializedObject.FindProperty("propPivotOffset");
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

            this.foldIdentity  = EditorPrefs.GetBool(PrefKey(SECTION_IDENTITY),  true);
            this.foldAnchor    = EditorPrefs.GetBool(PrefKey(SECTION_ANCHOR),    true);
            this.foldPlacement = EditorPrefs.GetBool(PrefKey(SECTION_PLACEMENT), true);
            this.foldRender    = EditorPrefs.GetBool(PrefKey(SECTION_RENDER),    true);
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

            this.DrawFoldout(SECTION_IDENTITY,  ref this.foldIdentity,  this.DrawIdentity);
            this.DrawFoldout(SECTION_ANCHOR,    ref this.foldAnchor,    this.DrawAnchor);
            this.DrawFoldout(SECTION_PLACEMENT, ref this.foldPlacement, this.DrawPlacement);
            this.DrawFoldout(SECTION_RENDER,    ref this.foldRender,    this.DrawRender);

            this.serializedObject.ApplyModifiedProperties();
        }

        void DrawFoldout(string label, ref bool state, System.Action body)
        {
            bool next = EditorGUILayout.Foldout(state, label, true, EditorStyles.foldoutHeader);
            if (next != state)
            {
                state = next;
                EditorPrefs.SetBool(PrefKey(label), state);
            }
            if (!state) return;
            EditorGUI.indentLevel++;
            body();
            EditorGUI.indentLevel--;
        }

        void DrawIdentity()
        {
            EditorGUILayout.PropertyField(this.propAuthoredInstances!);
        }

        void DrawAnchor()
        {
            EditorGUILayout.PropertyField(this.propAnchorOffsetLocal!);
            EditorGUILayout.PropertyField(this.propPropPivotOffset!);

            EditorGUILayout.Space(4f);
            this.DrawPreview();
        }

        void DrawPlacement()
        {
            EditorGUILayout.PropertyField(this.propPlacement!,          GUIContent.none, true);
            EditorGUILayout.PropertyField(this.propTilt!,               GUIContent.none, true);
            EditorGUILayout.PropertyField(this.propPropGroundSnap!);
            EditorGUILayout.PropertyField(this.propPropAlignToNormal!);
            EditorGUILayout.PropertyField(this.propOverrideScaleRange!);
            if (this.propOverrideScaleRange!.boolValue)
                EditorGUILayout.PropertyField(this.propScaleRangeOverride!);

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Colliders", EditorStyles.boldLabel);
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
        }

        void DrawRender()
        {
            EditorGUILayout.PropertyField(this.propRender!,  GUIContent.none, true);
            EditorGUILayout.PropertyField(this.propWind!,    GUIContent.none, true);
            EditorGUILayout.PropertyField(this.propDeform!,  GUIContent.none, true);
            EditorGUILayout.PropertyField(this.propBounds!,  GUIContent.none, true);
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

            // Draw crosshair at (anchorOffsetLocal + propPivotOffset)
            Vector3 markerPos = layer.AnchorOffsetLocal + layer.PropPivotOffset;
            this.DrawPreviewCrosshair(markerPos);

            this.previewUtil.camera.Render();

            var texture = this.previewUtil.EndPreview();
            GUI.DrawTexture(previewRect, texture, ScaleMode.StretchToFill, false);

            // Label
            EditorGUI.LabelField(
                new Rect(previewRect.x, previewRect.yMax - 16f, previewRect.width, 16f),
                $"Anchor+Pivot: {markerPos:F2}",
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
