#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>Layer type discriminant for WorldPainter stack rows.</summary>
    public enum LayerType { Height, Splat, Grass, Props, Biome }

    /// <summary>
    /// Photoshop-style layer stack for <see cref="WorldPainter"/>. Height row is synthetic;
    /// unified surface rows bind to <c>map.SurfaceLayers</c>; biome rows bind to the biomes list.
    ///
    /// Phase 5b: legacy splatLayers/scatterLayers fields and SplatLayerDef removed.
    /// Stack lists <c>map.SurfaceLayers</c> exclusively.
    /// Mutation methods in <c>WorldPainterLayerStackView.Mutations.cs</c>.
    /// </summary>
    internal sealed partial class WorldPainterLayerStackView
    {
        // ── Deps ──────────────────────────────────────────────────────────────

        private readonly SerializedObject serializedObject;
        private readonly WorldPainter painter;
        private readonly WorldPainterFilterChips chips;
        private readonly WorldPainterPreviewCache previewCache = new();

        // ── Cached SerializedProperty refs ───────────────────────────────────

        private SerializedProperty biomesProp = null!;

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

            this.biomesProp        = so.FindProperty("biomes")!;

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

            // ── Unified surface layers (Phase 2 SSOT) ─────────────────────────
            WorldMapAsset? map = this.painter.Map;
            if (map != null)
            {
                var surfaceLayers = map.SurfaceLayers;
                for (int i = 0; i < surfaceLayers.Count; i++)
                {
                    var sl = surfaceLayers[i];
                    if (sl == null) continue;

                    LayerType rowType = sl.Kind switch
                    {
                        WorldPainterLayer.LayerKind.Splat => LayerType.Splat,
                        WorldPainterLayer.LayerKind.Grass => LayerType.Grass,
                        WorldPainterLayer.LayerKind.Prop  => LayerType.Props,
                        _ => LayerType.Splat,
                    };

                    if (!this.chips.Passes(rowType)) continue;

                    // Thumbnail: albedo swatch for Splat, LOD0 preview for Grass/Props.
                    Texture2D? thumb = null;
                    if (sl is SplatLayer splat && splat.LayerSet != null)
                    {
                        var albedos = splat.LayerSet.EditorAlbedos;
                        if (albedos != null && albedos.Count > 0)
                            thumb = AssetPreview.GetAssetPreview(albedos[0]) ?? albedos[0];
                    }
                    else if (sl is GrassLayer grass)
                    {
                        var lods = grass.Render.Lods;
                        if (lods != null && lods.Length > 0 && lods[0].mesh != null)
                            thumb = this.previewCache.GetOrRender(
                                sl.GetInstanceID(), lods[0].mesh!, grass.Render.Material, 24);
                    }

                    int captured = i;
                    var row = this.BuildSurfaceLayerRow(
                        displayIndex++, rowType, sl.DisplayName,
                        sl,
                        onRemove: () => this.RemoveSurfaceLayerAt(captured),
                        albedoPreview: thumb);

                    this.stackContainer.Add(row);

                    // For SplatLayer: show albedo-slot sub-rows for paint-channel selection.
                    if (sl is SplatLayer splatLayerExpanded && this.chips.Passes(LayerType.Splat))
                    {
                        bool isSplatActive =
                            WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Splat &&
                            WorldPainterState.ActiveLayerId == sl.name;

                        if (isSplatActive || IsRowSelected(sl))
                        {
                            var layerSet = splatLayerExpanded.LayerSet;
                            int slotCount = layerSet != null ? layerSet.ActiveLayerCount : 0;
                            // Always show at least one slot row even if the set is empty,
                            // so the user can see the channel and click to activate painting.
                            if (slotCount == 0) slotCount = 1;

                            for (int ci = 0; ci < slotCount; ci++)
                            {
                                bool isActiveChannel = isSplatActive &&
                                    WorldPainterState.ActiveSplatChannel == ci;

                                // Build a display name from the albedo asset, or fall back to "Channel N".
                                string slotName = $"Channel {ci}";
                                if (layerSet != null)
                                {
                                    var albedos = layerSet.EditorAlbedos;
                                    if (ci < albedos.Count && albedos[ci] != null)
                                        slotName = albedos[ci].name;
                                }

                                int capturedCi = ci;
                                var crow = this.BuildVariantRow(
                                    displayIndex++, slotName, isActiveChannel,
                                    onClick: () =>
                                    {
                                        WorldPainterState.ActiveSplatChannel = capturedCi;
                                        WorldPainterState.ActiveGrassVariantIndex = -1;
                                        WorldPainterState.SetActiveLayer(
                                            splatLayerExpanded.name,
                                            WorldPainterState.PaintLayerKind.Splat);
                                        WorldPainterState.SetActiveBrushTool("splat.paint");
                                        this.RefreshStack();
                                    });
                                this.stackContainer.Add(crow);
                            }
                        }
                    }

                    // For GrassLayer: show variant sub-rows for paint-target selection.
                    if (sl is GrassLayer grassLayer && this.chips.Passes(LayerType.Grass))
                    {
                        bool isActiveLayer =
                            WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Meadow &&
                            WorldPainterState.ActiveLayerId == sl.name;

                        if (isActiveLayer || IsRowSelected(sl))
                        {
                            for (int vi = 0; vi < grassLayer.Palette.Count; vi++)
                            {
                                var variant = grassLayer.Palette[vi];
                                bool isActiveVariant = isActiveLayer &&
                                    WorldPainterState.ActiveGrassVariantIndex == vi;

                                string vname = string.IsNullOrEmpty(variant.name)
                                    ? $"Variant {vi}" : variant.name;

                                int capturedVi = vi;
                                var vrow = this.BuildVariantRow(
                                    displayIndex++, vname, isActiveVariant,
                                    onClick: () =>
                                    {
                                        WorldPainterState.ActiveGrassVariantIndex = capturedVi;
                                        WorldPainterState.SetActiveLayer(
                                            grassLayer.name,
                                            WorldPainterState.PaintLayerKind.Meadow);
                                        WorldPainterState.SetActiveBrushTool("density.paint");
                                        this.RefreshStack();
                                    });
                                this.stackContainer.Add(vrow);
                            }
                        }
                    }
                }
            }

            // Biome rows — bound to biomes list (mode-color violet).
            if (this.biomesProp != null)
            {
                for (int i = 0; i < this.biomesProp.arraySize; i++)
                {
                    if (!this.chips.Passes(LayerType.Biome)) continue;
                    var elem     = this.biomesProp.GetArrayElementAtIndex(i);
                    var preset   = elem.objectReferenceValue as BiomePreset;
                    string rn    = preset != null ? preset.name : $"Biome {i}";
                    int capturedBiome = i;

                    Texture2D? thumb = preset != null ? AssetPreview.GetMiniThumbnail(preset) : null;

                    var row = this.BuildBiomeRow(
                        displayIndex++, rn,
                        onSelect: () => this.SelectBiomeLayer(capturedBiome),
                        onRemove: () => this.RemoveBiomeLayer(capturedBiome),
                        albedoPreview: thumb);

                    this.stackContainer.Add(row);
                }
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

        /// <summary>
        /// Builds a row for a unified <see cref="WorldPainterLayer"/> sub-asset.
        /// Clicking selects the layer via <see cref="WorldPainterState.SetActiveLayer"/>.
        /// </summary>
        private VisualElement BuildSurfaceLayerRow(
            int displayIdx,
            LayerType type,
            string name,
            WorldPainterLayer layer,
            System.Action onRemove,
            Texture2D? albedoPreview = null)
        {
            bool isSelected = IsRowSelected(layer);

            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");
            if (isSelected)
                row.AddToClassList("wp-layer-row--selected");

            row.Add(this.MakeEyeToggle(null));
            row.Add(this.MakeLockToggle(false, null));

            var typeChip = new Label(LayerIcon(type));
            typeChip.AddToClassList("wp-type-chip");
            row.Add(typeChip);

            if (type == LayerType.Splat || type == LayerType.Grass || type == LayerType.Props)
            {
                var swatch = new VisualElement();
                swatch.AddToClassList("wp-splat-swatch");
                if (albedoPreview != null)
                    swatch.style.backgroundImage = new StyleBackground(albedoPreview);
                if (type == LayerType.Props && albedoPreview == null)
                    swatch.style.backgroundColor = new StyleColor(new Color(0.1f, 0.6f, 0.5f, 0.7f));
                row.Add(swatch);
            }

            var nameLabel = new Label(name);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            // "Select" button opens the sub-asset in the Inspector.
            var selectBtn = new Button(() => Selection.activeObject = layer) { text = "⚙" };
            selectBtn.AddToClassList("wp-remove-btn");
            selectBtn.tooltip = "Select sub-asset in Inspector";
            row.Add(selectBtn);

            var removeBtn = new Button(onRemove) { text = "✕" };
            removeBtn.AddToClassList("wp-remove-btn");
            row.Add(removeBtn);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                // Don't hijack button clicks.
                if (evt.target is Button) return;
                this.SelectSurfaceLayer(layer);
            });
            return row;
        }

        /// <summary>
        /// Builds a variant sub-row under a GrassLayer. Clicking selects that variant for painting.
        /// </summary>
        private VisualElement BuildVariantRow(
            int displayIdx,
            string variantName,
            bool isActive,
            System.Action onClick)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-layer-row");
            row.style.paddingLeft = new StyleLength(24f); // indent under parent grass row

            if (isActive)
                row.AddToClassList("wp-layer-row--selected");

            var dot   = new Label(isActive ? "●" : "○");
            dot.AddToClassList("wp-type-chip");
            row.Add(dot);

            var nameLabel = new Label(variantName);
            nameLabel.AddToClassList("wp-layer-name");
            row.Add(nameLabel);

            row.RegisterCallback<ClickEvent>(_ => onClick());
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

            if (type == LayerType.Splat || type == LayerType.Grass || type == LayerType.Props)
            {
                var swatch = new VisualElement();
                swatch.AddToClassList("wp-splat-swatch");
                if (albedoPreview != null)
                    swatch.style.backgroundImage = new StyleBackground(albedoPreview);
                if (type == LayerType.Props && albedoPreview == null)
                    swatch.style.backgroundColor = new StyleColor(new Color(0.1f, 0.6f, 0.5f, 0.7f));
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

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the given <paramref name="layer"/> is the currently active unified layer.
        /// </summary>
        private static bool IsRowSelected(WorldPainterLayer layer)
        {
            // A surface layer is selected when its name matches ActiveLayerId AND
            // the kind is a surface-layer kind (Splat, Meadow, or Prop).
            if (WorldPainterState.ActiveLayerId != layer.name) return false;
            return WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.None;
        }

        // Row helpers and layer selection live in WorldPainterLayerStackView.Mutations.cs
    }
}
