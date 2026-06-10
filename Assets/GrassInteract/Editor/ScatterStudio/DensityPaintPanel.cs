#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;

namespace GrassInteract.Editor
{
    /// <summary>
    /// In-window density-paint controls panel. Attaches to the <c>#density-paint-panel</c>
    /// <see cref="VisualElement"/> reserved by Phase 1.B in <c>ScatterStudio.uxml</c>.
    ///
    /// Controls provided:
    /// <list type="bullet">
    ///   <item>Icon-based Paint / Erase / Smooth mode toggle group bound to
    ///         <see cref="ScatterAuthoringState.I"/>.<see cref="ScatterAuthoringState.PaintMode"/>.</item>
    ///   <item>Size, Opacity, Falloff, and Flow sliders bound to <see cref="ScatterAuthoringState.I"/>.</item>
    ///   <item>Overlay-visible toggle bound to
    ///         <see cref="ScatterAuthoringState.I"/>.<see cref="ScatterAuthoringState.OverlayVisible"/>.</item>
    ///   <item>A small live heatmap <see cref="Image"/> thumbnail of the active density map.</item>
    ///   <item>A "Paint" entry button that activates <see cref="DensityPaintTool"/> via
    ///         <see cref="ToolManager.SetActiveTool{T}"/>.</item>
    /// </list>
    ///
    /// The orchestrator (<see cref="ScatterStudioWindow"/>) constructs this class and calls
    /// <see cref="Bind"/> when the selected layer changes.
    /// </summary>
    internal sealed class DensityPaintPanel
    {
        // ── Root ──────────────────────────────────────────────────────────────

        private readonly VisualElement root;

        // ── State ─────────────────────────────────────────────────────────────

        private DensityScatterLayer? activeLayer;
        private ScatterField?        activeField;

        // ── Thumbnail ─────────────────────────────────────────────────────────

        private readonly Image thumbnailImage;

        // ── Thumbnail size ────────────────────────────────────────────────────

        private const float THUMBNAIL_SIZE = 96f;

        // ── Mode button labels (fallback text used when icon lookup fails) ─────

        private static readonly string[] MODE_LABELS  = { "Paint", "Erase", "Smooth" };
        private static readonly string[] ICON_NAMES   = { "TerrainInspector.TerrainToolSplat", "TerrainInspector.TerrainToolSplatAlt", "TerrainInspector.TerrainToolSmoothHeight" };

        // ── Mode toggle buttons ───────────────────────────────────────────────

