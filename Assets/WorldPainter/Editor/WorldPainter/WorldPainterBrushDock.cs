#nullable enable
using System.IO;
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
    ///   - Unified always-visible 72×92 stamp thumbnail strip loaded from
    ///     Assets/WorldPainter/Editor/Resources/Brushes/ (editor-global, not nested in WorldMapAsset).
    ///   - Drag grayscale .png → saved to Resources/Brushes → reloads strip on next activation.
    ///   - Height / Splat / Density mode toggle row (maps to WorldPainterState.PaintLayerKind).
    ///   - F1–F3 preset slots bar + X=swap shortcut label.
    ///
    /// Design §4.4 §5.1.
    /// </summary>
    internal sealed class WorldPainterBrushDock
    {
        // ── Brush Resources path ──────────────────────────────────────────────

        internal const string BRUSHES_RESOURCES_FOLDER = "Assets/WorldPainter/Editor/Resources/Brushes";

        // ── Stamp thumbnails ──────────────────────────────────────────────────

        private Texture2D[] stampTextures = System.Array.Empty<Texture2D>();
        private string[]    stampNames    = System.Array.Empty<string>();
        private int selectedStampIndex;

        // ── Preset slot labels ────────────────────────────────────────────────

        private Label[] presetLabels = new Label[WorldPainterPresetSlots.SLOT_COUNT];

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Called on inspector activation to reload stamp thumbnails from disk.
        /// Editor-global: loads from <see cref="BRUSHES_RESOURCES_FOLDER"/>, not from WorldMapAsset.
        /// </summary>
        public void Reload()
        {
            this.LoadStampTextures();
        }

        /// <summary>Builds and returns the brush dock VisualElement.</summary>
        public VisualElement Build()
        {
            this.LoadStampTextures();

            var dock = new VisualElement();
            dock.AddToClassList("wp-brush-dock");

            var title = new Label("BRUSH");
            title.AddToClassList("wp-section-title");
            dock.Add(title);

            var brush = WorldPainterState.Brush;

            // Height / Splat / Density mode toggle
            dock.Add(this.BuildModeToggle());

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

            // Stamp thumbnail strip (always visible)
            dock.Add(this.BuildStampStrip());

            // P6: preset slots bar
            dock.Add(this.BuildPresetBar());

            return dock;
        }

        // ── Mode toggle (Height / Splat / Density) ────────────────────────────

        private VisualElement BuildModeToggle()
        {
            var row = new VisualElement();
            row.AddToClassList("wp-mode-toggle-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 4;

            var modes = new[] { "Height", "Splat", "Density" };
            var kinds = new[]
            {
                WorldPainterState.PaintLayerKind.None,
                WorldPainterState.PaintLayerKind.Splat,
                WorldPainterState.PaintLayerKind.Meadow,
            };

            for (int i = 0; i < modes.Length; i++)
            {
                int capturedIdx = i;
                var btn = new Button(() => this.OnModeClicked(kinds[capturedIdx]));
                btn.text = modes[i];
                btn.AddToClassList("wp-mode-btn");
                btn.style.flexGrow = 1;

                // Highlight the currently active mode.
                if (WorldPainterState.ActiveLayerKind == kinds[i])
                    btn.AddToClassList("wp-mode-btn--active");

                row.Add(btn);
            }

            return row;
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

        private void OnModeClicked(WorldPainterState.PaintLayerKind kind)
        {
            // Toggle: clicking the already-active mode deselects.
            if (WorldPainterState.ActiveLayerKind == kind)
                WorldPainterState.SetActiveLayer(string.Empty, WorldPainterState.PaintLayerKind.None);
            else
                WorldPainterState.SetActiveLayer(WorldPainterState.ActiveLayerId, kind);
        }

        // ── Stamp thumbnail strip ─────────────────────────────────────────────

        private VisualElement BuildStampStrip()
        {
            var container = new VisualElement();
            container.AddToClassList("wp-stamp-strip-container");

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.marginBottom  = 2;

            var stampTitle = new Label("STAMPS");
            stampTitle.AddToClassList("wp-section-title");
            titleRow.Add(stampTitle);

            // Import button — drag a .png to the Brushes folder via file panel.
            var importBtn = new Button(this.OnImportBrushClicked);
            importBtn.text = "+ Import";
            importBtn.style.fontSize   = 9;
            importBtn.style.marginLeft = 4;
            importBtn.tooltip = "Import a grayscale PNG as a brush stamp";
            titleRow.Add(importBtn);

            container.Add(titleRow);

            var strip = new VisualElement();
            strip.AddToClassList("wp-stamp-strip");
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.flexWrap      = Wrap.Wrap;

            if (this.stampTextures.Length == 0)
            {
                var hint = new Label("No stamps — click Import to add one.");
                hint.style.fontSize   = 9;
                hint.style.color      = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                hint.style.marginLeft = 4;
                strip.Add(hint);
            }
            else
            {
                for (int i = 0; i < this.stampTextures.Length; i++)
                {
                    int capturedIdx = i;
                    var cell = new VisualElement();
                    cell.AddToClassList("wp-stamp-cell");
                    cell.style.width  = 72;
                    cell.style.height = 72;
                    cell.style.marginTop = cell.style.marginRight =
                        cell.style.marginBottom = cell.style.marginLeft = 2;
                    cell.style.borderTopWidth = cell.style.borderRightWidth =
                        cell.style.borderBottomWidth = cell.style.borderLeftWidth = 2;

                    Color borderColor = (i == this.selectedStampIndex)
                        ? new Color(0.2f, 0.5f, 1f)
                        : new Color(0.3f, 0.3f, 0.3f);
                    cell.style.borderTopColor = cell.style.borderRightColor =
                        cell.style.borderBottomColor = cell.style.borderLeftColor =
                        new StyleColor(borderColor);

                    if (this.stampTextures[i] != null)
                    {
                        var img = new Image { image = this.stampTextures[i] };
                        img.style.width  = new StyleLength(StyleKeyword.Auto);
                        img.style.height = new StyleLength(StyleKeyword.Auto);
                        img.style.flexGrow = 1;
                        cell.Add(img);
                    }

                    var nameLbl = new Label(this.stampNames[i]);
                    nameLbl.style.fontSize         = 9;
                    nameLbl.style.unityTextAlign   = TextAnchor.MiddleCenter;
                    nameLbl.style.whiteSpace        = WhiteSpace.Normal;
                    nameLbl.style.overflow          = Overflow.Hidden;
                    cell.Add(nameLbl);

                    cell.RegisterCallback<ClickEvent>(_ =>
                    {
                        this.selectedStampIndex = capturedIdx;
                        this.ApplySelectedStamp();
                    });

                    strip.Add(cell);
                }
            }

            container.Add(strip);
            return container;
        }

        private void ApplySelectedStamp()
        {
            if (this.selectedStampIndex < 0 || this.selectedStampIndex >= this.stampTextures.Length)
                return;
            // Hook: apply the selected stamp texture to the brush falloff (via BrushFalloffDirty).
            // The actual stamp shape is used by the compute shader via the LUT.
            WorldPainterState.RaiseBrushFalloffDirty();
        }

        // ── Stamp import ──────────────────────────────────────────────────────

        private void OnImportBrushClicked()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Brush Stamp",
                Application.dataPath,
                "png,tga,psd");

            if (string.IsNullOrEmpty(path)) return;

            this.ImportBrushFromPath(path);
        }

        /// <summary>
        /// Copies the texture at <paramref name="sourcePath"/> into the brushes resources
        /// folder, imports it as an editor-only asset, and reloads the stamp strip.
        /// </summary>
        internal void ImportBrushFromPath(string sourcePath)
        {
            if (!Directory.Exists(BRUSHES_RESOURCES_FOLDER))
                Directory.CreateDirectory(BRUSHES_RESOURCES_FOLDER);

            string fileName = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(BRUSHES_RESOURCES_FOLDER, fileName).Replace('\\', '/');

            File.Copy(sourcePath, destPath, overwrite: true);
            AssetDatabase.Refresh();

            // Configure import settings: grayscale linear, no mip maps.
            var importer = AssetImporter.GetAtPath(destPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture     = false;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled   = false;
                importer.isReadable      = true;
                importer.textureType     = TextureImporterType.Default;
                importer.wrapMode        = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            this.LoadStampTextures();
        }

        // ── Stamp texture loading ─────────────────────────────────────────────

        private void LoadStampTextures()
        {
            if (!Directory.Exists(BRUSHES_RESOURCES_FOLDER))
            {
                this.stampTextures = System.Array.Empty<Texture2D>();
                this.stampNames    = System.Array.Empty<string>();
                return;
            }

            var guids  = AssetDatabase.FindAssets("t:Texture2D", new[] { BRUSHES_RESOURCES_FOLDER });
            var textures = new System.Collections.Generic.List<Texture2D>(guids.Length);
            var names    = new System.Collections.Generic.List<string>(guids.Length);

            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex != null)
                {
                    textures.Add(tex);
                    names.Add(Path.GetFileNameWithoutExtension(assetPath));
                }
            }

            this.stampTextures = textures.ToArray();
            this.stampNames    = names.ToArray();

            // Clamp selection.
            if (this.selectedStampIndex >= this.stampTextures.Length)
                this.selectedStampIndex = 0;
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
