#nullable enable
using System.Collections.Generic;
using WorldPainter;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Scene-view brush driver for WorldPainter (Phase 1 — no longer a Unity EditorTool but still
    /// a <see cref="ScriptableObject"/> so Unity's script-import metadata stays consistent with
    /// the type's history — switching to a plain C# class would trigger Unity's
    /// "is missing the class attribute 'ExtensionOfNativeClass'" import error).
    ///
    /// Owned by <see cref="WorldPainterInspector"/>, which subscribes <see cref="OnSceneGui"/>
    /// to <see cref="SceneView.duringSceneGui"/> while a WorldPainter is selected. The
    /// inspector calls <see cref="Enable"/> on mount and <see cref="Disable"/> on disable.
    ///
    /// Instantiate with <see cref="ScriptableObject.CreateInstance{T}()"/>; never <c>new</c>.
    ///
    /// Reuses the stroke plumbing (<see cref="TileRtCache"/>,
    /// <see cref="WorldPainterStroke"/>, <see cref="TerrainSculptRtWriteback"/>)
    /// on the <see cref="WorldPainter"/> tile refs.
    ///
    /// Spacing-stamping path: <see cref="WorldPainterStroke"/> interpolates the
    /// drag path and stamps every spacing metres.  The falloff LUT is uploaded
    /// once on Enable and re-uploaded whenever the CurveField changes.
    ///
    /// Stroke undo: a single Ctrl+Z reverts one full stroke per Unity Undo group.
    /// On Ctrl+Z, <see cref="Undo.undoRedoPerformed"/> fires; the handler pops the
    /// WorldPainterUndo snapshot for every tile touched and re-uploads bytes to GPU.
    ///
    /// Stroke code is in <c>WorldPainterSculptTool.Stroke.cs</c> (partial).
    /// </summary>
    internal sealed partial class WorldPainterSculptTool : ScriptableObject
    {
        // ── Tool resources ────────────────────────────────────────────────────

        internal ComputeShader? brushCompute;
        internal readonly TerrainSculptRtWriteback   writeback       = new TerrainSculptRtWriteback();
        internal readonly WorldPainterDensityEncoder densityEncoder  = new WorldPainterDensityEncoder();
        internal readonly WorldPainterAlphamapEncoder alphamapEncoder = new WorldPainterAlphamapEncoder();
        internal readonly TileRtCache                rtCache         = new TileRtCache();
        internal readonly WorldPainterStroke         stroke          = new WorldPainterStroke();
        internal readonly BrushFalloffLut            falloffLut      = new BrushFalloffLut();

        // ── Density RT (per-tile RT cache, managed in WorldPainterSculptTool.Density.cs) ──
        // densityRT / activeDensityMap properties live in the Density partial (legacy compat shims).

        // ── Prop stamp emitter (P4 task 2) ────────────────────────────────────

        internal readonly WorldPainterPropStampEmitter propEmitter = new WorldPainterPropStampEmitter();

        // ── Flatten target (P8 — captured per stroke when the Flatten tool is active) ──
        // World-space Y under the cursor at mouse-down; normalized per-tile at dispatch time.

        internal float flattenTargetWorldY;
        internal bool  flattenTargetValid;

        // ── Per-stroke tracking ───────────────────────────────────────────────

        internal readonly HashSet<Vector2Int> undoPushedCoords    = new HashSet<Vector2Int>();
        internal readonly HashSet<Vector2Int> strokeTouchedCoords = new HashSet<Vector2Int>();
        internal readonly List<Vector2Int>    resolveResults      = new List<Vector2Int>(4);

        // ── Undo group ────────────────────────────────────────────────────────

        internal int undoGroupId = -1;

        // ── HUD geometry (screen-space) ───────────────────────────────────────
        // Last rect drawn by DrawHud. Used by the prop Select path to swallow clicks that land on
        // the HUD so they don't fall through to the transform editor's click-pick (which would
        // deselect the instance when the user is actually clicking a Move/Rotate/Scale/Remove button).

        private Rect lastHudRect;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>Wires up event subscriptions + uploads the falloff LUT. Called by the inspector on mount.</summary>
        public void Enable()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/WorldPainter/Shaders/TerrainBrush.compute");

            // Upload initial falloff LUT from current brush settings.
            var brush = WorldPainterState.Brush;
            this.falloffLut.Upload(brush.falloff);

            // Subscribe: re-upload LUT when CurveField changes without re-activating.
            WorldPainterState.BrushFalloffDirty += this.OnBrushFalloffDirty;

            // Subscribe: Ctrl+Z triggers our custom per-tile snapshot revert.
            Undo.undoRedoPerformed += this.OnUndoRedoPerformed;

            // Subscribe: reset prop transform selection when the active layer changes away from Prop.
            WorldPainterState.ActiveLayerChanged += this.OnActiveLayerChangedForTransform;

            EditorApplication.update += this.OnEditorUpdate;
        }

        /// <summary>Unsubscribes events + tears down any in-flight stroke. Called by the inspector on disable.</summary>
        public void Disable()
        {
            WorldPainterState.BrushFalloffDirty -= this.OnBrushFalloffDirty;
            Undo.undoRedoPerformed -= this.OnUndoRedoPerformed;
            WorldPainterState.ActiveLayerChanged -= this.OnActiveLayerChangedForTransform;
            EditorApplication.update -= this.OnEditorUpdate;

            if (this.stroke.InStroke)
                this.TeardownActiveStroke(WorldPainterState.ActivePainter);
            else
                this.rtCache.ReleaseAll();

            this.falloffLut.Dispose();
        }

        private void OnEditorUpdate()
        {
            this.writeback.Tick();
            this.densityEncoder.Tick();
            this.alphamapEncoder.Tick();

            // NOTE: previously this block scheduled a coalesced grass/prop layer rebuild every
            // editor frame while a stroke was in progress, to give a live scatter preview.
            // Even at one rebuild per frame, the rebuild Disposes the engine's argsLodN
            // GraphicsBuffers — and Graphics.RenderMeshIndirect is player-loop-deferred, so the
            // previous frame's still-pending draw catches the just-released buffer and the GPU
            // renders garbage (the black-square flicker reported in Scene / Game / Inspector).
            // The stroke-end path already calls painter.RebuildScatterPreview() once, cleanly,
            // on mouse-up — that is the authoritative refresh point. No in-stroke rebuild.
        }

        // ── LUT re-upload (finding #3) ────────────────────────────────────────

        private void OnBrushFalloffDirty()
        {
            this.falloffLut.Upload(WorldPainterState.Brush.falloff);
        }

        // ── Prop transform mode — reset selection on layer change ─────────────

        private void OnActiveLayerChangedForTransform(string layerId, WorldPainterState.PaintLayerKind kind)
        {
            // When the user selects any layer that is not a Prop layer, reset the transform
            // selection so the gizmo doesn't linger on a stale instance.
            if (kind != WorldPainterState.PaintLayerKind.Prop)
                WorldPainterPropTransformEdit.Instance.Reset();
        }

        // ── Ctrl+Z stroke revert (finding #2) ────────────────────────────────

        /// <summary>
        /// Called by Unity when any Undo or Redo fires.
        /// We pop the WorldPainterUndo snapshot for every tile that was touched by the
        /// last stroke and re-upload bytes to GPU so the visual reverts immediately.
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            // Pop the latest WorldPainter snapshot for every tile that was last-stroked.
            foreach (var coord in WorldPainterState.LastStrokedTileSet)
            {
                var tile = this.FindTile(painter, coord);
                if (tile == null) continue;

                var snap = WorldPainterAuthoring.UndoStack.Pop(tile);
                if (snap == null) continue;

                // Re-upload the restored bytes to GPU.
                var gpu = this.FindGpu(painter, coord);
                if (gpu == null) continue;

                if (!this.rtCache.GetOrCreate(coord, gpu, out var heightRT))
                    continue;

                // Write restored CPU bytes back to the RT and commit synchronously.
                this.writeback.ExecuteSync(tile, gpu, heightRT);
                painter.CommitHeight(coord);
            }
        }

        // ── Scene view GUI ────────────────────────────────────────────────────

        /// <summary>
        /// Subscribed to <see cref="SceneView.duringSceneGui"/> by the inspector. Bails out
        /// when paint mode is off, no painter is bound, or the brush compute hasn't loaded.
        /// </summary>
        public void OnSceneGui(SceneView sceneView)
        {
            // Paint Mode gates continuous-stroke tools (paint, erase, sculpt). Click-only prop
            // tools (Place / Single / Select) dispatch regardless — "Paint Mode" is for brush
            // strokes, not single-click placement or transform-handle interaction.
            string gatingToolId = WorldPainterState.ActiveBrushToolId;
            if (!WorldPainterState.PaintModeActive && !WorldPainterState.IsClickOnlyTool(gatingToolId))
                return;
            if (this.brushCompute == null) return;

            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            Event e         = Event.current;
            int   controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            // ── Prop Select tool — delegate scene input to the transform editor ────

            bool isPropActive    = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop;
            string activeToolId  = WorldPainterState.ActiveBrushToolId;
            bool isSelectTool    = activeToolId == "instance.select";
            bool isPlacementTool = activeToolId == "instance.place" || activeToolId == "instance.single";

            if (isPropActive && isSelectTool)
            {
                PropLayer? propLayer = BrushToolTargets.ResolvePropLayer(painter);
                if (propLayer != null)
                {
                    // Draw the HUD FIRST: its buttons consume their own MouseDown, and the rect
                    // guard below swallows clicks on the HUD's dead space — both must happen before
                    // the transform editor runs, or its click-pick would deselect the instance the
                    // moment the user clicks a Move/Rotate/Scale/Remove button (the reported bug).
                    this.DrawHud();

                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp) &&
                        this.lastHudRect.Contains(e.mousePosition))
                    {
                        e.Use();
                    }

                    WorldPainterPropTransformEdit.Instance.OnSceneGUI(propLayer, sceneView);
                    return;
                }
            }

            // ── Normal brush-stroke path ──────────────────────────────────────────

            // TryGetBrushWorldPoint returns a point in PAINTING space (= root local space).
            // All tile-coord / resolver / compute-dispatch calls downstream expect painting coords.
            // For an identity root, InverseTransformPoint is a passthrough — no behaviour change.
            Ray  worldRay    = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = this.TryGetBrushWorldPoint(worldRay, painter, out Vector3 paintingPoint);

            if (hasHit)
            {
                var brush = WorldPainterState.Brush;

                // The brush ring is a *brush* concept — drop it for the placement tools (they
                // place exactly one instance at the cursor, not within a brush footprint). Erase
                // still uses the ring so the user sees the deletion radius. Not calling Set()
                // lets the existing freshness timeout fade the ring within 250 ms.
                bool suppressBrushRing = isPropActive && isPlacementTool;
                if (!suppressBrushRing)
                {
                    var previewColor = new Color(0.3f, 0.7f, 1.0f); // WorldPainter blue
                    // Pass the root's localToWorldMatrix so the preview ring is drawn under the
                    // root TRS (painting space → world space). Identity root = Handles.matrix stays
                    // Matrix4x4.identity, which is the default — no visual change.
                    TerrainBrushPreview.Set(paintingPoint, brush.size, previewColor, brush.shape,
                        s_heightFn, brush.maskTexture, painter.transform.localToWorldMatrix);
                }
                HandleUtility.Repaint();

                // Prop placement ghost — show the LOD 0 mesh translucent at the cursor so the
                // artist sees what they're about to place. Falls back to a wire disc when LOD 0
                // isn't assigned yet (the Setup panel will prompt them to add one).
                // PropGhostPreview renders via Graphics.DrawMeshNow / Handles in world space,
                // so convert the painting-space point back to world before passing it.
                if (isPropActive && isPlacementTool)
                {
                    PropLayer? propLayer = BrushToolTargets.ResolvePropLayer(painter);
                    if (propLayer != null)
                    {
                        Vector3 ghostWorldPoint = painter.transform.TransformPoint(paintingPoint);
                        PropGhostPreview.Draw(propLayer, ghostWorldPoint, valid: true);
                    }
                }
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                    this.HandleMouseDown(painter, paintingPoint, controlId);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.stroke.InStroke:
                    if (hasHit) this.HandleMouseDrag(painter, paintingPoint);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && this.stroke.InStroke:
                    this.HandleMouseUp(painter);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            this.DrawHud();
        }

        // ── Brush-disc terrain conform (restored from deleted TerrainSculptTool) ──

        // Cached so the brush preview gets a stable delegate (no per-event allocation).
        // Resolves the live ActivePainter each call so it always targets the current tiles.
        private static readonly TerrainBrushPreview.HeightFn s_heightFn = SampleActivePainterHeight;

        /// <summary>
        /// Per-vertex terrain height query for the conforming brush disc. Resolves the tile
        /// under (worldX, worldZ) on the active painter — map (SSOT) path first, falling back
        /// to the inline Tiles list — then samples the SSOT CPU heightmap (matches the GPU VTF).
        /// Returns false off-grid → the preview falls back to a flat disc at the hit height.
        /// </summary>
        private static bool SampleActivePainterHeight(float worldX, float worldZ, out float worldY)
        {
            worldY = 0f;
            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return false;

            Vector2Int coord = TerrainWorldGrid.WorldToTileCoord(worldX, worldZ);
            TerrainTileAsset? tile = painter.Map != null
                ? painter.Map.GetTile(coord)
                : ResolveInlineTile(painter, coord);
            if (tile == null) return false;

            return TerrainHeightSampleCpu.TrySampleWorld(tile, worldX, worldZ, out worldY);
        }

        private static TerrainTileAsset? ResolveInlineTile(WorldPainter painter, Vector2Int coord)
        {
            foreach (var entry in painter.Tiles)
                if (entry.coord == coord && entry.tileAsset != null)
                    return entry.tileAsset;
            return null;
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void DrawHud()
        {
            bool isPropActive = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop;

            // Build a 1-line target descriptor so the user can see WHAT will be painted.
            // Empty layer + Height fallback is the silent-failure case we surface here.
            var painter = WorldPainterState.ActivePainter;
            LayerType effective = painter != null
                ? WorldPainterState.EffectiveLayerType(painter)
                : LayerType.Height;
            string layerLabel = !string.IsNullOrEmpty(WorldPainterState.ActiveLayerId)
                ? WorldPainterState.ActiveLayerId
                : (effective == LayerType.Height ? "Height (base)" : "—");
            bool noLayerSelected =
                WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.None &&
                WorldPainterState.ActiveLayerIndex < 0;
            bool splatNoChannel =
                WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Splat &&
                WorldPainterState.ActivePaletteIndex < 0;

            bool isSelectTool = WorldPainterState.ActiveBrushToolId == "instance.select";
            bool hasSelection = isSelectTool && WorldPainterPropTransformEdit.Instance.SelectedIndex >= 0;

            Handles.BeginGUI();
            // Taller HUD when the prop transform toggle is shown OR a warning needs space.
            // The Select tool adds a Move/Rotate/Scale button row, plus a Remove row once an
            // instance is selected — each needs one more line.
            int baseHeight = (noLayerSelected || splatNoChannel) ? 72 : 64;
            int propExtra  = isPropActive
                ? (isSelectTool ? 22 + 24 + (hasSelection ? 22 : 0) : 22)
                : 0;
            var area = new Rect(8, 8, 260, baseHeight + propExtra);
            this.lastHudRect = area;
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("WorldPainter Brush", EditorStyles.boldLabel);
            GUILayout.Label($"Target: {effective}  ({layerLabel})", EditorStyles.miniLabel);
            if (noLayerSelected)
            {
                var warn = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.7f, 0.2f) } };
                GUILayout.Label("⚠ Select a layer in the stack to paint.", warn);
            }
            else if (splatNoChannel)
            {
                var warn = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.7f, 0.2f) } };
                GUILayout.Label("⚠ Click a TerrainLayer in the BrushDock palette to paint.", warn);
            }
            var brush = WorldPainterState.Brush;
            GUILayout.Label($"Size: {brush.size:F1}m  Strength: {brush.strength:F2}",
                EditorStyles.miniLabel);

            if (isPropActive)
            {
                string toolId = WorldPainterState.ActiveBrushToolId;
                string modeLabel = toolId switch
                {
                    "instance.select" => "Mode: Select (transform)",
                    "instance.erase"  => "Mode: Erase",
                    "instance.single" => "Mode: Single placement",
                    _                  => "Mode: Place (ghost preview)",
                };
                GUILayout.Label(modeLabel, EditorStyles.miniLabel);

                // Select tool: explicit Move/Rotate/Scale buttons. The transform editor owns its
                // own Mode (it can't rely on Unity's W/E/R hotkeys — they never reach a
                // duringSceneGui consumer driven from the inspector window), so drive it here.
                if (toolId == "instance.select")
                {
                    DrawTransformModeButtons();

                    // Remove button — deletes the selected instance (mirrors Delete/Backspace).
                    var propLayer = painter != null ? BrushToolTargets.ResolvePropLayer(painter) : null;
                    if (propLayer != null && WorldPainterPropTransformEdit.Instance.SelectedIndex >= 0)
                    {
                        if (GUILayout.Button("Remove Instance (Del)", EditorStyles.miniButton))
                        {
                            WorldPainterPropTransformEdit.Instance.RemoveSelected(propLayer);
                            HandleUtility.Repaint();
                        }
                    }
                }
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>
        /// Segmented Move/Rotate/Scale row that drives
        /// <see cref="WorldPainterPropTransformEdit.Instance"/>'s <c>Mode</c>. Mirrors the W/E/R
        /// shortcuts handled inside the transform editor's <c>OnSceneGUI</c>.
        /// </summary>
        private static void DrawTransformModeButtons()
        {
            var edit    = WorldPainterPropTransformEdit.Instance;
            var current = edit.Mode;

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(current == PropTransformMode.Move,   "Move (W)",   EditorStyles.miniButtonLeft)   && current != PropTransformMode.Move)
                edit.Mode = PropTransformMode.Move;
            if (GUILayout.Toggle(current == PropTransformMode.Rotate, "Rotate (E)", EditorStyles.miniButtonMid)    && current != PropTransformMode.Rotate)
                edit.Mode = PropTransformMode.Rotate;
            if (GUILayout.Toggle(current == PropTransformMode.Scale,  "Scale (R)",  EditorStyles.miniButtonRight)  && current != PropTransformMode.Scale)
                edit.Mode = PropTransformMode.Scale;
            GUILayout.EndHorizontal();

            if (edit.Mode != current)
                HandleUtility.Repaint();
        }
    }
}
