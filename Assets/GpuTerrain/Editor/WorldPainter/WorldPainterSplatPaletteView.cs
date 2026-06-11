#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Swatch palette row that shows all registered splat layers as clickable
    /// albedo thumbnails.  Add (≤4) and remove buttons maintain the
    /// <see cref="WorldPainter.SplatLayers"/> list via <see cref="SerializedObject"/>
    /// so all mutations go through the Unity Undo system.
    ///
    /// The 5th add is blocked with a surfaced <see cref="Debug.LogError"/> message
    /// (errors-over-fallbacks rule; see WorldPainter.MAX_SPLAT_LAYERS).
    ///
    /// Design §4.1 — Phase 2 palette swatch row.
    /// </summary>
    internal sealed class WorldPainterSplatPaletteView
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter painter;
        private SerializedProperty splatLayersProp;

        // ── UI ────────────────────────────────────────────────────────────────

        private VisualElement? swatchRow;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterSplatPaletteView(SerializedObject so, WorldPainter painter)
        {
            this.serializedObject = so;
            this.painter          = painter;
            this.splatLayersProp  = so.FindProperty("splatLayers")!;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        public VisualElement Build()
        {
            var root = new VisualElement();
            root.AddToClassList("wp-palette-root");

            var header = new VisualElement();
            header.AddToClassList("wp-stack-header");

            var title = new Label("SPLAT PALETTE");
            title.AddToClassList("wp-stack-title");
            header.Add(title);

            var addBtn = new Button(this.TryAddLayer) { text = "+ Layer" };
            addBtn.AddToClassList("wp-add-btn");
            header.Add(addBtn);

            root.Add(header);

            this.swatchRow = new VisualElement();
            this.swatchRow.AddToClassList("wp-palette-swatch-row");
            root.Add(this.swatchRow);

            this.RefreshSwatches();
            return root;
        }

        // ── Swatch rendering ──────────────────────────────────────────────────

        internal void RefreshSwatches()
        {
            if (this.swatchRow == null) return;

            this.serializedObject.Update();
            this.swatchRow.Clear();

            for (int i = 0; i < this.splatLayersProp.arraySize; i++)
            {
                var elem     = this.splatLayersProp.GetArrayElementAtIndex(i);
                var albedoP  = elem.FindPropertyRelative("albedo");
                var nameProp = elem.FindPropertyRelative("name");

                Texture2D? albedo = albedoP?.objectReferenceValue as Texture2D;
                Texture2D? thumb  = (albedo != null)
                    ? (AssetPreview.GetAssetPreview(albedo) ?? albedo)
                    : null;

                string label = nameProp?.stringValue ?? $"S{i}";
                int captured = i;

                var chip = this.BuildSwatchChip(i, label, thumb,
                    onRemove: () => this.RemoveLayer(captured));
                this.swatchRow.Add(chip);
            }
        }

        // ── Swatch chip ───────────────────────────────────────────────────────

        private VisualElement BuildSwatchChip(
            int index,
            string label,
            Texture2D? thumb,
            System.Action onRemove)
        {
            var chip = new VisualElement();
            chip.AddToClassList("wp-palette-chip");
            if (index == WorldPainterState.ActiveLayerIndex - 1)
                chip.AddToClassList("wp-palette-chip--active");

            // Albedo thumbnail
            var swatch = new VisualElement();
            swatch.AddToClassList("wp-splat-swatch");
            swatch.style.width  = new StyleLength(40f);
            swatch.style.height = new StyleLength(40f);
            if (thumb != null)
                swatch.style.backgroundImage = new StyleBackground(thumb);
            chip.Add(swatch);

            // Name label
            var nameLabel = new Label(label);
            nameLabel.AddToClassList("wp-layer-name");
            chip.Add(nameLabel);

            // Remove button
            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            chip.Add(removeBtn);

            // Click to select
            int captured = index;
            chip.RegisterCallback<ClickEvent>(_ =>
            {
                // +1 because display index 0 = Height (synthetic), splat starts at 1
                WorldPainterState.ActiveLayerIndex = captured + 1;
                this.RefreshSwatches();
            });

            return chip;
        }

        // ── Mutations ─────────────────────────────────────────────────────────

        private void TryAddLayer()
        {
            if (this.splatLayersProp.arraySize >= WorldPainter.MAX_SPLAT_LAYERS)
            {
                Debug.LogError(
                    $"[WorldPainter] Cannot add splat layer: RGBA32 splat RT supports " +
                    $"at most {WorldPainter.MAX_SPLAT_LAYERS} layers. Remove one first.");
                return;
            }

            Undo.RecordObject(this.painter, "Add Splat Layer (Palette)");

            int newIdx = this.splatLayersProp.arraySize;
            this.splatLayersProp.InsertArrayElementAtIndex(newIdx);

            var elem     = this.splatLayersProp.GetArrayElementAtIndex(newIdx);
            var nameProp = elem.FindPropertyRelative("name");
            if (nameProp != null)
                nameProp.stringValue = $"Splat {newIdx}";

            this.serializedObject.ApplyModifiedProperties();
            this.RefreshSwatches();
        }

        private void RemoveLayer(int index)
        {
            if (index < 0 || index >= this.splatLayersProp.arraySize) return;

            Undo.RecordObject(this.painter, "Remove Splat Layer (Palette)");
            this.splatLayersProp.DeleteArrayElementAtIndex(index);
            this.serializedObject.ApplyModifiedProperties();

            WorldPainterState.ActiveLayerIndex =
                Mathf.Max(0, WorldPainterState.ActiveLayerIndex - 1);
            this.RefreshSwatches();
        }
    }
}
