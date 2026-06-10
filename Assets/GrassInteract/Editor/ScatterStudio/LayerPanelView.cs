#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Manages the center data-bound layer panel inside <see cref="ScatterStudioWindow"/>: renders
    /// <see cref="PropertyField"/> foldout cards (Render, Wind, Deform, Bounds, Placement, and
    /// type-specific sections) for the currently selected <see cref="ScatterLayer"/>.
    ///
    /// Binds a <see cref="SerializedObject"/> for the selected layer and registers a change callback
    /// that routes all edits through <see cref="ScatterFieldLookup.MarkDirtyForLayer"/> for live
    /// re-scatter via the scheduler. Never calls <c>Rebuild()</c> directly.
    /// </summary>
    internal sealed class LayerPanelView
    {
        // ── State ─────────────────────────────────────────────────────────────

        private ScatterLayer? activeLayer;
        private SerializedObject? layerSO;

        // ── Child references ──────────────────────────────────────────────────

        private readonly VisualElement panelContainer;

        // ── Construction ──────────────────────────────────────────────────────

        internal LayerPanelView(VisualElement panelRoot)
        {
            this.panelContainer = panelRoot.Q<VisualElement>("layer-panel");
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Show the property panel for <paramref name="layer"/>. Pass null to clear.</summary>
        internal void ShowLayer(ScatterLayer? layer)
        {
            this.panelContainer.Unbind();
            this.panelContainer.Clear();

            this.activeLayer = layer;
            this.layerSO     = layer != null ? new SerializedObject(layer) : null;

            if (layer == null || this.layerSO == null)
            {
                this.panelContainer.Add(new Label("Select a layer to edit its properties."));
                return;
            }

            this.BuildPropertyCards(layer);
            this.panelContainer.Bind(this.layerSO);

            // Register change tracking for live re-scatter
            this.panelContainer.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                if (this.layerSO == null || this.activeLayer == null) return;
                this.layerSO.ApplyModifiedProperties();
                ScatterFieldLookup.MarkDirtyForLayer(this.activeLayer);
            });
        }

        // ── Property card builder ─────────────────────────────────────────────

        private void BuildPropertyCards(ScatterLayer layer)
        {
            if (this.layerSO == null) return;

            // Layer name label
            var nameLabel = new Label(layer.name);
            nameLabel.AddToClassList("layer-panel-title");
            this.panelContainer.Add(nameLabel);

            // Shared struct property groups — iterate the serialized properties by known names
            this.AddRenderCardWithBar();
            this.AddFoldoutCard("Wind",      "wind");
            this.AddFoldoutCard("Deform",    "deform");
            this.AddFoldoutCard("Bounds",    "bounds");
            this.AddFoldoutCard("Placement", "placement");

            // Type-specific properties
            if (layer is DensityScatterLayer)
            {
                this.AddFoldoutCard("Density", "densityMap", "targetInstances", "fieldBounds",
                    "scaleRange", "seed", "slopeRange", "splatLayerIndex", "splatThreshold",
                    "rotationOffsetEuler", "randomPitchRange", "randomRollRange", "alignToNormal");
            }
            else if (layer is InstanceScatterLayer)
            {
                this.AddFoldoutCard("Tilt", "tilt");
                this.AddFoldoutCard("Anchor", "anchorOffsetLocal");
                this.AddFoldoutCard("Instance", "authoredInstances",
                    "overrideScaleRange", "scaleRangeOverride");
                this.AddFoldoutCard("Collider", "defaultColliderMesh", "defaultColliderConvex",
                    "defaultColliderMaterial", "defaultColliderScale",
                    "poolColliders", "poolCap", "cullColliders", "cullDistance");
            }
        }

        /// <summary>
        /// Creates a foldout card containing a <see cref="PropertyField"/> for each of the named
        /// serialized properties. The first <paramref name="propertyNames"/> entry that corresponds
        /// to a compound struct (e.g. "render") is added as a single PropertyField which Unity
        /// expands automatically.
        /// </summary>
        private void AddFoldoutCard(string title, params string[] propertyNames)
        {
            if (this.layerSO == null) return;

            var foldout = new Foldout();
            foldout.text = title;
            foldout.value = true;
            foldout.AddToClassList("layer-panel-card");

            bool anyAdded = false;
            foreach (string propName in propertyNames)
            {
                var prop = this.layerSO.FindProperty(propName);
                if (prop == null) continue;

                var field = new PropertyField(prop);
                field.AddToClassList("layer-panel-field");
                foldout.Add(field);
                anyAdded = true;
            }

            if (anyAdded)
                this.panelContainer.Add(foldout);
        }

        /// <summary>
        /// Dedicated Render foldout: PropertyField for the "render" struct + an IMGUIContainer
        /// that hosts the <see cref="LodDistanceBar"/> below the numeric fields.
        /// Kept separate from <see cref="AddFoldoutCard"/> so the generic helper stays unchanged.
        /// </summary>
        private void AddRenderCardWithBar()
        {
            if (this.layerSO == null) return;

            SerializedProperty? renderProp = this.layerSO.FindProperty("render");
            if (renderProp == null) return;

            var foldout = new Foldout();
            foldout.text  = "Render";
            foldout.value = true;
            foldout.AddToClassList("layer-panel-card");

            var propField = new PropertyField(renderProp);
            propField.AddToClassList("layer-panel-field");
            foldout.Add(propField);

            // Capture the property path — SerializedObject may be rebound by Unity Bind.
            // Re-find the property inside the lambda so it stays valid after rebind.
            string renderPath = renderProp.propertyPath;
            SerializedObject so = this.layerSO;

            var barContainer = new IMGUIContainer(() =>
            {
                so.Update();
                SerializedProperty? rp = so.FindProperty(renderPath);
                if (rp == null) return;

                Rect r = GUILayoutUtility.GetRect(0f, LodDistanceBar.BAR_HEIGHT,
                    GUILayout.ExpandWidth(true));
                LodDistanceBar.Draw(r, rp);
            });

            foldout.Add(barContainer);
            this.panelContainer.Add(foldout);
        }
    }
}
