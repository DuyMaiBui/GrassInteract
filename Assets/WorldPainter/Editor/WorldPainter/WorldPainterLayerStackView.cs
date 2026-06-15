#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>Layer type discriminant for WorldPainter stack rows.</summary>
    public enum LayerType { Height, Splat, Grass, Props }

    /// <summary>
    /// Layer stack for <see cref="WorldPainter"/>. The Height row is synthetic; unified surface
    /// layers (grass/prop) render as large square preview cards bound to <c>map.SurfaceLayers</c>,
    /// mirroring Unity Terrain's paint-layer selector.
    ///
    /// Mutation methods in <c>WorldPainterLayerStackView.Mutations.cs</c>; selection + toggle
    /// factories in <c>WorldPainterLayerStackView.RowHelpers.cs</c>.
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Surface-card sizing ───────────────────────────────────────────────

        private const float CARD_SIZE  = 76f;  // square cell edge (px)
        private const int   CARD_THUMB = 64;    // LOD-0 preview render resolution

        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter painter;
        private readonly WorldPainterPreviewCache previewCache = new();

        // ── UI ────────────────────────────────────────────────────────────────

        private VisualElement? stackContainer;

        // ── Ctor ──────────────────────────────────────────────────────────────

        public WorldPainterLayerStackView(
            SerializedObject so,
            WorldPainter painter)
        {
            this.serializedObject = so;
            this.painter          = painter;
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

            // Explicit create buttons (replaces the old "+ ▾" dropdown menu).
            var addGrassBtn = new Button(this.AddGrassLayerUnified) { text = "+ Grass" };
            addGrassBtn.AddToClassList("wp-add-btn");
            addGrassBtn.tooltip = "Create a new Grass layer (requires a saved World Map).";
            header.Add(addGrassBtn);

            var addPropBtn = new Button(this.AddPropLayerUnified) { text = "+ Props" };
            addPropBtn.AddToClassList("wp-add-btn");
            addPropBtn.tooltip = "Create a new Props layer (requires a saved World Map).";
            header.Add(addPropBtn);

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
            this.stackContainer.Add(this.BuildSyntheticRow(
                displayIndex++, LayerType.Height, "Height (base)", locked: true));

            // Unified surface layers (grass/prop) → grid of large square preview cards.
            WorldMapAsset? map = this.painter.Map;
            if (map == null) return;

            var grid = new VisualElement();
            grid.AddToClassList("wp-card-grid");
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap      = Wrap.Wrap;

            bool anyCard = false;
            var surfaceLayers = map.SurfaceLayers;
            for (int i = 0; i < surfaceLayers.Count; i++)
            {
                var sl = surfaceLayers[i];
                if (sl == null) continue;

                LayerType rowType = sl.Kind switch
                {
                    WorldPainterLayer.LayerKind.Grass => LayerType.Grass,
                    WorldPainterLayer.LayerKind.Prop  => LayerType.Props,
                    _ => LayerType.Grass,
                };

                int captured = i;
                grid.Add(this.BuildSurfaceLayerCard(
                    rowType, sl, onRemove: () => this.RemoveSurfaceLayerAt(captured)));
                anyCard = true;
            }

            if (anyCard) this.stackContainer.Add(grid);
        }

        // ── Row builders ──────────────────────────────────────────────────────

        private VisualElement BuildSyntheticRow(
            int displayIdx, LayerType type, string name, bool locked)
        {
            var row = this.CreateBaseRow(displayIdx);
            row.Add(this.MakeEyeToggle(true, null));
            row.Add(this.MakeLockToggle(locked, null));

            var typeChip = new Label(LayerIcon(type));
            typeChip.AddToClassList("wp-type-chip");
            row.Add(typeChip);

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            // Selecting the Height (terrain) row shows its inline CDLOD LOD editor in the
            // detail card below (WorldPainterInspector) — no popup.
            int captured = displayIdx;
            row.RegisterCallback<ClickEvent>(_ => this.SelectLayer(captured));
            return row;
        }

        /// <summary>
        /// Builds a large square preview card for a unified <see cref="WorldPainterLayer"/> —
        /// LOD-0 thumbnail + name + enable (eye) toggle + remove. Clicking selects the layer for
        /// painting. Mirrors Unity Terrain's paint-texture selector and the brush dock's
        /// TerrainLayer palette strip.
        /// </summary>
        private VisualElement BuildSurfaceLayerCard(
            LayerType type,
            WorldPainterLayer layer,
            System.Action onRemove)
        {
            bool isSelected = IsRowSelected(layer);

            var cell = new VisualElement();
            cell.AddToClassList("wp-layer-card");
            cell.style.width  = CARD_SIZE;
            cell.style.height = CARD_SIZE + 16f;
            cell.style.marginTop = cell.style.marginRight =
                cell.style.marginBottom = cell.style.marginLeft = 3;
            cell.style.borderTopWidth = cell.style.borderRightWidth =
                cell.style.borderBottomWidth = cell.style.borderLeftWidth = 2;

            var border = isSelected
                ? new Color(0.30f, 0.60f, 1.00f)   // selected = blue (matches palette strip)
                : new Color(0.30f, 0.30f, 0.30f);
            cell.style.borderTopColor = cell.style.borderRightColor =
                cell.style.borderBottomColor = cell.style.borderLeftColor =
                new StyleColor(border);

            // Hidden layers (eye off) are dimmed.
            cell.style.opacity = layer.Enabled ? 1f : 0.4f;

            // Large LOD-0 preview (fallback: type icon on a tinted square).
            var preview = new VisualElement();
            preview.style.flexGrow = 1;
            Texture2D? thumb = this.ResolveLod0Thumb(layer, CARD_THUMB);
            if (thumb != null)
            {
                preview.style.backgroundImage = new StyleBackground(thumb);
            }
            else
            {
                preview.style.backgroundColor = new StyleColor(type == LayerType.Props
                    ? new Color(0.10f, 0.55f, 0.45f, 0.5f)
                    : new Color(0.20f, 0.45f, 0.20f, 0.5f));
                var icon = new Label(LayerIcon(type));
                icon.style.flexGrow = 1;
                icon.style.fontSize = 26;
                icon.style.unityTextAlign = TextAnchor.MiddleCenter;
                preview.Add(icon);
            }
            cell.Add(preview);

            // Name strip below the thumbnail.
            var nameLabel = new Label(layer.DisplayName);
            nameLabel.AddToClassList("wp-layer-name");
            nameLabel.style.fontSize = 9;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.height = 14;
            // Override the .wp-layer-name flex-grow:1 from USS so the name stays a thin fixed
            // strip and the preview keeps the rest of the card height.
            nameLabel.style.flexGrow   = 0;
            nameLabel.style.flexShrink = 0;
            cell.Add(nameLabel);

            // Enable (eye) toggle — top-left. Stops propagation so it doesn't also select.
            var eye = this.MakeEyeToggle(layer.Enabled, on => this.SetLayerEnabled(layer, on));
            eye.style.position = Position.Absolute;
            eye.style.top  = 1;
            eye.style.left = 1;
            eye.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            cell.Add(eye);

            // Remove (✕) — top-right.
            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            removeBtn.style.position = Position.Absolute;
            removeBtn.style.top   = 0;
            removeBtn.style.right = 0;
            removeBtn.style.width  = 16;
            removeBtn.style.height = 16;
            removeBtn.style.fontSize = 9;
            removeBtn.tooltip = "Remove this layer";
            cell.Add(removeBtn);

            // Click selects the layer for painting (ignore clicks on the controls).
            cell.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target is Button || evt.target is Toggle) return;
                this.SelectSurfaceLayer(layer);
            });
            return cell;
        }

        /// <summary>
        /// Renders a cached LOD-0 thumbnail for a grass/prop layer, or null when no LOD-0 mesh has
        /// been assigned yet.
        /// </summary>
        private Texture2D? ResolveLod0Thumb(WorldPainterLayer layer, int size)
        {
            Mesh?     mesh = null;
            Material? mat  = null;

            if (layer is GrassLayer grass)
            {
                var lods = grass.Render.Lods;
                if (lods != null && lods.Length > 0) { mesh = lods[0].mesh; mat = grass.Render.Material; }
            }
            else if (layer is PropLayer prop)
            {
                var lods = prop.Render.Lods;
                if (lods != null && lods.Length > 0) { mesh = lods[0].mesh; mat = prop.Render.Material; }
            }

            if (mesh == null) return null;
            return this.previewCache.GetOrRender(layer.GetInstanceID(), mesh, mat, size);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the given <paramref name="layer"/> is the currently active unified layer.
        /// </summary>
        private static bool IsRowSelected(WorldPainterLayer layer)
        {
            if (WorldPainterState.ActiveLayerId != layer.name) return false;
            return WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.None;
        }

        // Mutation methods in WorldPainterLayerStackView.Mutations.cs;
        // selection + toggle factories in WorldPainterLayerStackView.RowHelpers.cs.
    }
}
