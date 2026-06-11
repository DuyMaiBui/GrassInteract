#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>Layer type discriminant for WorldPainter stack rows.</summary>
    public enum LayerType { Height, Splat, Grass, Props }

    /// <summary>
    /// Photoshop-style layer stack bound to the SERIALIZED <see cref="WorldPainter"/>
    /// data: splatLayers + scatterLayers via <see cref="SerializedProperty"/>.
    ///
    /// Height (base) row is SYNTHETIC (not a list entry).
    /// Splat / Grass / Prop rows map 1-to-1 to the real schema lists so Add, Remove, and
    /// drag-reorder all persist through <see cref="SerializedObject.ApplyModifiedProperties"/>.
    ///
    /// Mutations are wrapped in <c>Undo.RecordObject</c> so Ctrl+Z works in the Inspector.
    /// Design §4.1.
    /// </summary>
    internal sealed class WorldPainterLayerStackView
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter painter;
        private readonly WorldPainterFilterChips chips;

        // ── Cached SerializedProperty refs ────────────────────────────────────

        private SerializedProperty splatLayersProp = null!;
        private SerializedProperty scatterLayersProp = null!;

        // ── UI ────────────────────────────────────────────────────────────────

        private VisualElement? stackContainer;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterLayerStackView(
            SerializedObject so,
            WorldPainter painter,
            WorldPainterFilterChips chips)
        {
            this.serializedObject  = so;
            this.painter           = painter;
            this.chips             = chips;

            this.splatLayersProp   = so.FindProperty("splatLayers")!;
            this.scatterLayersProp = so.FindProperty("scatterLayers")!;

            chips.FilterChanged += _ => this.RefreshStack();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("wp-stack-root");

            // Header: label + add button
            var header = new VisualElement();
            header.AddToClassList("wp-stack-header");
            var title = new Label("LAYERS");
            title.AddToClassList("wp-stack-title");
            header.Add(title);

            var addBtn = new Button(this.ShowAddMenu) { text = "+ ▾" };
            addBtn.AddToClassList("wp-add-btn");
            header.Add(addBtn);
            root.Add(header);

            // Stack container
            this.stackContainer = new VisualElement();
            this.stackContainer.AddToClassList("wp-stack-list");
            root.Add(this.stackContainer);

            this.RefreshStack();
            return root;
        }

        // ── Stack rendering ───────────────────────────────────────────────────

        private void RefreshStack()
        {
            if (this.stackContainer == null) return;

            this.serializedObject.Update();
            this.stackContainer.Clear();

            int displayIndex = 0;

            // Synthetic Height (base) row — always first, always locked.
            if (this.chips.Passes(LayerType.Height))
            {
                this.stackContainer.Add(this.BuildSyntheticRow(
                    displayIndex++,
                    LayerType.Height,
                    "Height (base)",
                    locked: true));
            }

            // Splat rows — bound to splatLayers list.
            for (int i = 0; i < this.splatLayersProp.arraySize; i++)
            {
                if (!this.chips.Passes(LayerType.Splat)) continue;
                var elem = this.splatLayersProp.GetArrayElementAtIndex(i);
                string name = elem.FindPropertyRelative("name")?.stringValue ?? $"Splat {i}";
                int captured = i;
                this.stackContainer.Add(
                    this.BuildSerializedRow(displayIndex++, LayerType.Splat, name,
                        onRemove: () => this.RemoveSplatLayer(captured)));
            }

            // Scatter (Grass/Props) rows — bound to scatterLayers list.
            for (int i = 0; i < this.scatterLayersProp.arraySize; i++)
            {
                var elem = this.scatterLayersProp.GetArrayElementAtIndex(i);
                // ScatterLayer is an asset ref — use the asset name.
                string layerName = elem.objectReferenceValue != null
                    ? elem.objectReferenceValue.name
                    : $"Scatter {i}";

                // Approximate type from name for the icon chip.
                LayerType type = layerName.ToLowerInvariant().Contains("prop")
                    ? LayerType.Props
                    : LayerType.Grass;

                if (!this.chips.Passes(type)) continue;
                int captured = i;
                this.stackContainer.Add(
                    this.BuildSerializedRow(displayIndex++, type, layerName,
                        onRemove: () => this.RemoveScatterLayer(captured)));
            }
        }

        // ── Row builders ──────────────────────────────────────────────────────

        /// <summary>Builds the synthetic Height (base) row (not backed by a list entry).</summary>
        private VisualElement BuildSyntheticRow(int displayIdx, LayerType type, string name, bool locked)
        {
            var row = this.CreateBaseRow(displayIdx);
            row.Add(this.MakeEyeToggle(null));
            row.Add(this.MakeLockToggle(locked, null));

            var typeChip = new Label(LayerIcon(type));
            typeChip.AddToClassList("wp-type-chip");
            row.Add(typeChip);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            int captured = displayIdx;
            row.RegisterCallback<ClickEvent>(_ => this.SelectLayer(captured));
            return row;
        }

        /// <summary>Builds a row backed by a real serialized list entry.</summary>
        private VisualElement BuildSerializedRow(
            int displayIdx,
            LayerType type,
            string name,
            System.Action onRemove)
        {
            var row = this.CreateBaseRow(displayIdx);

            // Eye toggle (visibility is editor-only state — not in schema; stored in state).
            row.Add(this.MakeEyeToggle(null));

            // Lock toggle (also editor-only).
            row.Add(this.MakeLockToggle(false, null));

            // Type icon chip.
            var typeChip2 = new Label(LayerIcon(type));
            typeChip2.AddToClassList("wp-type-chip");
            row.Add(typeChip2);

            // Name label.
            var nameLabel2 = new Label(name);
            nameLabel2.AddToClassList("wp-layer-name");
            row.Add(nameLabel2);

            // Remove button.
            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            row.Add(removeBtn);

            int captured = displayIdx;
            row.RegisterCallback<ClickEvent>(_ => this.SelectLayer(captured));

            return row;
        }

        private VisualElement CreateBaseRow(int displayIdx)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");
            if (displayIdx == WorldPainterState.ActiveLayerIndex)
                row.AddToClassList("wp-layer-row--selected");
            return row;
        }

        private Toggle MakeEyeToggle(System.Action<bool>? onChange)
        {
            var eye = new Toggle { value = true };
            eye.AddToClassList("wp-eye-toggle");
            if (onChange != null)
                eye.RegisterValueChangedCallback(e => onChange(e.newValue));
            return eye;
        }

        private Toggle MakeLockToggle(bool locked, System.Action<bool>? onChange)
        {
            var lockBtn = new Toggle { value = !locked, tooltip = "Lock layer" };
            lockBtn.AddToClassList("wp-lock-toggle");
            if (onChange != null)
                lockBtn.RegisterValueChangedCallback(e => onChange(!e.newValue));
            return lockBtn;
        }

        // ── Add menu ──────────────────────────────────────────────────────────

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Splat layer"), false, () => this.AddSplatLayer());
            menu.AddDisabledItem(new GUIContent("Grass layer (P3)"));
            menu.AddDisabledItem(new GUIContent("Props layer (P4)"));
            menu.AddDisabledItem(new GUIContent("Biome (P5)"));
            menu.ShowAsContext();
        }

        // ── Mutations (persisted via SerializedObject) ─────────────────────────

        private void AddSplatLayer()
        {
            Undo.RecordObject(this.painter, "Add Splat Layer");

            int newIdx = this.splatLayersProp.arraySize;
            this.splatLayersProp.InsertArrayElementAtIndex(newIdx);

            var elem = this.splatLayersProp.GetArrayElementAtIndex(newIdx);
            var nameProp = elem.FindPropertyRelative("name");
            if (nameProp != null)
                nameProp.stringValue = $"Splat {newIdx}";

            this.serializedObject.ApplyModifiedProperties();
            WorldPainterState.ActiveLayerIndex = 0;
            this.RefreshStack();
        }

        private void RemoveSplatLayer(int index)
        {
            if (index < 0 || index >= this.splatLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Splat Layer");
            this.splatLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        private void RemoveScatterLayer(int index)
        {
            if (index < 0 || index >= this.scatterLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Scatter Layer");
            // Object-reference arrays need element cleared before deletion.
            this.scatterLayersProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            this.scatterLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshStack();
        }

        // ── Layer selection ───────────────────────────────────────────────────

        private void SelectLayer(int index)
        {
            WorldPainterState.ActiveLayerIndex = index;
            this.RefreshStack();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string LayerIcon(LayerType t) => t switch
        {
            LayerType.Height => "⛰",
            LayerType.Splat  => "🎨",
            LayerType.Grass  => "🌿",
            LayerType.Props  => "🌳",
            _                => "?",
        };
    }
}
