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

        // ── Biome composite stamp (P5 task 2) ─────────────────────────────────

        internal WorldPainterBiomeStamp? biomeStamp;

        // ── Biome channel mute mask (P5 task 4) ───────────────────────────────

        internal BiomeChannelMask biomeMuteMask = BiomeChannelMask.None;

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

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>Wires up event subscriptions + uploads the falloff LUT. Called by the inspector on mount.</summary>
        public void Enable()
        {
            this.brushCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/WorldPainter/Shaders/TerrainBrush.compute");

            // Initialise biome composite stamp (P5).
            this.biomeStamp = new WorldPainterBiomeStamp(
                this.propEmitter, this.densityEncoder, this.falloffLut);

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

            // Live scatter preview during a grass stroke: rebuild ONLY the active grass layer
            // after each tick so the scene shows updated grass continuously (not only on mouse-
            // up). Density Texture2Ds were just refreshed by densityEncoder.Tick; per-layer
            // rebuild is cheap enough at editor tick rate.
            if (this.stroke.InStroke)
            {
                var painter = WorldPainterState.ActivePainter;
                if (painter != null)
                {
                    var grass = BrushToolTargets.ResolveActiveGrassLayer(painter);
                    if (grass != null)
                    {
                        painter.RebuildGrassLayer(grass);
                        UnityEditor.SceneView.RepaintAll();
                    }
                }
            }
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
            if (!WorldPainterState.PaintModeActive) return;
            if (this.brushCompute == null) return;

            var painter = WorldPainterState.ActivePainter;
            if (painter == null) return;

            Event e         = Event.current;
            int   controlId = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            // ── Prop Transform mode — delegate scene input; skip brush logic ──────

            if (WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop &&
                WorldPainterPropTransformEdit.PropTransformModeActive)
            {
                PropLayer? propLayer = BrushToolTargets.ResolvePropLayer(painter);
                if (propLayer != null)
                {
                    WorldPainterPropTransformEdit.Instance.OnSceneGUI(propLayer, sceneView);
                    this.DrawHud();
                    return;
                }
            }

            // ── Normal brush-stroke path ──────────────────────────────────────────

            Ray  ray    = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hasHit = this.TryGetBrushWorldPoint(ray, painter, out Vector3 worldPoint);

            if (hasHit)
            {
                var brush = WorldPainterState.Brush;
                // Alpha is set by TerrainBrushPreview (ring=1f, fill=FILL_ALPHA) — not honored here.
                var previewColor = new Color(0.3f, 0.7f, 1.0f); // WorldPainter blue
                TerrainBrushPreview.Set(worldPoint, brush.size, previewColor, brush.shape, s_heightFn);
                HandleUtility.Repaint();

                // Prop layer ghost preview (P4 task 3 — inline Handles, no WorldPainter.Editor dep).
                // Draw a green wire disc at the hover point when a Prop layer is active.
                LayerType hoverType = WorldPainterState.ActiveLayerType(painter, out _);
                if (hoverType == LayerType.Props && e.type == EventType.Repaint)
                    this.DrawPropGhostHandles(worldPoint, valid: true);
            }

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && hasHit:
                    this.HandleMouseDown(painter, worldPoint, controlId);
                    e.Use();
                    break;

                case EventType.MouseDrag when e.button == 0 && this.stroke.InStroke:
                    if (hasHit) this.HandleMouseDrag(painter, worldPoint);
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

        // ── Prop ghost preview (P4 task 3, option A — Handles only) ──────────

        /// <summary>
        /// Draws an inline prop placement ghost using Handles — green (valid placement)
        /// or red (slope/overlap rejected). No dependency on WorldPainter.Editor.
        /// </summary>
        private void DrawPropGhostHandles(Vector3 worldPos, bool valid)
        {
            // Wire disc at brush centre scaled to ~1m prop representation.
            Color ghostColor = valid
                ? new Color(0.3f, 1f, 0.3f, 0.8f)
                : new Color(1f, 0.2f, 0.2f, 0.8f);

            using (new Handles.DrawingScope(ghostColor))
            {
                Handles.DrawWireDisc(worldPos, Vector3.up, 0.5f);
                Handles.DrawLine(worldPos, worldPos + Vector3.up * 1.5f);
            }
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void DrawHud()
        {
            bool isPropActive = WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Prop;
            bool isTransform  = WorldPainterPropTransformEdit.PropTransformModeActive;

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
                WorldPainterState.ActiveBiomeIndex < 0 &&
                WorldPainterState.ActiveLayerIndex < 0;
            bool splatNoChannel =
                WorldPainterState.ActiveLayerKind == WorldPainterState.PaintLayerKind.Splat &&
                WorldPainterState.ActivePaletteIndex < 0;

            Handles.BeginGUI();
            // Taller HUD when the prop transform toggle is shown OR a warning needs space.
            int baseHeight = (noLayerSelected || splatNoChannel) ? 72 : 64;
            var area = new Rect(8, 8, 260, isPropActive ? baseHeight + 22 : baseHeight);
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
                string modeLabel = isTransform
                    ? "Mode: Transform  [T = scatter]"
                    : "Mode: Scatter  [T = transform]";
                if (GUILayout.Button(modeLabel, EditorStyles.miniButton))
                    WorldPainterPropTransformEdit.ToggleMode();
            }

            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
