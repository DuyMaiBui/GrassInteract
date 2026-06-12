#nullable enable
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.UIElements;

namespace WorldPainter.Editor
{
    /// <summary>
    /// UIToolkit custom inspector for <see cref="WorldPainter"/>.
    /// Applies <c>.pro</c> or <c>.light</c> class to the root VisualElement
    /// based on <see cref="EditorGUIUtility.isProSkin"/>, matching the
    /// Scatter Studio theme convention.
    ///
    /// P6: header mini-map, perf badge, live readout strip, and coach marks
    ///     are mounted in fixed header/footer zones.
    /// </summary>
    [CustomEditor(typeof(WorldPainter))]
    internal sealed class WorldPainterInspector : UnityEditor.Editor
    {
        // ── Stylesheet paths (relative to Assets/) ───────────────────────────

        private const string USS_PRO_PATH =
            "Assets/WorldPainter/Editor/WorldPainter/WorldPainter.uss";

        private const string USS_LIGHT_PATH =
            "Assets/WorldPainter/Editor/WorldPainter/WorldPainterLight.uss";

        // ── Sub-views (created once, reused on repaint) ───────────────────────

        private WorldPainterFilterChips? filterChips;
        private WorldPainterLayerStackView? layerStack;
        private WorldPainterBrushDock? brushDock;

        // ── Grass sub-views ───────────────────────────────────────────────────

        private WorldPainterPreviewCache?    previewCache;
        private WorldPainterLodPreviewPanel? lodPreviewPanel;
        private WorldPainterLodBandRuler?    lodBandRuler;
        private WorldPainterScatterLayerCard? scatterCard;

        // ── Prop sub-views (P4) ───────────────────────────────────────────────

        private WorldPainterPropLayerCard? propCard;

        // ── P6 sub-views ──────────────────────────────────────────────────────

        private WorldPainterMiniMap?         miniMap;
        private WorldPainterPerfBadge?       perfBadge;
        private WorldPainterLiveReadoutStrip? readoutStrip;
        private WorldPainterCoachMarks?       coachMarks;

        // ── CreateInspectorGUI ────────────────────────────────────────────────

        public override VisualElement CreateInspectorGUI()
        {
            var painter = (WorldPainter)this.target;

            // Bind state
            WorldPainterState.ActivePainter = painter;
            WorldPainterAuthoring.ActivePainter = painter;

            // Selecting a paintable layer/brush must enter paint mode: activate the sculpt
            // EditorTool so the brush ring draws and strokes register. The tool's kernel then
            // follows ActiveLayerKind (terrain height/splat vs scatter density/prop).
            WorldPainterState.ActiveLayerChanged -= this.OnActiveLayerChanged;
            WorldPainterState.ActiveLayerChanged += this.OnActiveLayerChanged;

            // Root
            var root = new VisualElement();
            root.AddToClassList("world-painter-root");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "pro" : "light");

            // Load stylesheets
            this.AddStyleSheet(root, USS_PRO_PATH);
            if (!EditorGUIUtility.isProSkin)
            {
                this.AddStyleSheet(root, USS_LIGHT_PATH);
            }

            // ── P6 Header zone: mini-map + perf badge ─────────────────────────
            var headerZone = new VisualElement();
            headerZone.style.flexDirection = FlexDirection.Row;
            headerZone.style.alignItems    = Align.Center;
            headerZone.style.flexShrink    = 0;

            this.miniMap = new WorldPainterMiniMap();
            headerZone.Add(this.miniMap.Build(painter));

            var headerRight = new VisualElement();
            headerRight.style.flexDirection = FlexDirection.Column;
            headerRight.style.flexGrow = 1;

            this.perfBadge = new WorldPainterPerfBadge();
            headerRight.Add(this.perfBadge.Build(painter));

            // Cheat-sheet button
            this.coachMarks = new WorldPainterCoachMarks();
            var cheatBtn = this.coachMarks.BuildCheatSheetButton();
            cheatBtn.style.alignSelf = Align.FlexEnd;
            headerRight.Add(cheatBtn);

            headerZone.Add(headerRight);
            root.Add(headerZone);

            // ── Empty-state coach marks ───────────────────────────────────────
            var emptyTilesState = this.coachMarks.BuildNoTilesEmptyState();
            var emptyStackState = this.coachMarks.BuildEmptyStackState();

            // ── P4: "Create World Map" button (shown only when no map is assigned) ──
            var createMapBtn = new Button(() =>
            {
                WorldMapAssetFactory.CreateAndAssign(painter);
            })
            {
                text    = "Create World Map",
                tooltip = "Creates a new WorldMapAsset with a first tile at (0,0) and assigns it to this WorldPainter.",
            };
            createMapBtn.style.marginTop    = 4;
            createMapBtn.style.marginBottom = 4;
            createMapBtn.style.alignSelf    = Align.FlexStart;

