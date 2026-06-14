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

            // Paint Mode toggle — replaces the Scene-view toolbar Brush icon. When OFF the
            // scene-input driver short-circuits (no brush ring, no strokes). Auto-flips ON
            // when a paintable layer is selected.
            dock.Add(this.BuildPaintModeToggle());

            var brush = WorldPainterState.Brush;

            // Phase 2c — TerrainLayer palette strip: 64×64 diffuse thumbnails for the
            // map's TerrainPalette + an "Add TerrainLayer" ObjectField + per-entry "×"
            // remove. Click a thumbnail = active paint ink. Sits ABOVE the contextual
            // tool palette because picking an ink should come before picking Paint/Erase.
            dock.Add(this.BuildPaletteStrip());

            // Contextual tool palette (tools for the active layer kind)
            dock.Add(this.BuildToolPalette());

            // Shape toggle (Circle / Square)
            dock.Add(this.BuildShapeToggle());

            // Size
            dock.Add(this.BuildSlider("Size (m)", 0.5f, 256f,
                () => brush.size,
                v  => brush.size = v));

            // Strength
            dock.Add(this.BuildSlider("Strength", 0f, 1f,
                () => brush.strength,
                v  => brush.strength = v));

            // Falloff CurveField
            dock.Add(this.BuildCurveField(brush));

            // Spacing
            dock.Add(this.BuildSlider("Spacing (m)", 0.1f, 64f,
                () => brush.spacing,
                v  => brush.spacing = v));

            // Flow
            dock.Add(this.BuildSlider("Flow", 0f, 1f,
                () => brush.flow,
                v  => brush.flow = v));

            // P6: preset slots bar
            dock.Add(this.BuildPresetBar());

            return dock;
        }

        // ── TerrainLayer palette strip (Phase 2c) ─────────────────────────────

        private VisualElement? paletteStripRoot;

        /// <summary>
        /// Builds the palette strip container + wires it to repopulate whenever the
        /// WorldMap palette mutates (AddTerrainLayer/RemoveTerrainLayer) or the active
        /// palette index flips. The container repopulates from
        /// <see cref="WorldPainterState.ActivePainter"/>.Map.TerrainPalette on every refresh.
        /// </summary>
        private VisualElement BuildPaletteStrip()
        {
            var container = new VisualElement();

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

        // ── Paint Mode toggle (Phase 1 — replaces Scene-view brush toolbar icon) ──

        private VisualElement BuildPaintModeToggle()
        {
            var row = new VisualElement();
            row.AddToClassList("wp-mode-toggle-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;

            var btn = new Button { text = PaintModeLabel() };
            btn.AddToClassList("wp-mode-btn");
            btn.style.flexGrow = 1;
            if (WorldPainterState.PaintModeActive)
                btn.AddToClassList("wp-mode-btn--active");

            btn.clicked += () =>
            {
                WorldPainterState.PaintModeActive = !WorldPainterState.PaintModeActive;
            };

            // Keep the button label + active class in sync with state flips from elsewhere
            // (layer selection auto-engages, tool click engages, programmatic disable in OnDisable).
            System.Action<bool> onPaintMode = on =>
            {
                btn.text = PaintModeLabel();
                if (on) btn.AddToClassList("wp-mode-btn--active");
                else    btn.RemoveFromClassList("wp-mode-btn--active");
            };
            WorldPainterState.PaintModeChanged += onPaintMode;
            btn.RegisterCallback<DetachFromPanelEvent>(_ =>
                WorldPainterState.PaintModeChanged -= onPaintMode);

            row.Add(btn);
            return row;
        }

        private static string PaintModeLabel() =>
            WorldPainterState.PaintModeActive ? "● Paint Mode: ON" : "○ Paint Mode: OFF";

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
                var hint = new Label(kind == LayerType.Biome
                    ? "Biome uses contribution toggles"
                    : "Select a layer to paint");
                hint.style.fontSize   = 9;
                hint.style.color      = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                hint.style.marginLeft = 4;
                this.toolPaletteRoot.Add(hint);
                return;
            }

            var resolved = BrushToolRegistry.ResolveActiveTool(
                kind, WorldPainterState.ActiveBrushToolId);

            for (int i = 0; i < tools.Count; i++)
            {
                var tool = tools[i];
                var btn = new Button(() =>
                {
                    WorldPainterState.SetActiveBrushTool(tool.Id);
                    // Phase 1 — clicking any tool also turns paint mode on. The driver is
                    // permanently subscribed to SceneView.duringSceneGui by the inspector;
                    // this flag is the on/off gate.
                    WorldPainterState.PaintModeActive = true;
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

                if (resolved != null && resolved.Id == tool.Id)
                    btn.AddToClassList("wp-mode-btn--active");

                this.toolPaletteRoot.Add(btn);
            }
        }

        // ── Shape toggle (Circle / Square) ────────────────────────────────────
        private VisualElement BuildShapeToggle()
        {
            var row = new VisualElement();
            row.AddToClassList("wp-mode-toggle-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;

            var shapes = new[] { "Circle", "Square" };
            var values = new[] { BrushShape.Circle, BrushShape.Square };

            for (int i = 0; i < shapes.Length; i++)
            {
                int capturedIdx = i;
                var btn = new Button(() =>
                {
                    WorldPainterState.Brush.shape = values[capturedIdx];
                    WorldPainterState.RaiseBrushFalloffDirty(); // nudge repaint/preview refresh
                });
                btn.text = shapes[i];
                btn.AddToClassList("wp-mode-btn");
                btn.style.flexGrow = 1;

                if (WorldPainterState.Brush.shape == values[i])
                    btn.AddToClassList("wp-mode-btn--active");

                row.Add(btn);
            }

            return row;
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
