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

        // Identity of the layer currently shown in the detail card. The 200ms poll
        // rebuilds the card ONLY when this changes — rebuilding every tick would
        // destroy+recreate the embedded layer Editor 5×/sec (flicker + lost focus).
        private string? lastCardKey;

        // ── Unified surface-layer detail (Phase 2) ────────────────────────────

        // Cached editor for the currently-selected unified surface layer. Destroyed when
        // the selection changes (prevents stale references and re-creation flicker).
        private UnityEditor.Editor? surfaceLayerEditor;

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
                int surfaceLayerCount = painter.Map != null ? painter.Map.SurfaceLayers.Count : 0;
                // Phase 5: SplatLayers/ScatterLayers removed from WorldPainter; use SurfaceLayers.
                bool noLayers = surfaceLayerCount == 0 &&
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

            // ── Brush dock (constant — sits directly under the layer filter) ──
            this.brushDock = new WorldPainterBrushDock();
            root.Add(this.brushDock.Build());

            // ── Layer stack ───────────────────────────────────────────────────
            this.layerStack = new WorldPainterLayerStackView(
                this.serializedObject,
                painter,
                this.filterChips);
            root.Add(this.layerStack.Build());

            // ── Per-layer coach-mark zone ─────────────────────────────────────
            root.Add(this.coachMarks.BuildLayerCoachArea(painter));

            // Scatter layer card area — refreshed when active layer index changes.
            // Mounted at the very bottom (below the footer readout strip) further down.
            var cardArea = new VisualElement();
            cardArea.AddToClassList("wp-splat-card");

            root.schedule.Execute(() =>
            {
                // ── Phase 2: unified surface-layer detail card ────────────────
                // When a unified surface layer is active (ActiveLayerId set, kind != None)
                // embed its default inspector via CreateEditor in an IMGUIContainer.
                // Legacy scatter/splat detail (ActiveLayerId empty) is handled below.
                string unifiedId   = WorldPainterState.ActiveLayerId;
                var    unifiedKind = WorldPainterState.ActiveLayerKind;

                WorldPainterLayer? activeSurfaceLayer = null;
                if (!string.IsNullOrEmpty(unifiedId) && unifiedKind != WorldPainterState.PaintLayerKind.None)
                {
                    WorldMapAsset? map = painter.Map;
                    if (map != null)
                    {
                        foreach (var sl in map.SurfaceLayers)
                        {
                            if (sl != null && sl.name == unifiedId)
                            {
                                activeSurfaceLayer = sl;
                                break;
                            }
                        }
                    }
                }

                // Build a stable key: unified layer id wins; fall back to legacy index key.
                // Phase 5: ScatterLayers/ActiveScatterIndex removed — key on ActiveLayerIndex only.
                string key;
                if (activeSurfaceLayer != null)
                    key = $"surface:{activeSurfaceLayer.GetInstanceID()}";
                else
                {
                    LayerType legacyType = WorldPainterState.ActiveLayerType(painter, out _);
                    int idx = WorldPainterState.ActiveLayerIndex;
                    key = $"{legacyType}:{idx}";
                }

                if (key == this.lastCardKey) return; // unchanged → keep card
                this.lastCardKey = key;

                cardArea.Clear();

                // Destroy the previously cached editor (avoids leaking UnityEditor.Editor instances).
                if (this.surfaceLayerEditor != null)
                {
                    Object.DestroyImmediate(this.surfaceLayerEditor);
                    this.surfaceLayerEditor = null;
                }

                if (activeSurfaceLayer != null)
                {
                    // Unified detail: embed the sub-asset's default inspector in an IMGUIContainer.
                    WorldPainterLayer capturedLayer = activeSurfaceLayer;
                    this.surfaceLayerEditor = UnityEditor.Editor.CreateEditor(capturedLayer);
                    UnityEditor.Editor capturedEditor = this.surfaceLayerEditor;

                    var label = new Label($"[{capturedLayer.Kind}] {capturedLayer.DisplayName}");
                    label.AddToClassList("wp-layer-name");
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.marginTop = 6;
                    cardArea.Add(label);

                    var imgui = new IMGUIContainer(() =>
                    {
                        if (capturedEditor == null || capturedEditor.target == null) return;
                        capturedEditor.OnInspectorGUI();
                    });
                    cardArea.Add(imgui);
                }
                // Legacy scatter/prop cards removed (Phase 5 SSOT consolidation).
                // Unified surface-layer detail (activeSurfaceLayer != null branch above) is the
                // sole authoring path; no fallback card needed for legacy layer types.
            }).Every(200);

            // ── P6 Footer zone: live readout strip ────────────────────────────
            this.readoutStrip = new WorldPainterLiveReadoutStrip();
            root.Add(this.readoutStrip.Build(painter));

            // ── Active-layer detail card (bottom-most) ────────────────────────
            root.Add(cardArea);

            return root;
        }

        // ── Cleanup ───────────────────────────────────────────────────────────

        private void OnDisable()
        {
            WorldPainterState.ActiveLayerChanged -= this.OnActiveLayerChanged;

            // Destroy the cached surface-layer editor to avoid leaking UnityEditor.Editor instances.
            if (this.surfaceLayerEditor != null)
            {
                Object.DestroyImmediate(this.surfaceLayerEditor);
                this.surfaceLayerEditor = null;
            }

            if (WorldPainterState.ActivePainter == (WorldPainter)this.target)
            {
                WorldPainterState.ActivePainter   = null;
                WorldPainterAuthoring.ActivePainter = null;
                WorldPainterState.ResetLastStroked();
            }
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