            // Show/hide based on content (200ms poll).
            root.schedule.Execute(() =>
            {
                bool noTiles = painter.Map == null && painter.Tiles.Count == 0;
                bool noLayers = painter.SplatLayers.Count == 0 &&
                                painter.ScatterLayers.Count == 0 &&
                                painter.Biomes.Count == 0;
                bool hasNoMap = painter.Map == null;
                emptyTilesState.style.display = noTiles
                    ? DisplayStyle.Flex : DisplayStyle.None;
                createMapBtn.style.display = hasNoMap
                    ? DisplayStyle.Flex : DisplayStyle.None;
                emptyStackState.style.display = (!noTiles && noLayers)
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(200);

            root.Add(emptyTilesState);
            root.Add(createMapBtn);
            root.Add(emptyStackState);

            // ── Filter chips (All / Height / Splat / Grass / Props) ───────────
            this.filterChips = new WorldPainterFilterChips();
            root.Add(this.filterChips.Build());

            // ── Layer stack ───────────────────────────────────────────────────
            this.layerStack = new WorldPainterLayerStackView(
                this.serializedObject,
                painter,
                this.filterChips);
            root.Add(this.layerStack.Build());

            // ── Per-layer coach-mark zone ─────────────────────────────────────
            root.Add(this.coachMarks.BuildLayerCoachArea(painter));

            // Grass sub-views (created once; shown when a Grass layer is active).
            this.previewCache   = new WorldPainterPreviewCache();
            this.lodPreviewPanel = new WorldPainterLodPreviewPanel();
            this.lodBandRuler   = new WorldPainterLodBandRuler(this.previewCache);
            this.scatterCard    = new WorldPainterScatterLayerCard(
                this.lodPreviewPanel, this.lodBandRuler, this.previewCache);
            this.propCard = new WorldPainterPropLayerCard();

            // Scatter layer card area — refreshed when active layer index changes.
            var cardArea = new VisualElement();
            cardArea.AddToClassList("wp-splat-card");
            root.Add(cardArea);

            root.schedule.Execute(() =>
            {
                LayerType layerType = WorldPainterState.ActiveLayerType(painter, out _);
                cardArea.Clear();

                if (layerType == LayerType.Grass)
                {
                    int si = WorldPainterState.ActiveScatterIndex(painter);
                    if (si >= 0 && this.scatterCard != null)
                    {
                        var card = this.scatterCard.Build(painter, si);
                        if (card != null) cardArea.Add(card);
                    }
                }
                else if (layerType == LayerType.Props)
                {
                    int si = WorldPainterState.ActiveScatterIndex(painter);
                    if (si >= 0 && this.propCard != null)
                    {
                        var card = this.propCard.Build(painter, si);
                        if (card != null) cardArea.Add(card);
                    }
                }
            }).Every(200);

            // Brush dock (constant — never moves between layer selections)
            this.brushDock = new WorldPainterBrushDock();
            root.Add(this.brushDock.Build());

            // ── P6 Footer zone: live readout strip ────────────────────────────
            this.readoutStrip = new WorldPainterLiveReadoutStrip();
            root.Add(this.readoutStrip.Build(painter));

            return root;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void OnDisable()
        {
            WorldPainterState.ActiveLayerChanged -= this.OnActiveLayerChanged;

            if (WorldPainterState.ActivePainter == (WorldPainter)this.target)
            {
                WorldPainterState.ActivePainter   = null;
                WorldPainterAuthoring.ActivePainter = null;
                WorldPainterState.ResetLastStroked();
            }

            this.lodPreviewPanel?.Cleanup();
            this.previewCache?.Cleanup();
        }

        // ── Paint-mode activation ─────────────────────────────────────────────

        /// <summary>
        /// Enters/exits paint mode when the active paint layer changes. Selecting a layer
        /// (kind != None) activates the <see cref="WorldPainterSculptTool"/> so the brush
        /// displays and painting works; deselecting (None) restores the previous tool.
        /// </summary>
        private void OnActiveLayerChanged(string layerId, WorldPainterState.PaintLayerKind kind)
        {
            if (kind != WorldPainterState.PaintLayerKind.None)
            {
                if (ToolManager.activeToolType != typeof(WorldPainterSculptTool))
                    ToolManager.SetActiveTool(typeof(WorldPainterSculptTool));
            }
            else if (ToolManager.activeToolType == typeof(WorldPainterSculptTool))
            {
                ToolManager.RestorePreviousTool();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AddStyleSheet(VisualElement root, string path)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet != null)
            {
                root.styleSheets.Add(sheet);
            }
            else
            {
                Debug.LogWarning($"[WorldPainter] USS not found: {path}");
            }
        }
    }
}
