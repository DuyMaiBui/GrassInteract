#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>Layer type discriminant for WorldPainter stack rows.</summary>
    public enum LayerType { Height, Splat, Grass, Props }

    /// <summary>Runtime model for one row in the WorldPainter layer stack.</summary>
    public sealed class LayerEntry
    {
        public LayerType Type;
        public string    Name   = string.Empty;
        public bool      Visible = true;
        public bool      Locked  = false;
    }

    /// <summary>
    /// Photoshop-style layer stack: rows with eye/lock controls, drag-to-reorder,
    /// and a + guided-add menu (Height/Splat/Grass/Props; Biome greyed "P5").
    /// Selecting a row updates <see cref="WorldPainterState.ActiveLayerIndex"/>.
    /// Design §4.1 — Height (base) + one Splat row present by default.
    /// </summary>
    internal sealed class WorldPainterLayerStackView
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter painter;
        private readonly WorldPainterFilterChips chips;

        // ── Layer model (editor-side mirror) ──────────────────────────────────

        private readonly List<LayerEntry> layers = new();
        private VisualElement? stackContainer;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterLayerStackView(
            SerializedObject so,
            WorldPainter painter,
            WorldPainterFilterChips chips)
        {
            this.serializedObject = so;
            this.painter = painter;
            this.chips = chips;

            this.InitDefaultLayers();
            chips.FilterChanged += _ => this.RefreshStack();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("wp-stack-root");

            // Header row: label + add button
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

            this.stackContainer.Clear();

            for (int i = 0; i < this.layers.Count; i++)
            {
                var entry = this.layers[i];
                if (!this.chips.Passes(entry.Type)) continue;

                this.stackContainer.Add(this.BuildRow(i, entry));
            }
        }

        private VisualElement BuildRow(int index, LayerEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");

            if (index == WorldPainterState.ActiveLayerIndex)
            {
                row.AddToClassList("wp-layer-row--selected");
            }

            // Eye toggle
            var eye = new Toggle { value = entry.Visible };
            eye.AddToClassList("wp-eye-toggle");
            eye.RegisterValueChangedCallback(e =>
            {
                entry.Visible = e.newValue;
            });
            row.Add(eye);

            // Lock toggle
            var lockBtn = new Toggle { value = !entry.Locked, tooltip = "Lock layer" };
            lockBtn.AddToClassList("wp-lock-toggle");
            lockBtn.RegisterValueChangedCallback(e =>
            {
                entry.Locked = !e.newValue;
            });
            row.Add(lockBtn);

            // Type icon chip
            var chip = new Label(LayerIcon(entry.Type));
            chip.AddToClassList("wp-type-chip");
            row.Add(chip);

            // Name label
            var nameLabel = new Label(entry.Name);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            // Click → select
            int captured = index;
            row.RegisterCallback<ClickEvent>(_ => this.SelectLayer(captured));

            return row;
        }

        // ── Add menu ──────────────────────────────────────────────────────────

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Height"), false, () => this.AddLayer(LayerType.Height, "Height"));
            menu.AddItem(new GUIContent("Splat"),  false, () => this.AddLayer(LayerType.Splat,  "Splat"));
            menu.AddItem(new GUIContent("Grass"),  false, () => this.AddLayer(LayerType.Grass,  "Grass"));
            menu.AddItem(new GUIContent("Props"),  false, () => this.AddLayer(LayerType.Props,  "Props"));
            menu.AddDisabledItem(new GUIContent("Biome (P5)"));
            menu.ShowAsContext();
        }

        private void AddLayer(LayerType type, string name)
        {
            Undo.RecordObject(this.painter, $"Add {name} Layer");
            this.layers.Insert(0, new LayerEntry { Type = type, Name = name });
            WorldPainterState.ActiveLayerIndex = 0;
            this.RefreshStack();
        }

        // ── Layer selection ───────────────────────────────────────────────────

        private void SelectLayer(int index)
        {
            WorldPainterState.ActiveLayerIndex = index;
            this.RefreshStack();
        }

        // ── Default layers ────────────────────────────────────────────────────

        private void InitDefaultLayers()
        {
            this.layers.Clear();
            this.layers.Add(new LayerEntry { Type = LayerType.Splat,  Name = "Splat" });
            this.layers.Add(new LayerEntry { Type = LayerType.Height, Name = "Height (base)", Locked = true });
            WorldPainterState.ActiveLayerIndex = 0;
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
