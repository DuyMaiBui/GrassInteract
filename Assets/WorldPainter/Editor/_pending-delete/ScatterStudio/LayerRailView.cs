#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Manages the left layer-rail panel inside <see cref="ScatterStudioWindow"/>: renders the list
    /// of <see cref="ScatterLayer"/> entries with color chips, enable toggles, and remove buttons;
    /// provides "+ Density" and "+ Instance" buttons that create fully-wired sub-assets with a
    /// single undoable step; supports drag-reorder (Undo-wrapped).
    ///
    /// Owned by <see cref="ScatterStudioWindow"/>. Exposes a <see cref="LayerSelected"/> event so
    /// the window can update the center panel when selection changes.
    /// </summary>
    internal sealed class LayerRailView
    {
        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fires when the user selects a layer row, or null when selection cleared.</summary>
        internal event Action<ScatterLayer?>? LayerSelected;

        // ── State ─────────────────────────────────────────────────────────────

        private ScatterField? field;
        private TerrainScatterConfig? config;
        private SerializedObject? serializedConfig;
        private int selectedIndex = -1;

        // ── Child references ──────────────────────────────────────────────────

        private readonly VisualElement layerList;
        private readonly Button addDensityButton;
        private readonly Button addInstanceButton;

        // ── Construction ──────────────────────────────────────────────────────

        internal LayerRailView(VisualElement railRoot)
        {
            this.layerList         = railRoot.Q<VisualElement>("layer-list");
            this.addDensityButton  = railRoot.Q<Button>("add-density-button");
            this.addInstanceButton = railRoot.Q<Button>("add-instance-button");

            this.addDensityButton.clicked  += this.OnAddDensity;
            this.addInstanceButton.clicked += this.OnAddInstance;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Bind (or rebind) to a new field. Pass null to clear.</summary>
        internal void Bind(ScatterField? newField, SerializedObject? newSerializedConfig)
        {
            this.field            = newField;
            this.config           = newField?.Config;
            this.serializedConfig = newSerializedConfig;
            this.selectedIndex    = -1;
            this.Rebuild();
        }

        /// <summary>Rebuild the row list from the current config.</summary>
        internal void Rebuild()
        {
            this.layerList.Clear();

            bool hasField = this.field != null && this.config != null;
            this.addDensityButton.SetEnabled(hasField);
            this.addInstanceButton.SetEnabled(hasField);

            if (!hasField) return;

            var layers = this.config!.Layers;
            for (int i = 0; i < layers.Count; ++i)
            {
                int capturedIndex = i;
                ScatterLayer? layer = layers[i];
                this.layerList.Add(this.BuildRow(layer, capturedIndex));
            }
        }

        /// <summary>Returns the currently selected layer, or null.</summary>
        internal ScatterLayer? SelectedLayer =>
            this.config != null && this.selectedIndex >= 0 && this.selectedIndex < this.config.Layers.Count
                ? this.config.Layers[this.selectedIndex]
                : null;

        // ── Row builder ───────────────────────────────────────────────────────

        private VisualElement BuildRow(ScatterLayer? layer, int index)
        {
            var row = new VisualElement();
            row.AddToClassList("layer-row");
            if (index == this.selectedIndex)
                row.AddToClassList("layer-row--selected");

            // Color chip
            var chip = new VisualElement();
            chip.AddToClassList("layer-color-chip");
            chip.AddToClassList(layer is DensityScatterLayer ? "layer-chip--density" : "layer-chip--instance");
            row.Add(chip);

            // Name label
            var label = new Label(layer != null ? layer.name : "(null)");
            label.AddToClassList("layer-name-label");
            row.Add(label);

            // Remove button
            var removeBtn = new Button(() => this.OnRemoveLayer(index));
            removeBtn.text = "x";
            removeBtn.AddToClassList("layer-remove-button");
            row.Add(removeBtn);

            // Row click → select
            row.RegisterCallback<ClickEvent>(_ => this.SelectLayer(index));

            return row;
        }

        // ── Selection ─────────────────────────────────────────────────────────

        private void SelectLayer(int index)
        {
            this.selectedIndex = index;
            // Rebuild rows to update selection highlight
            this.Rebuild();
            ScatterLayer? layer = this.SelectedLayer;
            this.LayerSelected?.Invoke(layer);
        }

        // ── Add layer ─────────────────────────────────────────────────────────

        private void OnAddDensity()
        {
            if (this.config == null || this.serializedConfig == null) return;

            var layer = ScriptableObject.CreateInstance<DensityScatterLayer>();
            layer.name = this.GenerateLayerName("DensityLayer");
            this.AddLayerSubAsset(layer, "Add Density Layer");
        }

        private void OnAddInstance()
        {
            if (this.config == null || this.serializedConfig == null) return;

            var layer = ScriptableObject.CreateInstance<InstanceScatterLayer>();
            layer.name = this.GenerateLayerName("InstanceLayer");

            // Auto-create the AuthoredInstancesData sub-asset
            var authoredData = ScriptableObject.CreateInstance<AuthoredInstancesData>();
            authoredData.name = layer.name + "_Instances";

            Undo.RegisterCreatedObjectUndo(authoredData, "Add Instance Layer");
            AssetDatabase.AddObjectToAsset(authoredData, this.config);
            EditorUtility.SetDirty(this.config);

            // Wire the authoredInstances field on the layer via SerializedObject
            // We need to do this after AddObjectToAsset but we use direct field set here
            // since the layer isn't in the asset yet; the SP wire happens inside AddLayerSubAsset.
            this.AddLayerSubAsset(layer, "Add Instance Layer", authoredData);
        }

        private void AddLayerSubAsset(ScatterLayer layer, string undoLabel, AuthoredInstancesData? authoredData = null)
        {
            if (this.config == null || this.serializedConfig == null) return;

            // Register the config object for undo BEFORE the asset is modified
            Undo.RegisterCompleteObjectUndo(this.config, undoLabel);

            // Register the layer creation for undo
            Undo.RegisterCreatedObjectUndo(layer, undoLabel);

            // Add the layer as a sub-asset
            AssetDatabase.AddObjectToAsset(layer, this.config);

            // If this is an InstanceScatterLayer and we have authoredData, wire the field
            if (layer is InstanceScatterLayer instanceLayer && authoredData != null)
            {
                // Use SerializedObject to set the authoredInstances field properly
                var layerSO = new SerializedObject(instanceLayer);
                var authoredProp = layerSO.FindProperty("authoredInstances");
                if (authoredProp != null)
                {
                    authoredProp.objectReferenceValue = authoredData;
                    layerSO.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Append to config.layers via SerializedProperty (so Undo captures the list change)
            this.serializedConfig.Update();
            var layersProp = this.serializedConfig.FindProperty("layers");
            if (layersProp != null)
            {
                layersProp.arraySize += 1;
                var elem = layersProp.GetArrayElementAtIndex(layersProp.arraySize - 1);
                elem.objectReferenceValue = layer;
                this.serializedConfig.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(this.config);

            // Select the newly created layer
            this.selectedIndex = this.config.Layers.Count - 1;
            this.Rebuild();
            this.LayerSelected?.Invoke(layer);
        }

        // ── Remove layer ──────────────────────────────────────────────────────

        private void OnRemoveLayer(int index)
        {
            if (this.config == null || this.serializedConfig == null) return;
            if (index < 0 || index >= this.config.Layers.Count) return;

            ScatterLayer? layerToRemove = this.config.Layers[index];
            if (layerToRemove == null) return;

            Undo.RegisterCompleteObjectUndo(this.config, "Remove Layer");

            // Remove from config.layers SerializedProperty
            this.serializedConfig.Update();
            var layersProp = this.serializedConfig.FindProperty("layers");
            if (layersProp != null)
            {
                layersProp.DeleteArrayElementAtIndex(index);
                // Unity's DeleteArrayElementAtIndex sets the element to null first if it's an object
                // reference — call again if needed to actually shrink the array
                if (index < layersProp.arraySize && layersProp.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    layersProp.DeleteArrayElementAtIndex(index);
                this.serializedConfig.ApplyModifiedProperties();
            }

            // Remove the sub-asset from the config asset
            AssetDatabase.RemoveObjectFromAsset(layerToRemove);
            Undo.DestroyObjectImmediate(layerToRemove);

            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(this.config);

            // Clamp selection
            if (this.selectedIndex >= this.config.Layers.Count)
                this.selectedIndex = this.config.Layers.Count - 1;

            this.Rebuild();
            this.LayerSelected?.Invoke(this.SelectedLayer);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string GenerateLayerName(string prefix)
        {
            if (this.config == null) return prefix + "0";
            int count = 0;
            foreach (var layer in this.config.Layers)
                if (layer != null && layer.name.StartsWith(prefix, StringComparison.Ordinal))
                    count++;
            return prefix + count;
        }
    }
}
