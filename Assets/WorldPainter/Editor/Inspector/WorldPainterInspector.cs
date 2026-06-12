#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GpuTerrain.Editor
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

            // Show/hide based on content (200ms poll).
            root.schedule.Execute(() =>
            {
                bool noTiles = painter.Tiles.Count == 0;
                bool noLayers = painter.SplatLayers.Count == 0 &&
                                painter.ScatterLayers.Count == 0 &&
                                painter.Biomes.Count == 0;
                emptyTilesState.style.display = noTiles
                    ? DisplayStyle.Flex : DisplayStyle.None;
                emptyStackState.style.display = (!noTiles && noLayers)
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(200);

            root.Add(emptyTilesState);
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
            if (WorldPainterState.ActivePainter == (WorldPainter)this.target)
            {
                WorldPainterState.ActivePainter   = null;
                WorldPainterAuthoring.ActivePainter = null;
                WorldPainterState.ResetLastStroked();
            }

            this.lodPreviewPanel?.Cleanup();
            this.previewCache?.Cleanup();
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