        private readonly Button[] modeButtons = new Button[3];

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Constructs the panel and attaches all controls to <paramref name="mountPoint"/>.
        /// The mount point is the <c>#density-paint-panel</c> VisualElement from the uxml.
        /// </summary>
        internal DensityPaintPanel(VisualElement mountPoint)
        {
            this.root = mountPoint;

            // ── Section title ─────────────────────────────────────────────────
            var title = new Label("Density Paint");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop    = 8f;
            title.style.marginBottom = 4f;
            title.style.marginLeft   = 4f;
            this.root.Add(title);

            // ── Mode toggle group ─────────────────────────────────────────────
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom  = 6f;
            modeRow.style.marginLeft    = 4f;
            modeRow.style.marginRight   = 4f;

            for (int i = 0; i < 3; ++i)
            {
                int modeIndex = i; // capture for lambda
                var btn = new Button(() => this.SetPaintMode(modeIndex));

                // Try icon; fall back to text label.
                GUIContent icon = EditorGUIUtility.IconContent(ICON_NAMES[i]);
                if (icon.image != null)
                {
                    var img = new Image { image = icon.image as Texture2D };
                    img.style.width  = 20f;
                    img.style.height = 20f;
                    btn.Add(img);
                    btn.tooltip = MODE_LABELS[i];
                }
                else
                {
                    btn.text = MODE_LABELS[i];
                }

                btn.style.flexGrow      = 1f;
                btn.style.marginLeft    = 1f;
                btn.style.marginRight   = 1f;
                btn.style.paddingTop    = 3f;
                btn.style.paddingBottom = 3f;

                this.modeButtons[i] = btn;
                modeRow.Add(btn);
            }
            this.root.Add(modeRow);
            this.RefreshModeButtonStyles();

            // ── Brush sliders ─────────────────────────────────────────────────
            this.root.Add(this.MakeSlider("Size",    0.1f, 50f, ScatterAuthoringState.I.BrushSize,    v => ScatterAuthoringState.I.BrushSize    = v));
            this.root.Add(this.MakeSlider("Opacity", 0f,   1f,  ScatterAuthoringState.I.BrushOpacity, v => ScatterAuthoringState.I.BrushOpacity = v));
            this.root.Add(this.MakeSlider("Falloff", 0f,   1f,  ScatterAuthoringState.I.BrushFalloff, v => ScatterAuthoringState.I.BrushFalloff = v));
            this.root.Add(this.MakeSlider("Flow",    0f,   1f,  ScatterAuthoringState.I.BrushFlow,    v => ScatterAuthoringState.I.BrushFlow    = v));

            // ── Overlay toggle ────────────────────────────────────────────────
            var overlayRow = new VisualElement();
            overlayRow.style.flexDirection = FlexDirection.Row;
            overlayRow.style.alignItems    = Align.Center;
            overlayRow.style.marginTop     = 6f;
            overlayRow.style.marginLeft    = 4f;
            overlayRow.style.marginRight   = 4f;

            var overlayToggle = new Toggle("Show Heatmap Overlay")
            {
                value = ScatterAuthoringState.I.OverlayVisible,
            };
            overlayToggle.RegisterValueChangedCallback(evt =>
            {
                ScatterAuthoringState.I.OverlayVisible = evt.newValue;
            });
            overlayRow.Add(overlayToggle);
            this.root.Add(overlayRow);

            // ── Heatmap thumbnail ─────────────────────────────────────────────
            this.thumbnailImage = new Image();
            this.thumbnailImage.style.width         = THUMBNAIL_SIZE;
            this.thumbnailImage.style.height        = THUMBNAIL_SIZE;
            this.thumbnailImage.style.marginTop     = 6f;
            this.thumbnailImage.style.marginBottom  = 6f;
            this.thumbnailImage.style.marginLeft    = 4f;
            this.thumbnailImage.style.alignSelf     = Align.FlexStart;
            this.thumbnailImage.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            this.thumbnailImage.tooltip = "Active density map thumbnail.\nRefreshes on Bind/selection change.";
            this.root.Add(this.thumbnailImage);

            // ── Paint entry button ────────────────────────────────────────────
            var paintButton = new Button(this.OnActivatePaintTool) { text = "Paint (activate tool)" };
            paintButton.style.marginTop    = 4f;
            paintButton.style.marginBottom = 8f;
            paintButton.style.marginLeft   = 4f;
            paintButton.style.marginRight  = 4f;
            this.root.Add(paintButton);

            // ── Subscribe to undo so mode buttons reflect external changes ─────
            Undo.undoRedoPerformed += this.OnUndoRedo;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Binds (or rebinds) the panel to a scatter field. Pass <c>null</c> to clear.
        /// Refreshes the thumbnail, mode buttons, and updates the overlay binding.
        /// </summary>
        internal void Bind(ScatterField? field)
        {
            this.activeField = field;

            // Find the first DensityScatterLayer in the field (if any) for the thumbnail.
            this.activeLayer = null;
            if (field != null)
            {
                foreach (ScatterLayer layer in field.Layers)
                {
                    if (layer is DensityScatterLayer densityLayer)
                    {
                        this.activeLayer = densityLayer;
                        break;
                    }
                }
            }

            // Keep ScatterAuthoringState in sync so DensityPaintTool (global EditorTool) can read it.
            ScatterAuthoringState.I.ActiveLayer = this.activeLayer;

            // Update the scene-overlay binding.
            ScatterDensityOverlay.SetActiveLayer(this.activeLayer, this.activeField);

            this.RefreshThumbnail();
            this.RefreshModeButtonStyles();
        }

        /// <summary>
        /// Targets a specific density layer (driven by layer-rail selection) so paint activation +
        /// overlay + thumbnail act on the selected layer rather than the field's first density layer.
        /// Non-density selections leave the last bound layer in place (the Paint tab is disabled then).
        /// Also stores the layer in ScatterAuthoringState so the global DensityPaintTool can read it.
        /// </summary>
        internal void BindLayer(DensityScatterLayer? layer)
        {
            if (layer == null) return;
            this.activeLayer = layer;

            // Keep ScatterAuthoringState in sync so DensityPaintTool (a global, unscoped EditorTool)
            // can resolve the active layer from OnToolGUI without depending on this.target.
            ScatterAuthoringState.I.ActiveLayer = layer;

            ScatterDensityOverlay.SetActiveLayer(this.activeLayer, this.activeField);
            this.RefreshThumbnail();
        }

        // ── Mode selection ────────────────────────────────────────────────────

        private void SetPaintMode(int mode)
        {
            ScatterAuthoringState.I.PaintMode = mode;
            this.RefreshModeButtonStyles();
        }

        private void RefreshModeButtonStyles()
        {
            int current = ScatterAuthoringState.I.PaintMode;
            for (int i = 0; i < this.modeButtons.Length; ++i)
            {
                bool active = i == current;
                this.modeButtons[i].style.borderTopWidth    = active ? 2f : 0f;
                this.modeButtons[i].style.borderBottomWidth = active ? 2f : 0f;
                this.modeButtons[i].style.borderLeftWidth   = active ? 2f : 0f;
                this.modeButtons[i].style.borderRightWidth  = active ? 2f : 0f;

                Color borderCol = active ? new Color(0.3f, 0.6f, 1f) : Color.clear;
                this.modeButtons[i].style.borderTopColor    = borderCol;
                this.modeButtons[i].style.borderBottomColor = borderCol;
                this.modeButtons[i].style.borderLeftColor   = borderCol;
                this.modeButtons[i].style.borderRightColor  = borderCol;
            }
        }

        // ── Thumbnail ─────────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the thumbnail image from the active layer's density map.
        ///
        /// Refresh timing: called on <see cref="Bind"/> (selection change) and on undo/redo.
        /// There is no stroke-end hook currently wired from this panel — the thumbnail will
        /// therefore lag by one selection change after a paint stroke. If a post-stroke hook is
        /// added to <see cref="DensityPaintTool"/> or <see cref="ScatterRebuildScheduler"/> in a
        /// later phase, subscribe it here.
        ///
        /// Limitation (noted): the thumbnail does not auto-refresh mid-stroke or immediately
        /// after a stroke ends unless the user changes the layer selection or triggers undo/redo.
        /// The scene-view overlay renders the live density map and is the primary real-time
        /// feedback mechanism.
        /// </summary>
        private void RefreshThumbnail()
        {
            Texture2D? map = this.activeLayer?.DensityMap;
            this.thumbnailImage.image = map;
            this.thumbnailImage.style.display =
                map != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Paint tool activation ─────────────────────────────────────────────

        private void OnActivatePaintTool()
        {
            if (this.activeLayer == null)
            {
                Debug.LogWarning("[Scatter Studio] No DensityScatterLayer to paint — " +
                                 "select or add a density layer first.");
                return;
            }

            // DensityPaintTool is now a global (unscoped) EditorTool — no typeof() target type.
            // A global tool activates unconditionally via SetActiveTool; no Selection manipulation
            // or delayCall is required. The active layer is already stored in ScatterAuthoringState
            // by BindLayer(), so the tool's OnToolGUI can resolve it immediately.
            try
            {
                ToolManager.SetActiveTool<DensityPaintTool>();
            }
            catch (System.InvalidOperationException ex)
            {
                Debug.LogWarning($"[Scatter Studio] Could not activate Density Paint tool: {ex.Message}");
            }
        }

        // ── Undo ──────────────────────────────────────────────────────────────

        private void OnUndoRedo()
        {
            this.RefreshThumbnail();
            this.RefreshModeButtonStyles();
        }

        // ── Slider factory ────────────────────────────────────────────────────

        private VisualElement MakeSlider(
            string label,
            float min,
            float max,
            float initialValue,
            System.Action<float> onChanged)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 2f;
            row.style.marginBottom  = 2f;
            row.style.marginLeft    = 4f;
            row.style.marginRight   = 4f;

            var lbl = new Label(label);
            lbl.style.width    = 56f;
            lbl.style.flexShrink = 0f;
            row.Add(lbl);

            var slider = new Slider(min, max)
            {
                value    = initialValue,
                showInputField = true,
            };
            slider.style.flexGrow = 1f;
            slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            row.Add(slider);

            return row;
        }
    }
}
