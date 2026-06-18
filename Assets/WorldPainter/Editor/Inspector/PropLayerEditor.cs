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
        SerializedProperty? propScaleFactor;
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
            this.propScaleFactor          = this.serializedObject.FindProperty("scaleFactor");
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

            // Instance-owned axis meshes (per-inspector, never static) — see DrawAxisLine.
            if (this.axisMeshX != null) { Object.DestroyImmediate(this.axisMeshX); this.axisMeshX = null; }
            if (this.axisMeshY != null) { Object.DestroyImmediate(this.axisMeshY); this.axisMeshY = null; }
            if (this.axisMeshZ != null) { Object.DestroyImmediate(this.axisMeshZ); this.axisMeshZ = null; }
            if (this.lineMaterialRed   != null) { Object.DestroyImmediate(this.lineMaterialRed);   this.lineMaterialRed   = null; }
            if (this.lineMaterialGreen != null) { Object.DestroyImmediate(this.lineMaterialGreen); this.lineMaterialGreen = null; }
            if (this.lineMaterialBlue  != null) { Object.DestroyImmediate(this.lineMaterialBlue);  this.lineMaterialBlue  = null; }
            if (this.fallbackMaterial  != null) { Object.DestroyImmediate(this.fallbackMaterial);  this.fallbackMaterial  = null; }
        }

        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();

            // Snapshot scaleFactor before any drawing so we can detect a scaleFactor-only
            // change and guard the outer MarkPropDirty call below from firing on it.
            float scaleFactorBefore = this.propScaleFactor != null ? this.propScaleFactor.floatValue : 1f;

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

            if (this.target is not PropLayer prop) return;

            float scaleFactorAfter = this.propScaleFactor != null ? this.propScaleFactor.floatValue : 1f;
            bool scaleFactorChanged = !Mathf.Approximately(scaleFactorBefore, scaleFactorAfter);

            // Live scaleFactor path: push to engine immediately, no rebuild.
            // MUST NOT call MarkPropDirty — that would trigger a full re-scatter.
            if (scaleFactorChanged)
                ApplyScaleFactorOnAllPainters(prop, scaleFactorAfter);

            // Rebuild path: fires only when something OTHER than scaleFactor changed.
            // Guard: if the ONLY change was scaleFactor, skip MarkPropDirty entirely.
            // Known minor limitation: a single GUI pass changing scaleFactor AND another field at
            // once (Undo/Redo/Reset/paste) skips the rebuild for that pass; the other field reflects
            // on the next rebuild-triggering edit. Build() re-applies scaleFactor in cull/bounds
            // lockstep, so no render desync results.
            if (changed && !scaleFactorChanged)
                RebuildOnAllPainters(prop);
        }

        static void RebuildOnAllPainters(PropLayer layer)
        {
            // Coalesced — see WorldPainterRebuildScheduler. Fast typing in numeric fields can
            // fire EndChangeCheck multiple times per frame; the scheduler collapses them so the
            // game-view render doesn't catch a partially-disposed GPU buffer.
            WorldPainterRebuildScheduler.MarkPropDirty(layer);
        }

        /// <summary>
        /// Pushes the new <paramref name="factor"/> to the live prop engine for
        /// <paramref name="layer"/> without triggering a re-scatter.
        /// </summary>
        static void ApplyScaleFactorOnAllPainters(PropLayer layer, float factor)
        {
            var painters = UnityEngine.Object.FindObjectsByType<WorldPainter>(FindObjectsSortMode.None);
            for (int i = 0; i < painters.Length; ++i)
            {
                var p = painters[i];
                if (p == null || p.Map == null) continue;
                if (!p.Map.SurfaceLayers.Contains(layer)) continue;
                p.SetPropLayerScaleFactor(layer, factor);
            }
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
            // scaleFactor is drawn here for inspector placement but its change is detected via
            // the scaleFactorBefore/After guard in OnInspectorGUI — it routes to
            // SetPropLayerScaleFactor (live, no rebuild) and is excluded from MarkPropDirty.
            EditorGUILayout.PropertyField(this.propScaleFactor!);

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

            // Lights — PreviewRenderUtility's default lights are off; without this an URP/Lit
            // mesh renders near-black and looks like nothing is being drawn. Two-light key+fill
            // is the standard preview setup.
            if (this.previewUtil.lights != null && this.previewUtil.lights.Length > 0 && this.previewUtil.lights[0] != null)
            {
                this.previewUtil.lights[0].intensity         = 1.4f;
                this.previewUtil.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
                if (this.previewUtil.lights.Length > 1 && this.previewUtil.lights[1] != null)
                {
                    this.previewUtil.lights[1].intensity         = 0.8f;
                    this.previewUtil.lights[1].transform.rotation = Quaternion.Euler(60f, -160f, 0f);
                }
            }
            this.previewUtil.ambientColor = new Color(0.5f, 0.5f, 0.5f, 1f);

            // Draw mesh — ALWAYS use the inspector preview material (stock URP Lit), regardless
            // of layer.Render.Material. The layer material typically uses WorldPainter/ScatterInstanced
            // (or similar) which reads procedural-instancing StructuredBuffers (_Instances,
            // _VisibleIndices, _Interactors, _InstanceTilt) that only exist while the GPU engine
            // is running. PreviewRenderUtility.DrawMesh doesn't bind any of them, so on Metal
            // the draw is silently skipped and the mesh disappears. Using a stock Lit material —
            // with _BaseMap and _BaseColor copied from the layer material so the user still sees
            // their texture and tint — sidesteps the buffer dependency entirely.
            if (mesh != null)
            {
                var mat = this.GetOrCreatePreviewMaterial(layer.Render.Material);
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

            // Three separate meshes — one per axis — so each previewUtil.DrawMesh queues a
            // distinct mesh reference. A single shared/static mesh mutated three times
            // collapses to whichever axis was set LAST (camera.Render plays the queued
            // material draws against the mesh's current state, not a snapshot), so only
            // one line ended up rendering — that's why the anchor crosshair "didn't work".
            this.DrawAxisLine(ref this.axisMeshX, pos, pos + new Vector3(AXIS_LENGTH, 0f, 0f), Color.red);
            this.DrawAxisLine(ref this.axisMeshY, pos, pos + new Vector3(0f, AXIS_LENGTH, 0f), Color.green);
            this.DrawAxisLine(ref this.axisMeshZ, pos, pos + new Vector3(0f, 0f, AXIS_LENGTH), Color.blue);
        }

        Mesh? axisMeshX;
        Mesh? axisMeshY;
        Mesh? axisMeshZ;

        void DrawAxisLine(ref Mesh? mesh, Vector3 from, Vector3 to, Color color)
        {
            if (mesh == null)
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new[] { from, to });
            mesh.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0);

            var mat = this.GetOrCreateLineMaterial(color);
            if (mat != null)
                this.previewUtil!.DrawMesh(mesh, Matrix4x4.identity, mat, 0);
        }

        Material? lineMaterialRed;
        Material? lineMaterialGreen;
        Material? lineMaterialBlue;
        Material? fallbackMaterial;

        // Inspector preview material — stock URP Lit (or Standard / Internal-Colored as a
        // last resort) so the mesh renders WITHOUT needing the GPU engine's StructuredBuffers
        // bound. When the layer has its own material assigned we copy across just the visible
        // style (_BaseMap + tint) so the preview matches the user's artistic intent without
        // inheriting the procedural-instancing shader requirements.
        Material? GetOrCreatePreviewMaterial(Material? layerMat)
        {
            if (this.fallbackMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard")
                          ?? Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return null;
                this.fallbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            // Default neutral tint when no layer material — gray so the mesh shape reads cleanly.
            Color tint    = new Color(0.65f, 0.65f, 0.65f, 1f);
            Texture? tex  = null;
            if (layerMat != null)
            {
                if (layerMat.HasProperty("_BaseColor")) tint = layerMat.GetColor("_BaseColor");
                if (layerMat.HasProperty("_BaseMap"))   tex  = layerMat.GetTexture("_BaseMap");
            }

            if (this.fallbackMaterial.HasProperty("_BaseColor"))
                this.fallbackMaterial.SetColor("_BaseColor", tint);
            this.fallbackMaterial.color = tint;
            if (this.fallbackMaterial.HasProperty("_BaseMap"))
                this.fallbackMaterial.SetTexture("_BaseMap", tex);
            // Standard/Internal-Colored expose the legacy "_MainTex" name — set both so whichever
            // the chosen shader actually samples gets the texture.
            if (this.fallbackMaterial.HasProperty("_MainTex"))
                this.fallbackMaterial.SetTexture("_MainTex", tex);

            return this.fallbackMaterial;
        }

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
