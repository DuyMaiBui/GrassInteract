#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GrassInteract;

namespace GpuTerrain.Editor
{
    /// <summary>Layer type discriminant for WorldPainter stack rows.</summary>
    public enum LayerType { Height, Splat, Grass, Props }

    /// <summary>
    /// Photoshop-style layer stack for <see cref="WorldPainter"/>. Height row is synthetic;
    /// splat/scatter rows bind to serialized lists. Mutation methods in
    /// <c>WorldPainterLayerStackView.Mutations.cs</c>. Design §4.1.
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter painter;
        private readonly WorldPainterFilterChips chips;
        private readonly WorldPainterPreviewCache previewCache = new();

        // ── Cached SerializedProperty refs ────────────────────────────────────

        private SerializedProperty splatLayersProp = null!;
        private SerializedProperty scatterLayersProp = null!;

        // ── UI ────────────────────────────────────────────────────────────────

        private VisualElement? stackContainer;

        // ── Drag-reorder state ────────────────────────────────────────────────

        private int dragFromIndex = -1;

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

            var header = new VisualElement();
            header.AddToClassList("wp-stack-header");
            var title = new Label("LAYERS");
            title.AddToClassList("wp-stack-title");
            header.Add(title);

            var addBtn = new Button(this.ShowAddMenu) { text = "+ ▾" };
            addBtn.AddToClassList("wp-add-btn");
            header.Add(addBtn);
            root.Add(header);

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
                    displayIndex++, LayerType.Height, "Height (base)", locked: true));
            }

            // Splat rows — bound to splatLayers list; include albedo swatch + drag-reorder.
            for (int i = 0; i < this.splatLayersProp.arraySize; i++)
            {
                if (!this.chips.Passes(LayerType.Splat)) continue;
                var elem  = this.splatLayersProp.GetArrayElementAtIndex(i);
                string rn = elem.FindPropertyRelative("name")?.stringValue ?? $"Splat {i}";
                var albedoProp = elem.FindPropertyRelative("albedo");
                Texture2D? albedo = albedoProp?.objectReferenceValue as Texture2D;
                Texture2D? thumb  = (albedo != null)
                    ? (AssetPreview.GetAssetPreview(albedo) ?? albedo) : null;

                int captured = i;
                var row = this.BuildSerializedRow(
                    displayIndex++, LayerType.Splat, rn,
                    onRemove: () => this.RemoveSplatLayer(captured),
                    albedoPreview: thumb);

                this.RegisterDragReorder(row, captured);
                this.stackContainer.Add(row);
            }

            // Scatter (Grass/Props) rows — bound to scatterLayers list.
            for (int i = 0; i < this.scatterLayersProp.arraySize; i++)
            {
                var elem = this.scatterLayersProp.GetArrayElementAtIndex(i);
                ScatterLayer? scatterLayer = elem.objectReferenceValue as ScatterLayer;
                string layerName = scatterLayer != null ? scatterLayer.name : $"Scatter {i}";

                LayerType type = layerName.ToLowerInvariant().Contains("prop")
                    ? LayerType.Props : LayerType.Grass;

                if (!this.chips.Passes(type)) continue;

                // LOD0 cached 24px thumbnail for collapsed grass row.
                Texture2D? lodThumb = null;
                if (type == LayerType.Grass && scatterLayer != null)
                {
                    Mesh[] lodMeshes = scatterLayer.Render.LodMeshes;
                    Mesh? lod0 = lodMeshes.Length > 0 ? lodMeshes[0] : null;
                    if (lod0 != null)
                        lodThumb = this.previewCache.GetOrRender(
                            scatterLayer.GetInstanceID(), lod0, scatterLayer.Render.Material, 24);
                }

                int captured = i;
                this.stackContainer.Add(
                    this.BuildSerializedRow(displayIndex++, type, layerName,
                        onRemove: () => this.RemoveScatterLayer(captured),
                        albedoPreview: lodThumb));
            }
        }

        // ── Row builders ──────────────────────────────────────────────────────

        private VisualElement BuildSyntheticRow(
            int displayIdx, LayerType type, string name, bool locked)
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

        private VisualElement BuildSerializedRow(
            int displayIdx,
            LayerType type,
            string name,
            System.Action onRemove,
            Texture2D? albedoPreview = null)
        {
            var row = this.CreateBaseRow(displayIdx);

            row.Add(this.MakeEyeToggle(null));
            row.Add(this.MakeLockToggle(false, null));

            var typeChip = new Label(LayerIcon(type));
            typeChip.AddToClassList("wp-type-chip");
            row.Add(typeChip);

            // Albedo/LOD0 swatch chip for Splat and Grass rows.
            if (type == LayerType.Splat || type == LayerType.Grass)
            {
                var swatch = new VisualElement();
                swatch.AddToClassList("wp-splat-swatch");
                if (albedoPreview != null)
                    swatch.style.backgroundImage = new StyleBackground(albedoPreview);
                row.Add(swatch);
            }

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            row.Add(removeBtn);

            int captured = displayIdx;
            row.RegisterCallback<ClickEvent>(_ => this.SelectLayer(captured));
            return row;
        }

        // Row helpers and layer selection live in WorldPainterLayerStackView.Mutations.cs
    }
}
