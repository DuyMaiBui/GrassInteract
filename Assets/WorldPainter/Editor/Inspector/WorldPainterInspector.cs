#nullable enable
using UnityEditor;
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
        private WorldPainterTileStrip?        tileStrip;

        // ── Phase 1 — Scene-view brush driver (replaces the EditorTool) ───────

        // Lifetime is the inspector's lifetime: created in CreateInspectorGUI, disposed in OnDisable.
        // Its OnSceneGui is the SceneView.duringSceneGui subscriber.
        private WorldPainterSculptTool? brushDriver;

        // Ghost-tile click handler (Add/Remove modes — driven by WorldPainterState.TileEditMode).
        private WorldPainterTileGhostHandler? tileGhost;

        // ── CreateInspectorGUI ────────────────────────────────────────────────

        public override VisualElement CreateInspectorGUI()
        {
            var painter = (WorldPainter)this.target;

            // Bind state
            WorldPainterState.ActivePainter = painter;
            WorldPainterAuthoring.ActivePainter = painter;

            // Phase 1 — instantiate the brush driver and subscribe its scene-input handler to
            // SceneView.duringSceneGui. Replaces the old EditorTool with its toolbar icon.
            // OnSceneGui internally short-circuits when WorldPainterState.PaintModeActive is false.
            if (this.brushDriver == null)
            {
                // ScriptableObject (not "new") because the type still derives from SO so Unity's
                // script importer can find ExtensionOfNativeClass metadata. HideAndDontSave so it
                // never accidentally serialises into the scene.
                this.brushDriver = ScriptableObject.CreateInstance<WorldPainterSculptTool>();
                this.brushDriver.hideFlags = HideFlags.HideAndDontSave;
                this.brushDriver.Enable();
                SceneView.duringSceneGui -= this.brushDriver.OnSceneGui;
                SceneView.duringSceneGui += this.brushDriver.OnSceneGui;
            }

            // Tile ghost handler — drawn only when TileEditMode != Off; checks state internally.
            if (this.tileGhost == null)
            {
                this.tileGhost = new WorldPainterTileGhostHandler();
                SceneView.duringSceneGui -= this.tileGhost.OnSceneGui;
                SceneView.duringSceneGui += this.tileGhost.OnSceneGui;
            }

            // Auto-enter paint mode on layer selection (replaces the old ToolManager.SetActiveTool).
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

            // Manual hot-rebuild — full dispose + rebuild of every tile + surface engine.
            // Use when the render looks out of sync (e.g. after an asset import or a
            // serialized field change that bypasses the live brush writeback path).
            var rebuildBtn = new Button(() => RebuildPainter(painter)) { text = "↻ Rebuild World" };
            rebuildBtn.tooltip = "Force a full rebuild of the terrain render + surface scatter engines. " +
                                 "Use if the render looks out of sync.";
            rebuildBtn.style.alignSelf  = Align.FlexEnd;
            rebuildBtn.style.marginTop  = 2;
            headerRight.Add(rebuildBtn);

            // Cheat-sheet button
            this.coachMarks = new WorldPainterCoachMarks();
            var cheatBtn = this.coachMarks.BuildCheatSheetButton();
            cheatBtn.style.alignSelf = Align.FlexEnd;
            headerRight.Add(cheatBtn);

            headerZone.Add(headerRight);
            root.Add(headerZone);

            // ── Tile strip: explicit coord add / remove ───────────────────────
            this.tileStrip = new WorldPainterTileStrip();
            root.Add(this.tileStrip.Build(painter));

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
                bool noLayers = surfaceLayerCount == 0;
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

            // ── Animate in Edit Mode toggle ───────────────────────────────────
            var animToggle = new Toggle("Animate in Edit Mode")
            {
                value = WorldPainterAnimPump.Enabled,
                tooltip = "Continuously advance _GrassTime in edit mode so wind sway and interactor lean " +
                          "animate in the SceneView without manual interaction. Throttled to ~30 fps.",
            };
            animToggle.RegisterValueChangedCallback(evt =>
                WorldPainterAnimPump.Enabled = evt.newValue);
            root.Add(animToggle);

            // ── Brush dock (constant — sits directly under the layer filter) ──
            this.brushDock = new WorldPainterBrushDock();
            root.Add(this.brushDock.Build());

            // ── Layer stack ───────────────────────────────────────────────────
            this.layerStack = new WorldPainterLayerStackView(
                this.serializedObject,
                painter);
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
                        // Grass/prop layer fields are editable ONLY here, inside the WorldPainter
                        // detail card. Selecting the sub-asset directly renders it read-only —
                        // GrassLayerEditor / PropLayerEditor gate on this flag.
                        WorldPainterLayerEditContext.EditingInWorldPainter = true;
                        try { capturedEditor.OnInspectorGUI(); }
                        finally { WorldPainterLayerEditContext.EditingInWorldPainter = false; }
                    });
                    cardArea.Add(imgui);
                }
                else if (WorldPainterState.ActiveLayerType(painter, out _) == LayerType.Height)
                {
                    // Terrain (Height base) selected → inline CDLOD distance-band editor,
                    // mirroring the grass/prop inline LOD section (no popup).
                    var terrainLabel = new Label("[Terrain] LOD");
                    terrainLabel.AddToClassList("wp-layer-name");
                    terrainLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    terrainLabel.style.marginTop = 6;
                    cardArea.Add(terrainLabel);

                    var terrainImgui = new IMGUIContainer(() =>
                    {
                        // Own SerializedObject (not this.serializedObject, which the layer-stack
                        // shares) so terrain-band edits never race the inspector's Update/Apply cycle.
                        var so = new SerializedObject(painter);
                        so.Update();
                        var ranges = so.FindProperty("lodRangesM");
                        if (ranges == null)
                        {
                            EditorGUILayout.HelpBox("lodRangesM not found on WorldPainter.", MessageType.Error);
                            return;
                        }
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.LabelField("Terrain LOD (CDLOD distance bands)", EditorStyles.boldLabel);
                        WorldPainterLodGui.DrawTerrainLodSection(ranges);
                        bool terrainChanged = EditorGUI.EndChangeCheck();
                        so.ApplyModifiedProperties();
                        if (terrainChanged)
                            WorldPainterRebuildScheduler.MarkTerrainDirty(painter);
                    });
                    cardArea.Add(terrainImgui);
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

            // Phase 1 — tear down the brush driver and its SceneView subscription.
            if (this.brushDriver != null)
            {
                SceneView.duringSceneGui -= this.brushDriver.OnSceneGui;
                this.brushDriver.Disable();
                Object.DestroyImmediate(this.brushDriver);
                this.brushDriver = null;
            }
            WorldPainterState.PaintModeActive = false;

            // Tile ghost handler — drop subscription + force mode off so the next inspector
            // open starts clean.
            if (this.tileGhost != null)
            {
                SceneView.duringSceneGui -= this.tileGhost.OnSceneGui;
                this.tileGhost = null;
            }
            WorldPainterState.TileEditMode = WorldPainterState.TileEditModeKind.Off;

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
        /// Auto-enables paint mode whenever the user selects a paintable layer; auto-disables
        /// when the selection drops back to None. Replaces the old EditorTool activation path —
        /// the brush driver is always subscribed to SceneView.duringSceneGui, but it
        /// short-circuits when <see cref="WorldPainterState.PaintModeActive"/> is false.
        /// </summary>
        private void OnActiveLayerChanged(string layerId, WorldPainterState.PaintLayerKind kind)
        {
            // Auto-enable Paint Mode ONLY when the new layer's default tool is a continuous
            // brush stroke. Prop layers default to "Place" (click-only) — auto-enabling Paint
            // Mode there would show a misleading "Paint Mode: ON" label even though the user
            // is just placing single instances. The SculptTool gate lets click-only tools
            // dispatch regardless of this flag, so prop placement still works.
            WorldPainterState.PaintModeActive =
                kind != WorldPainterState.PaintLayerKind.None &&
                kind != WorldPainterState.PaintLayerKind.Prop;
        }

        // ── Manual rebuild ────────────────────────────────────────────────────

        /// <summary>
        /// Hot rebuild: full dispose + rebuild of every tile + every surface scatter engine.
        /// Mirrors the inline path the deleted scene overlay used; the inspector's "↻ Rebuild
        /// World" button is the canonical entry point now.
        /// </summary>
        private static void RebuildPainter(WorldPainter painter)
        {
            if (painter == null) return;
            painter.TryBuild();
            painter.RebuildScatterPreview();
            SceneView.RepaintAll();
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
                WpLog.Warning($"[WorldPainter] USS not found: {path}");
            }
        }
    }
}
