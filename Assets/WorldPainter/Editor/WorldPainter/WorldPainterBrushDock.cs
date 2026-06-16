#nullable enable
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Constant brush dock always visible below the layer stack.
    /// Binds size / strength / falloff CurveField / spacing / flow to the
    /// one <see cref="BrushSettings"/> SSOT on <see cref="WorldPainterState"/>.
    /// Position is fixed across layer selection — only the layer stack changes.
    ///
    /// P6 additions:
    ///   - Contextual tool palette (Paint/Erase/Smooth etc. for the active layer kind).
    ///   - Height / Splat / Density mode toggle row (maps to WorldPainterState.PaintLayerKind).
    ///   - F1–F3 preset slots bar + X=swap shortcut label.
    ///
    /// Design §4.4 §5.1.
    /// </summary>
    internal sealed class WorldPainterBrushDock
    {
        // ── Contextual tool palette ───────────────────────────────────────────

        private VisualElement? toolPaletteRoot;
        private VisualElement? brushSettingsContainer;

        // Set Height row — shown only when the Height layer is active (Raise/Lower target).
        private VisualElement? setHeightRow;

        // Brush-mask gallery (thumbnails from the project's Brushes folder) + the drag/drop field.
        private const string BRUSH_FOLDER = "Assets/WorldPainter/Editor/Resources/Brushes";
        private VisualElement? brushGalleryRoot;
        private ObjectField?   brushMaskField;

        // ── Preset slot labels ────────────────────────────────────────────────

        private Label[] presetLabels = new Label[WorldPainterPresetSlots.SLOT_COUNT];

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>Reload hook kept for inspector-activation compatibility (no-op).</summary>
        public void Reload() { }

        /// <summary>Builds and returns the brush dock VisualElement.</summary>
        public VisualElement Build()
        {
            var dock = new VisualElement();
            dock.AddToClassList("wp-brush-dock");

            var title = new Label("BRUSH");
            title.AddToClassList("wp-section-title");
            dock.Add(title);

            // Paint Mode has no manual toggle button — it is driven automatically by layer
            // selection (continuous-stroke layers auto-enable) and tool clicks. The scene-input
            // driver short-circuits when WorldPainterState.PaintModeActive is false.

            var brush = WorldPainterState.Brush;
            brush.shape = BrushShape.Circle; // only the circle brush is exposed (square removed)

            // Phase 2c — TerrainLayer palette strip: 64×64 diffuse thumbnails for the
            // map's TerrainPalette + an "Add TerrainLayer" ObjectField + per-entry "×"
            // remove. Click a thumbnail = active paint ink. Sits ABOVE the contextual
            // tool palette because picking an ink should come before picking Paint/Erase.
            dock.Add(this.BuildPaletteStrip());

            // Contextual tool palette (tools for the active layer kind)
            dock.Add(this.BuildToolPalette());

            // Brush size / strength / set-height / falloff / mask are bundled — hidden for prop
            // layers because Place / Single / Select / Erase don't use a brush footprint (they
            // operate at the cursor). Spacing + Flow were removed: Flow was a no-op for sculpting
            // and Spacing now uses a fixed internal default (strokes stay speed-independent).
            this.brushSettingsContainer = new VisualElement();
            this.brushSettingsContainer.Add(this.BuildSlider("Size (m)", 0.5f, 256f,
                () => brush.size, v => brush.size = v));
            this.brushSettingsContainer.Add(this.BuildSlider("Strength", 0f, 1f,
                () => brush.strength, v => brush.strength = v));

            // Set Height (Raise/Lower target). Only meaningful for the Height layer — its row is
            // shown/hidden by RefreshBrushSettingsVisibility. Raise rises toward it (ceiling),
            // Lower drops toward it (floor).
            this.setHeightRow = this.BuildSlider("Set Height (m)", -20f, 20f,
                () => brush.setHeight, v => brush.setHeight = v);
            this.brushSettingsContainer.Add(this.setHeightRow);

            this.brushSettingsContainer.Add(this.BuildCurveField(brush));

            // Brush mask: a clickable thumbnail gallery from the project's Brushes folder plus a
            // drag/drop field for importing custom masks (no longer drag/drop-only).
            this.brushSettingsContainer.Add(this.BuildBrushGallery());
            this.brushSettingsContainer.Add(this.BuildBrushMaskField());
            dock.Add(this.brushSettingsContainer);

            // P6: preset slots bar
            dock.Add(this.BuildPresetBar());

            // Hook layer changes so the container hides for prop layers and the Set Height row
            // shows only for the Height layer. Both the kind change (ActiveLayerChanged) and the
            // stack-index change (ActiveLayerIndexChanged → Height base row) can flip the answer.
            this.RefreshBrushSettingsVisibility();
            System.Action<string, WorldPainterState.PaintLayerKind> onActive =
                (_, __) => this.RefreshBrushSettingsVisibility();
            System.Action onIndex = this.RefreshBrushSettingsVisibility;
            WorldPainterState.ActiveLayerChanged      += onActive;
            WorldPainterState.ActiveLayerIndexChanged += onIndex;
            dock.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                WorldPainterState.ActiveLayerChanged      -= onActive;
                WorldPainterState.ActiveLayerIndexChanged -= onIndex;
            });

            return dock;
        }

        /// <summary>
        /// Drives context-sensitive dock visibility off the active layer kind:
        /// <list type="bullet">
        /// <item>Brush settings hidden for Prop layers (prop tools act at the cursor, no footprint).</item>
        /// <item>Set Height row shown only for the Height layer.</item>
        /// <item>Terrain palette shown only in the terrain-base context (Height or Splat), hidden
        /// on Grass/Prop layers.</item>
        /// </list>
        /// </summary>
        private void RefreshBrushSettingsVisibility()
        {
            if (this.brushSettingsContainer == null) return;
            bool isProp = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop;
            this.brushSettingsContainer.style.display = isProp ? DisplayStyle.None : DisplayStyle.Flex;

            // EffectiveLayerType ignores its painter argument, so a null ActivePainter is safe.
            LayerType layerType = WorldPainterState.EffectiveLayerType(WorldPainterState.ActivePainter!);

            // Set Height drives only the Height layer's Raise/Lower brushes — hide it elsewhere.
            if (this.setHeightRow != null)
                this.setHeightRow.style.display =
                    layerType == LayerType.Height ? DisplayStyle.Flex : DisplayStyle.None;

            // Terrain palette belongs to the terrain base: show while sculpting the Height layer
            // AND while painting a terrain texture (Splat) — picking a palette ink flips the kind
            // to Splat, so gating on Height|Splat keeps the palette visible through that flow.
            // Hidden on Grass/Prop surface layers (you're not texturing the terrain there).
            if (this.paletteStripContainer != null)
            {
                bool terrainContext = layerType == LayerType.Height || layerType == LayerType.Splat;
                this.paletteStripContainer.style.display =
                    terrainContext ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── TerrainLayer palette strip (Phase 2c) ─────────────────────────────

        private VisualElement? paletteStripRoot;
        // Outer palette container (title + strip) — visibility is gated to the terrain-base
        // editing context (Height / Splat) by RefreshBrushSettingsVisibility.
        private VisualElement? paletteStripContainer;

        /// <summary>
        /// Builds the palette strip container + wires it to repopulate whenever the
        /// WorldMap palette mutates (AddTerrainLayer/RemoveTerrainLayer) or the active
        /// palette index flips. The container repopulates from
        /// <see cref="WorldPainterState.ActivePainter"/>.Map.TerrainPalette on every refresh.
        /// </summary>
        private VisualElement BuildPaletteStrip()
        {
            var container = new VisualElement();
            this.paletteStripContainer = container; // gated to Height/Splat in RefreshBrushSettingsVisibility

            var title = new Label("TERRAIN PALETTE");
            title.AddToClassList("wp-section-title");
            container.Add(title);

            this.paletteStripRoot = new VisualElement();
            this.paletteStripRoot.style.flexDirection = FlexDirection.Row;
            this.paletteStripRoot.style.flexWrap      = Wrap.Wrap;
            this.paletteStripRoot.style.marginBottom  = 4;
            container.Add(this.paletteStripRoot);

            this.PopulatePaletteStrip();

            // Repopulate when the palette changes (Add/Remove) or the active index flips —
            // we don't have a "palette mutated" event yet, so the +/× buttons call
            // PopulatePaletteStrip themselves. ActivePaletteIndex flips here for the
            // highlight to follow programmatic selection (e.g. tool buttons keep paint mode on).
            System.Action<int> onActiveIdx = _ => this.PopulatePaletteStrip();
            WorldPainterState.ActivePaletteIndexChanged += onActiveIdx;
            this.paletteStripRoot.RegisterCallback<DetachFromPanelEvent>(_ =>
                WorldPainterState.ActivePaletteIndexChanged -= onActiveIdx);

            return container;
        }

        private void PopulatePaletteStrip()
        {
            if (this.paletteStripRoot == null) return;
            this.paletteStripRoot.Clear();

            var painter = WorldPainterState.ActivePainter;
            var map     = painter != null ? painter.Map : null;
            if (map == null)
            {
                var hint = new Label("Select a WorldPainter with a Map to author the palette.");
                hint.style.fontSize   = 9;
                hint.style.color      = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                hint.style.marginLeft = 4;
                this.paletteStripRoot.Add(hint);
                return;
            }

            var palette = map.TerrainPalette;
            int active  = WorldPainterState.ActivePaletteIndex;

            for (int i = 0; i < palette.Count; i++)
            {
                int    capturedIdx = i;
                var    terrainLayer = palette[i];

                var cell = new VisualElement();
                cell.style.width  = 72;
                cell.style.height = 72;
                cell.style.marginTop = cell.style.marginRight =
                    cell.style.marginBottom = cell.style.marginLeft = 2;
                cell.style.borderTopWidth = cell.style.borderRightWidth =
                    cell.style.borderBottomWidth = cell.style.borderLeftWidth = 2;

                bool isActive = capturedIdx == active;
                var border = isActive
                    ? new Color(0.30f, 0.60f, 1.00f) // wp-mode-btn--active blue
                    : new Color(0.30f, 0.30f, 0.30f);
                cell.style.borderTopColor = cell.style.borderRightColor =
                    cell.style.borderBottomColor = cell.style.borderLeftColor =
                    new StyleColor(border);

                // Thumbnail = palette[i].diffuseTexture; falls back to a label when null.
                Texture2D? thumb = terrainLayer != null ? terrainLayer.diffuseTexture : null;
                if (thumb != null)
                {
                    var img = new Image { image = thumb };
                    img.style.width  = new StyleLength(StyleKeyword.Auto);
                    img.style.height = new StyleLength(StyleKeyword.Auto);
                    img.style.flexGrow = 1;
                    cell.Add(img);
                }
                else
                {
                    var lbl = new Label($"[{capturedIdx}]\nno diffuse");
                    lbl.style.fontSize = 9;
                    lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                    lbl.style.flexGrow = 1;
                    cell.Add(lbl);
                }

                // Click cell to activate this palette index.
                cell.RegisterCallback<ClickEvent>(_ => this.ActivatePalette(capturedIdx));

                // Tiny "×" remove button in the top-right corner.
                var removeBtn = new Button(() => this.RemovePaletteEntry(capturedIdx)) { text = "×" };
                removeBtn.style.position = Position.Absolute;
                removeBtn.style.top   = 0;
                removeBtn.style.right = 0;
                removeBtn.style.width = 16;
                removeBtn.style.height = 16;
                removeBtn.style.fontSize = 10;
                removeBtn.tooltip = $"Remove '{(terrainLayer != null ? terrainLayer.name : "—")}' from the palette";
                cell.Add(removeBtn);

                this.paletteStripRoot.Add(cell);
            }

            // "+" add affordance — ObjectField inside a 72×72 cell so it lines up with the strip.
            var addCell = new VisualElement();
            addCell.style.width  = 72;
            addCell.style.height = 72;
            addCell.style.marginTop = addCell.style.marginRight =
                addCell.style.marginBottom = addCell.style.marginLeft = 2;
            addCell.style.borderTopWidth = addCell.style.borderRightWidth =
                addCell.style.borderBottomWidth = addCell.style.borderLeftWidth = 1;
            addCell.style.borderTopColor = addCell.style.borderRightColor =
                addCell.style.borderBottomColor = addCell.style.borderLeftColor =
                new StyleColor(new Color(0.5f, 0.5f, 0.5f));

            var addLabel = new Label("+ Add");
            addLabel.style.fontSize = 9;
            addLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            addCell.Add(addLabel);

            var objField = new ObjectField
            {
                objectType         = typeof(UnityEngine.TerrainLayer),
                allowSceneObjects  = false,
            };
            objField.style.flexGrow = 1;
            objField.RegisterValueChangedCallback(evt =>
            {
                var picked = evt.newValue as UnityEngine.TerrainLayer;
                if (picked != null) this.AddPaletteEntry(picked);
                objField.SetValueWithoutNotify(null); // reset so the same asset can be added twice
            });
            addCell.Add(objField);

            this.paletteStripRoot.Add(addCell);
        }

        // ── Palette state transitions ────────────────────────────────────────

        private void ActivatePalette(int index)
        {
            // Picking an ink also means "you're now in splat mode" — flip the active layer
            // kind so the contextual tool palette renders Paint/Erase, and engage paint mode
            // so the brush driver isn't gated.
            WorldPainterState.ActivePaletteIndex = index;
            WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.Splat);
            WorldPainterState.SetActiveBrushTool("palette.paint");
            WorldPainterState.PaintModeActive   = true;
        }

        private void AddPaletteEntry(UnityEngine.TerrainLayer terrainLayer)
        {
            var painter = WorldPainterState.ActivePainter;
            var map     = painter != null ? painter.Map : null;
            if (map == null) return;

            int idx = WorldMapAssetLifecycle.AddTerrainLayer(map, terrainLayer);
            if (idx < 0) return;

            // Auto-select the newly-added layer + rebuild so the alphamap/palette array
            // is reflected on the GPU immediately.
            this.ActivatePalette(idx);
            painter!.TryBuild();
            UnityEditor.SceneView.RepaintAll();

            this.PopulatePaletteStrip();
        }

        private void RemovePaletteEntry(int index)
        {
            var painter = WorldPainterState.ActivePainter;
            var map     = painter != null ? painter.Map : null;
            if (map == null) return;

            // Confirmation — removal is destructive even though we don't trim alphamap data.
            bool confirmed = UnityEditor.EditorUtility.DisplayDialog(
                "Remove TerrainLayer?",
                $"Remove palette entry at index {index}? Existing alphamap weights for that " +
                "channel will be ignored by the shader but stay on disk (cheap re-add later).",
                "Remove", "Cancel");
            if (!confirmed) return;

            WorldMapAssetLifecycle.RemoveTerrainLayer(map, index);

            // Reset selection if we just removed the active ink.
            if (WorldPainterState.ActivePaletteIndex == index)
                WorldPainterState.ActivePaletteIndex = -1;
            else if (WorldPainterState.ActivePaletteIndex > index)
                WorldPainterState.ActivePaletteIndex--;

            painter!.TryBuild();
            UnityEditor.SceneView.RepaintAll();

            this.PopulatePaletteStrip();
        }

        // ── Contextual tool palette (tools per active layer kind) ─────────────

        /// <summary>
        /// Builds the TOOLS row. Its buttons reflect the active layer kind (Height → Raise/Lower/
        /// Smooth/Flatten, Splat → Paint/Erase, Density → Paint/Erase/Smooth, Props → Place/Erase/
        /// Single). Rebuilds when the active layer (stack or palette) or active tool changes.
        /// </summary>
        private VisualElement BuildToolPalette()
        {
            var container = new VisualElement();

            var title = new Label("TOOLS");
            title.AddToClassList("wp-section-title");
            container.Add(title);

            this.toolPaletteRoot = new VisualElement();
            this.toolPaletteRoot.AddToClassList("wp-mode-toggle-row");
            this.toolPaletteRoot.style.flexDirection = FlexDirection.Row;
            this.toolPaletteRoot.style.flexWrap      = Wrap.Wrap;
            this.toolPaletteRoot.style.marginBottom  = 4;
            container.Add(this.toolPaletteRoot);

            this.PopulateToolPalette();

            // Subscribe to all three signals that can change the effective layer kind / tool.
            System.Action onIndex = this.PopulateToolPalette;
            System.Action<string> onTool = _ => this.PopulateToolPalette();
            System.Action<string, WorldPainterState.PaintLayerKind> onLayer =
                (_, __) => this.PopulateToolPalette();

            WorldPainterState.ActiveLayerIndexChanged += onIndex;
            WorldPainterState.ActiveBrushToolChanged  += onTool;
            WorldPainterState.ActiveLayerChanged      += onLayer;

            this.toolPaletteRoot.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                WorldPainterState.ActiveLayerIndexChanged -= onIndex;
                WorldPainterState.ActiveBrushToolChanged  -= onTool;
                WorldPainterState.ActiveLayerChanged      -= onLayer;
            });

            return container;
        }

        private void PopulateToolPalette()
        {
            if (this.toolPaletteRoot == null) return;
            this.toolPaletteRoot.Clear();

            var painter = WorldPainterState.ActivePainter;
            LayerType kind = painter != null
                ? WorldPainterState.EffectiveLayerType(painter)
                : LayerType.Height;

            // Phase 2b: when in splat mode, surface a yellow warning if the user hasn't
            // picked a TerrainLayer from the palette strip above yet — the brush dispatch
            // returns early without one.
            if (kind == LayerType.Splat && WorldPainterState.ActivePaletteIndex < 0)
            {
                var warn = new Label("⚠ Click a TerrainLayer thumbnail above to pick an ink.");
                warn.style.fontSize   = 9;
                warn.style.color      = new StyleColor(new Color(1f, 0.7f, 0.2f));
                warn.style.marginBottom = 2;
                warn.style.flexBasis    = new StyleLength(new Length(100, LengthUnit.Percent));
                this.toolPaletteRoot.Add(warn);
            }

            var tools = BrushToolRegistry.ToolsFor(kind);
            if (tools.Count == 0)
            {
                var hint = new Label("Select a layer to paint");
                hint.style.fontSize   = 9;
                hint.style.color      = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                hint.style.marginLeft = 4;
                this.toolPaletteRoot.Add(hint);
                return;
            }

            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                var btn = new Button(() =>
                {
                    // Toggle behaviour: clicking the already-engaged tool turns the brush OFF
                    // (no stroke, no click-only place/select). Clearing the active tool id to
                    // empty + PaintModeActive=false makes the SculptTool dispatch gate return for
                    // BOTH continuous-stroke and click-only tools (IsClickOnlyTool("") == false).
                    bool clickOnly = WorldPainterState.IsClickOnlyTool(tool.Id);
                    bool engaged   = WorldPainterState.ActiveBrushToolId == tool.Id &&
                                     (clickOnly || WorldPainterState.PaintModeActive);
                    if (engaged)
                    {
                        WorldPainterState.PaintModeActive = false;
                        WorldPainterState.SetActiveBrushTool(string.Empty);
                    }
                    else
                    {
                        // Paint Mode = "I am painting/erasing with a continuous brush stroke."
                        // Click-only prop tools (Place / Single / Select) are NOT brush strokes,
                        // so picking them leaves Paint Mode OFF; picking a continuous-stroke tool
                        // (paint, erase, sculpt, etc.) turns it ON.
                        WorldPainterState.PaintModeActive = !clickOnly;
                        WorldPainterState.SetActiveBrushTool(tool.Id);
                    }
                });
                btn.AddToClassList("wp-mode-btn");
                btn.style.flexGrow = 1;
                btn.style.flexDirection = FlexDirection.Row;
                btn.style.alignItems    = Align.Center;
                btn.style.justifyContent = Justify.Center;

                // Icon (optional — falls back to text-only when no PNG is authored).
                var icon = BrushIcons.For(tool.Id);
                if (icon != null)
                {
                    var img = new Image { image = icon };
                    img.style.width  = 16;
                    img.style.height = 16;
                    img.style.marginRight = 3;
                    btn.Add(img);
                }
                btn.Add(new Label(tool.Label));

                // Highlight only the ENGAGED tool. When the brush is toggled off (active id empty
                // / paint mode off) nothing is highlighted, mirroring the disabled brush state.
                bool toolEngaged = WorldPainterState.ActiveBrushToolId == tool.Id &&
                                   (WorldPainterState.IsClickOnlyTool(tool.Id) || WorldPainterState.PaintModeActive);
                if (toolEngaged)
                    btn.AddToClassList("wp-mode-btn--active");

                this.toolPaletteRoot.Add(btn);
            }
        }

        // ── Preset bar ────────────────────────────────────────────────────────

        private VisualElement BuildPresetBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("wp-preset-bar");

            for (int i = 0; i < WorldPainterPresetSlots.SLOT_COUNT; i++)
            {
                int capturedIdx = i;
                var slot = new VisualElement();
                slot.AddToClassList("wp-preset-slot");
                if (WorldPainterPresetSlots.IsOccupied(i))
                    slot.AddToClassList("wp-preset-slot--occupied");

                var label = new Label(this.PresetSlotLabel(i));
                label.style.fontSize = 10;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                slot.Add(label);
                this.presetLabels[i] = label;

                slot.RegisterCallback<ClickEvent>(ev =>
                {
                    if (ev.shiftKey)
                    {
                        WorldPainterPresetSlots.Save(capturedIdx);
                        this.RefreshPresetLabels(bar);
                    }
                    else
                    {
                        WorldPainterPresetSlots.Recall(capturedIdx);
                    }
                });

                slot.tooltip = $"F{i + 1}: Recall  |  Shift+Click: Save";
                bar.Add(slot);
            }

            // Swap label hint.
            var swapHint = new Label("X=swap");
            swapHint.style.fontSize = 9;
            swapHint.style.marginLeft = 4;
            swapHint.style.alignSelf  = Align.Center;
            bar.Add(swapHint);

            return bar;
        }

        private void RefreshPresetLabels(VisualElement bar)
        {
            for (int i = 0; i < bar.childCount - 1 && i < WorldPainterPresetSlots.SLOT_COUNT; i++)
            {
                var slot = bar[i];
                if (WorldPainterPresetSlots.IsOccupied(i))
                    slot.AddToClassList("wp-preset-slot--occupied");
                else
                    slot.RemoveFromClassList("wp-preset-slot--occupied");

                var lbl = slot.Q<Label>();
                if (lbl != null) lbl.text = this.PresetSlotLabel(i);
            }
        }

        private string PresetSlotLabel(int slotIdx) =>
            WorldPainterPresetSlots.IsOccupied(slotIdx)
                ? $"F{slotIdx + 1}*"
                : $"F{slotIdx + 1}";

        // ── Field builders ────────────────────────────────────────────────────

        private VisualElement BuildSlider(
            string label,
            float min,
            float max,
            System.Func<float> getter,
            System.Action<float> setter)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-field-row");

            var slider = new Slider(label, min, max)
            {
                value = getter(),
            };
            slider.AddToClassList("wp-brush-slider");
            slider.RegisterValueChangedCallback(e => setter(e.newValue));

            row.Add(slider);
            return row;
        }

        // ── Brush mask gallery (pick from the project's Brushes folder) ───────

        /// <summary>
        /// Builds the brush-mask gallery: a "None" (plain falloff) cell plus a clickable thumbnail
        /// for every Texture2D under <see cref="BRUSH_FOLDER"/>. Clicking a cell sets the active
        /// brush mask. Re-highlights whenever the mask changes (incl. via the drag/drop field).
        /// </summary>
        private VisualElement BuildBrushGallery()
        {
            var container = new VisualElement();

            var title = new Label("BRUSH MASK");
            title.AddToClassList("wp-section-title");
            container.Add(title);

            this.brushGalleryRoot = new VisualElement();
            this.brushGalleryRoot.style.flexDirection = FlexDirection.Row;
            this.brushGalleryRoot.style.flexWrap      = Wrap.Wrap;
            this.brushGalleryRoot.style.marginBottom  = 4;
            container.Add(this.brushGalleryRoot);

            this.PopulateBrushGallery();

            // Mask changes raise BrushFalloffDirty (gallery clicks and the drag/drop field both
            // do) — repopulate so the active-cell highlight follows the current mask.
            System.Action onMaskChanged = this.PopulateBrushGallery;
            WorldPainterState.BrushFalloffDirty += onMaskChanged;
            this.brushGalleryRoot.RegisterCallback<DetachFromPanelEvent>(_ =>
                WorldPainterState.BrushFalloffDirty -= onMaskChanged);

            return container;
        }

        private void PopulateBrushGallery()
        {
            if (this.brushGalleryRoot == null) return;
            this.brushGalleryRoot.Clear();

            var current = WorldPainterState.Brush.maskTexture;

            // "None" → plain circle/square falloff (no mask).
            this.brushGalleryRoot.Add(this.BuildBrushCell(null, current == null, "None (plain falloff)"));

            if (AssetDatabase.IsValidFolder(BRUSH_FOLDER))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { BRUSH_FOLDER }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var    tex  = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null)
                        this.brushGalleryRoot.Add(this.BuildBrushCell(tex, tex == current, tex.name));
                }
            }
        }

        private VisualElement BuildBrushCell(Texture2D? tex, bool isActive, string tooltip)
        {
            var cell = new VisualElement();
            cell.style.width = cell.style.height = 40;
            cell.style.marginTop = cell.style.marginRight =
                cell.style.marginBottom = cell.style.marginLeft = 2;
            cell.style.borderTopWidth = cell.style.borderRightWidth =
                cell.style.borderBottomWidth = cell.style.borderLeftWidth = 2;
            var border = isActive
                ? new Color(0.30f, 0.60f, 1.00f)  // active blue
                : new Color(0.30f, 0.30f, 0.30f);
            cell.style.borderTopColor = cell.style.borderRightColor =
                cell.style.borderBottomColor = cell.style.borderLeftColor = new StyleColor(border);
            cell.tooltip = tooltip;

            if (tex != null)
            {
                var img = new Image { image = tex, scaleMode = ScaleMode.ScaleToFit };
                img.style.flexGrow = 1;
                cell.Add(img);
            }
            else
            {
                var lbl = new Label("○");
                lbl.style.fontSize = 18;
                lbl.style.unityTextAlign = TextAnchor.MiddleCenter;
                lbl.style.flexGrow = 1;
                cell.Add(lbl);
            }

            cell.RegisterCallback<ClickEvent>(_ => this.SelectBrushMask(tex));
            return cell;
        }

        private void SelectBrushMask(Texture2D? tex)
        {
            if (tex != null) EnsureBrushMaskReadable(tex);
            WorldPainterState.Brush.maskTexture = tex;
            this.brushMaskField?.SetValueWithoutNotify(tex); // keep the drag/drop field in sync
            WorldPainterState.RaiseBrushFalloffDirty();       // refresh preview + re-highlight gallery
        }

        // ── Brush mask (import brush as texture) ──────────────────────────────

        /// <summary>
        /// ObjectField to import/select an optional grayscale brush-mask texture. When set, the
        /// brush footprint is shaped by the texture (multiplied with the falloff) instead of a
        /// plain circle. The picked texture is forced uncompressed + linear + readable so the
        /// stamp kernel's Load() sampling works on all platforms.
        /// </summary>
        private VisualElement BuildBrushMaskField()
        {
            var row = new VisualElement();
            row.AddToClassList("wp-field-row");

            var field = new ObjectField("Import Mask")
            {
                objectType        = typeof(Texture2D),
                allowSceneObjects = false,
                value             = WorldPainterState.Brush.maskTexture,
                tooltip           = "Import a custom grayscale brush mask. Or pick one from the " +
                                    "gallery above. None = plain falloff. Black = no deposit, white = full.",
            };
            this.brushMaskField = field;
            field.RegisterValueChangedCallback(evt =>
            {
                var tex = evt.newValue as Texture2D;
                if (tex != null) EnsureBrushMaskReadable(tex);
                WorldPainterState.Brush.maskTexture = tex;
                WorldPainterState.RaiseBrushFalloffDirty(); // nudge preview refresh
            });

            row.Add(field);
            return row;
        }

        /// <summary>
        /// Forces an imported brush-mask texture to uncompressed + linear + CPU-readable so the
        /// compute kernel's point Load() works (block-compressed textures are not reliably
        /// Load-able across platforms).
        /// </summary>
        private static void EnsureBrushMaskReadable(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path)) return;
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

            bool dirty = false;
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (!importer.isReadable) { importer.isReadable  = true;  dirty = true; }
            if (importer.sRGBTexture) { importer.sRGBTexture = false; dirty = true; }
            if (dirty) importer.SaveAndReimport();
        }

        private VisualElement BuildCurveField(BrushSettings brush)
        {
            var row = new VisualElement();
            row.AddToClassList("wp-field-row");

            // CurveField is an IMGUI-backed control; wrap in an IMGUIContainer.
            var container = new IMGUIContainer(() =>
            {
                EditorGUI.BeginChangeCheck();
                var newCurve = EditorGUILayout.CurveField("Falloff", brush.falloff);
                if (EditorGUI.EndChangeCheck())
                {
                    brush.falloff = newCurve;
                    // Notify the active sculpt tool to re-upload the 256×1 LUT.
                    WorldPainterState.RaiseBrushFalloffDirty();
                }
            });
            container.AddToClassList("wp-curve-container");

            row.Add(container);
            return row;
        }
    }
}
