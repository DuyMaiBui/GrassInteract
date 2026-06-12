#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Three-section layer palette: SPLAT / MEADOW / PROP.
    ///
    /// Each section has its own header + "+" button and a row of clickable layer chips.
    ///   Splat   — albedo thumbnails via AssetPreview; sets ActiveLayerKind.Splat.
    ///   Meadow  — LOD0 mesh thumbnails via WorldPainterPreviewCache; sets ActiveLayerKind.Meadow.
    ///   Prop    — LOD0 mesh thumbnails via WorldPainterPreviewCache; sets ActiveLayerKind.Prop.
    ///             The prop *placement behavior* is P7; this file ships the list + activation shell.
    ///
    /// Clicking a chip calls <see cref="WorldPainterState.SetActiveLayer"/> and never modifies
    /// <c>UnityEditor.Selection</c> (design §5 contract).
    ///
    /// Design §5 Phase 5 — 3-section palette (core + section partials split for 200-line limit).
    /// </summary>
    internal sealed partial class WorldPainterSplatPaletteView
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject     serializedObject;
        private readonly WorldPainter         painter;
        private readonly WorldPainterPreviewCache previewCache;
        private          SerializedProperty   splatLayersProp;

        // ── UI roots (one per section) ────────────────────────────────────────

        private VisualElement? splatSwatchRow;
        private VisualElement? meadowSwatchRow;
        private VisualElement? propSwatchRow;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterSplatPaletteView(
            SerializedObject so,
            WorldPainter painter,
            WorldPainterPreviewCache previewCache)
        {
            this.serializedObject = so;
            this.painter          = painter;
            this.previewCache     = previewCache;
            this.splatLayersProp  = so.FindProperty("splatLayers")!;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("wp-palette-root");

            root.Add(this.BuildSplatSection());
            root.Add(this.BuildMeadowSection());
            root.Add(this.BuildPropSection());

            return root;
        }

        // ── Refresh all sections (after selection change) ─────────────────────

        private void RefreshAll()
        {
            this.RefreshSplatSwatches();
            this.RefreshMeadowSwatches();
            this.RefreshPropSwatches();
        }

        // ── Shared chip builder ───────────────────────────────────────────────

        private VisualElement BuildChip(
            bool isActive,
            string label,
            Texture2D? thumb,
            System.Action onSelect,
            System.Action onRemove)
        {
            var chip = new VisualElement();
            chip.AddToClassList("wp-palette-chip");
            if (isActive)
                chip.AddToClassList("wp-palette-chip--active");

            var swatch = new VisualElement();
            swatch.AddToClassList("wp-splat-swatch");
            swatch.style.width  = new StyleLength(40f);
            swatch.style.height = new StyleLength(40f);
            if (thumb != null)
                swatch.style.backgroundImage = new StyleBackground(thumb);
            chip.Add(swatch);

            var nameLabel = new Label(label);
            nameLabel.AddToClassList("wp-layer-name");
            chip.Add(nameLabel);

            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            chip.Add(removeBtn);

            chip.RegisterCallback<ClickEvent>(evt =>
            {
                // Only trigger select on direct chip click, not the remove button.
                if (evt.target is Button) return;
                onSelect();
            });

            return chip;
        }

        // ── Section header builder ────────────────────────────────────────────

        private static VisualElement BuildSectionHeader(string title, System.Action onAdd)
        {
            var header = new VisualElement();
            header.AddToClassList("wp-stack-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("wp-stack-title");
            header.Add(titleLabel);

            var addBtn = new Button(onAdd) { text = "+ Layer" };
            addBtn.AddToClassList("wp-add-btn");
            header.Add(addBtn);

            return header;
        }
    }
}
