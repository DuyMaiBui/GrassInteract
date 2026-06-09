#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// In-window UI Toolkit panel that replaces the old <c>DrawSelectedInstance</c> scene-overlay.
    /// Attaches to the <c>#instance-panel</c> <see cref="VisualElement"/> reserved in
    /// <c>ScatterStudio.uxml</c> — this class does NOT edit the uxml file.
    ///
    /// Responsibilities:
    /// <list type="bullet">
    ///   <item>Single-instance collider config (ported verbatim from
    ///     <c>InstancePlacementTool.DrawSelectedInstance</c>).</item>
    ///   <item>Multi-select batch transform/collider edit.</item>
    ///   <item>Scale-range override toggle + <see cref="Vector2"/> editing
    ///     <c>InstanceScatterLayer.overrideScaleRange</c> / <c>scaleRangeOverride</c>
    ///     via <see cref="SerializedProperty"/>.</item>
    /// </list>
    ///
    /// All mutations are Undo-wrapped. All re-scatter routes through
    /// <see cref="ScatterRebuildScheduler.MarkDirty"/>.
    /// </summary>
    internal sealed class InstancePanel
    {
        // ── Mount root ────────────────────────────────────────────────────────

        private readonly VisualElement root;

        // ── Bound state ───────────────────────────────────────────────────────

        private ScatterField?          activeField;
        private InstanceScatterLayer?  activeLayer;
        private SerializedObject?      layerSO;

        // ── IMGUI container (for complex multi-control Handles-style rows) ────

        private IMGUIContainer? imguiContainer;

        // ── Scale-range override (UI Toolkit PropertyFields) ─────────────────

        private readonly VisualElement scaleOverrideSection;
        private readonly Toggle        overrideToggle;
        private readonly Vector2Field  scaleRangeField;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates the panel and attaches it to <paramref name="mountPoint"/>
        /// (<c>#instance-panel</c> from the uxml).
        /// </summary>
        internal InstancePanel(VisualElement mountPoint)
        {
            this.root = mountPoint;

            // ── Section header ────────────────────────────────────────────────
            var header = new Label("Instance Properties");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop    = 6f;
            header.style.marginBottom = 4f;
            header.style.marginLeft   = 4f;
            this.root.Add(header);

            // ── IMGUI container for the per-instance / batch edit controls ────
            // (uses EditorGUI helpers which require IMGUI — keeps AuthoredInstancesData
            //  calls exactly as they were in DrawSelectedInstance)
            this.imguiContainer = new IMGUIContainer(this.DrawInstanceGUI);
            this.imguiContainer.style.minHeight = 0f;
            this.root.Add(this.imguiContainer);

            // ── Scale-range override section (UI Toolkit) ─────────────────────
            this.scaleOverrideSection = new VisualElement();
            this.scaleOverrideSection.style.marginTop   = 8f;
            this.scaleOverrideSection.style.marginLeft  = 4f;
            this.scaleOverrideSection.style.marginRight = 4f;

            var scaleHeader = new Label("Scale Range Override");
            scaleHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            scaleHeader.style.marginBottom = 2f;
            this.scaleOverrideSection.Add(scaleHeader);

            this.overrideToggle = new Toggle("Override Scale Range");
            this.overrideToggle.RegisterValueChangedCallback(this.OnOverrideToggleChanged);
            this.scaleOverrideSection.Add(this.overrideToggle);

            this.scaleRangeField = new Vector2Field("Scale Range (min, max)");
            this.scaleRangeField.RegisterValueChangedCallback(this.OnScaleRangeChanged);
            this.scaleOverrideSection.Add(this.scaleRangeField);

            this.root.Add(this.scaleOverrideSection);

            // ── Undo listener ─────────────────────────────────────────────────
            Undo.undoRedoPerformed += this.OnUndoRedo;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Binds the panel to a (possibly null) scatter field. Call whenever the active
        /// <see cref="ScatterField"/> or selected <see cref="ScatterLayer"/> changes.
        /// </summary>
        internal void Bind(ScatterField? field)
        {
            this.activeField = field;

            // Determine active InstanceScatterLayer from the field's selected layer
            // by inspecting what's currently selected (same pattern as ScatterStudioWindow).
            this.activeLayer = null;
            this.layerSO     = null;

            // Try to find an active InstanceScatterLayer in the field.
            if (field?.Config != null)
            {
                foreach (var layer in field.Config.Layers)
                {
                    if (layer is InstanceScatterLayer isl)
                    {
                        this.activeLayer = isl;
                        this.layerSO     = new SerializedObject(isl);
                        break; // first instance layer — selection-aware update comes via BindLayer
                    }
                }
            }

            this.RefreshScaleOverrideUI();
        }

        /// <summary>
        /// Binds the panel to a specific <see cref="InstanceScatterLayer"/>. Called by
        /// <see cref="ScatterStudioWindow"/> (via the layer-selection event) to keep the panel
        /// in sync with the layer-rail selection.
        /// </summary>
        internal void BindLayer(InstanceScatterLayer? layer)
        {
            this.activeLayer = layer;
            this.layerSO     = layer != null ? new SerializedObject(layer) : null;
            this.RefreshScaleOverrideUI();
        }

        // ── Scale-range override UI ────────────────────────────────────────────

        private void RefreshScaleOverrideUI()
        {
            if (this.activeLayer == null)
            {
                this.scaleOverrideSection.style.display = DisplayStyle.None;
                return;
            }

            this.scaleOverrideSection.style.display = DisplayStyle.Flex;

            // Update UI fields WITHOUT triggering their change-callbacks.
            this.overrideToggle.SetValueWithoutNotify(this.activeLayer.OverrideScaleRange);
            this.scaleRangeField.SetValueWithoutNotify(this.activeLayer.ScaleRangeOverride);
            this.scaleRangeField.SetEnabled(this.activeLayer.OverrideScaleRange);
        }

        private void OnOverrideToggleChanged(ChangeEvent<bool> evt)
        {
            if (this.activeLayer == null || this.layerSO == null) return;

            Undo.RegisterCompleteObjectUndo(this.activeLayer, "Toggle Scale Range Override");
            this.layerSO.Update();

            var prop = this.layerSO.FindProperty("overrideScaleRange");
            if (prop != null)
            {
                prop.boolValue = evt.newValue;
                this.layerSO.ApplyModifiedProperties();
            }

            this.scaleRangeField.SetEnabled(evt.newValue);
            this.MarkLayerDirty();
        }

        private void OnScaleRangeChanged(ChangeEvent<Vector2> evt)
        {
            if (this.activeLayer == null || this.layerSO == null) return;

            Undo.RegisterCompleteObjectUndo(this.activeLayer, "Edit Scale Range Override");
            this.layerSO.Update();

            var prop = this.layerSO.FindProperty("scaleRangeOverride");
            if (prop != null)
            {
                // x = min, y = max; clamp min > 0 as documented in P0.
                Vector2 clamped = new Vector2(Mathf.Max(0.0001f, evt.newValue.x), Mathf.Max(0.0001f, evt.newValue.y));
                prop.vector2Value = clamped;
                this.layerSO.ApplyModifiedProperties();
                // Update the field to show clamped values.
                this.scaleRangeField.SetValueWithoutNotify(clamped);
            }

            this.MarkLayerDirty();
        }

        // ── IMGUI instance drawing (ported verbatim from DrawSelectedInstance) ─

        private void DrawInstanceGUI()
        {
            if (this.activeField == null || this.activeLayer == null) return;

            AuthoredInstancesData? authored = this.activeLayer.AuthoredInstances;
            if (authored == null)
            {
                EditorGUILayout.HelpBox("Layer has no AuthoredInstancesData sub-asset.", MessageType.Error);
                return;
            }

            (_, int layerIdx) = ScatterFieldLookup.FindOwningField(this.activeLayer);

            // Try to get the active InstancePlacementTool to read selection state.
            // If the tool is not active we show a hint.
            var tool = InstancePlacementToolTracker.ActiveTool;
            if (tool == null)
            {
                EditorGUILayout.HelpBox(
                    "Activate the Instance Placement EditorTool in the scene view to select instances.",
                    MessageType.Info);
                return;
            }

            int selectedIdx    = tool.SelectedIndex;
            var multiSelection = tool.MultiSelection;

            // ── Multi-select batch controls ─────────────────────────────────
            if (multiSelection.Count > 1)
            {
                this.DrawBatchControls(authored, this.activeField, layerIdx, multiSelection, tool);
                return;
            }

            // ── Single-instance controls (verbatim port of DrawSelectedInstance) ─
            if (selectedIdx < 0 || !authored.TryGetRecord(selectedIdx, out InstanceRecord rec))
            {
                GUILayout.Label("Click an instance to select.", EditorStyles.miniLabel);
                return;
            }

            GUILayout.Label($"Instance #{selectedIdx}", EditorStyles.boldLabel);

            bool configured = (rec.overrideMask & InstanceOverrideMask.ColliderConfigured) != 0;
            bool generate   = configured && rec.generateCollider;

            EditorGUI.BeginChangeCheck();
            bool newGenerate = EditorGUILayout.Toggle("Generate Collider", generate);
            bool newConvex   = EditorGUILayout.Toggle("Convex", rec.colliderConvex);
            float newScale   = EditorGUILayout.FloatField("Collider Scale", rec.colliderScale <= 0f ? 1f : rec.colliderScale);

            var curMesh = authored.GetObjectRef(rec.colliderMeshRefIndex) as Mesh;
            var newMesh = (Mesh?)EditorGUILayout.ObjectField("Mesh Override", curMesh, typeof(Mesh), false);

            var curMat = authored.GetObjectRef(rec.colliderMaterialRefIndex) as PhysicsMaterial;
            var newMat = (PhysicsMaterial?)EditorGUILayout.ObjectField("PhysicsMaterial", curMat, typeof(PhysicsMaterial), false);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(authored, "Edit Instance Collider");
                if (newGenerate || newMesh != null || newMat != null)
                {
                    int meshRef = newMesh != null ? authored.EnsureObjectRef(newMesh) : -1;
                    int matRef  = newMat  != null ? authored.EnsureObjectRef(newMat)  : -1;
                    authored.SetColliderConfig(selectedIdx, newGenerate, newConvex, newScale, meshRef, matRef);
                }
                else
                {
                    authored.ClearColliderConfig(selectedIdx);
                }

                authored.PackBlob();
                EditorUtility.SetDirty(authored);
                if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(this.activeField, layerIdx);
            }
        }

        // ── Batch controls ─────────────────────────────────────────────────────

        private void DrawBatchControls(
            AuthoredInstancesData authored,
            ScatterField field,
            int layerIdx,
            IReadOnlyCollection<int> selection,
            InstancePlacementTool tool)
        {
            GUILayout.Label($"Batch edit ({selection.Count} instances)", EditorStyles.boldLabel);

            // Batch: generate collider toggle
            EditorGUI.BeginChangeCheck();
            bool batchGenerate = EditorGUILayout.Toggle("Generate Collider (all)", false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(authored, "Batch Edit Instance Colliders");
                foreach (int idx in selection)
                {
                    if (!authored.TryGetRecord(idx, out InstanceRecord r)) continue;
                    if (batchGenerate)
                    {
                        authored.SetColliderConfig(idx, true, r.colliderConvex, r.colliderScale <= 0f ? 1f : r.colliderScale,
                            r.colliderMeshRefIndex, r.colliderMaterialRefIndex);
                    }
                    else
                    {
                        authored.ClearColliderConfig(idx);
                    }
                }
                authored.PackBlob();
                EditorUtility.SetDirty(authored);
                if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
            }

            // Batch: uniform scale
            EditorGUI.BeginChangeCheck();
            float batchScale = EditorGUILayout.FloatField("Uniform Scale", 1f);
            if (EditorGUI.EndChangeCheck() && batchScale > 0f)
            {
                Undo.RegisterCompleteObjectUndo(authored, "Batch Scale Instances");
                foreach (int idx in selection)
                {
                    if (!authored.TryGetRecord(idx, out InstanceRecord r)) continue;
                    r.scale = batchScale;
                    authored.SetRecord(idx, r);
                }
                authored.PackBlob();
                EditorUtility.SetDirty(authored);
                if (layerIdx >= 0) ScatterRebuildScheduler.MarkDirty(field, layerIdx);
            }

            // Clear selection button
            if (GUILayout.Button("Clear Selection"))
                tool.ClearSelection();
        }

        // ── Dirty / undo helpers ──────────────────────────────────────────────

        private void MarkLayerDirty()
        {
            if (this.activeLayer == null || this.activeField == null) return;
            EditorUtility.SetDirty(this.activeLayer);
            ScatterFieldLookup.MarkDirtyForLayer(this.activeLayer);
        }

        private void OnUndoRedo()
        {
            this.layerSO?.Update();
            this.RefreshScaleOverrideUI();
        }
    }

    // ── InstancePlacementToolTracker ──────────────────────────────────────────

    /// <summary>
    /// Static registry that the active <see cref="InstancePlacementTool"/> registers itself into
    /// so that <see cref="InstancePanel"/> can read selection state without a direct tool reference.
    /// Registration happens via <see cref="UnityEditor.EditorTools.ToolManager"/> callbacks.
    /// </summary>
    internal static class InstancePlacementToolTracker
    {
        // Set by InstancePlacementTool's EditorTool lifecycle (OnActivated / OnWillBeDeactivated).
        // Unity 6's ToolManager exposes activeToolType (a Type) but no public active-tool instance,
        // so the tool registers itself here for the panel to read selection state.
        internal static InstancePlacementTool? ActiveTool { get; set; }
    }
}
