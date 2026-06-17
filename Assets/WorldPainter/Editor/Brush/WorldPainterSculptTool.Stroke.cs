#nullable enable
using System.Collections.Generic;
using WorldPainter;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Stroke dispatch half of <see cref="WorldPainterSculptTool"/> (partial).
    ///
    /// Contains: mouse handlers, per-tile dispatch (<see cref="DispatchOneTile"/>),
    /// brush-compute bind, helpers for tile/GPU lookup + brush-world-point resolution,
    /// and stroke tracking commit.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // ── Mouse handlers ────────────────────────────────────────────────────

        private void HandleMouseDown(WorldPainter painter, Vector3 worldPos, int controlId)
        {
            this.undoPushedCoords.Clear();
            this.strokeTouchedCoords.Clear();
            this.rtCache.ReleaseAll();

            // Register the neighbour map for seam sync (P6) — must be done before the
            // first stamp so ApplySeamSync can find adjacent tiles at writeback time.
            this.writeback.RegisterNeighbours(WorldPainterSculptTool.EnumerateTileEntries(painter));

            // Begin Unity Undo group — one Ctrl+Z per stroke.
            Undo.IncrementCurrentGroup();
            this.undoGroupId = Undo.GetCurrentGroup();

            Undo.SetCurrentGroupName("WorldPainter Sculpt Stroke");

            GUIUtility.hotControl = controlId;
            this.stroke.Begin(worldPos);

            // Capture the flatten target (cursor world-Y) once per stroke when the Flatten tool
            // is active — normalized per-tile at dispatch. Other tools ignore this.
            this.flattenTargetValid = false;
            LayerType startKind = WorldPainterState.EffectiveLayerType(painter);
            IBrushTool? startTool = BrushToolRegistry.ResolveActiveTool(
                startKind, WorldPainterState.ActiveBrushToolId);
            if (startTool is HeightFlattenTool)
            {
                Vector2Int fc = TerrainWorldGrid.WorldToTileCoord(worldPos.x, worldPos.z);
                var ft = this.FindTile(painter, fc);
                if (ft != null && TerrainHeightSampleCpu.TrySampleWorld(ft, worldPos.x, worldPos.z, out float fy))
                {
                    this.flattenTargetWorldY = fy;
                    this.flattenTargetValid  = true;
                }
            }

            // Fresh stamp buffer + drain clock for this stroke.
            this.stampBuffer.Clear();
            this.lastDrainTime = EditorApplication.timeSinceStartup;

            // Initial stamp at mouse-down position — dispatched immediately for instant feedback.
            this.DoStamp(painter, worldPos);
            this.CommitLastStrokedState();

            // Live scatter preview at the click point (grass): flush density + flicker-free deferred
            // rebuild so the first dab shows under the cursor without waiting for mouse-up.
            this.PreviewActiveScatter(painter);
        }

        private void HandleMouseDrag(WorldPainter painter, Vector3 worldPos)
        {
            var brush = WorldPainterState.Brush;

            // Spacing-stamping: enqueue (cheap, no GPU) every spacingM metres along the path. The
            // throttled drain dispatches the batch + one scatter rebuild — decoupling mouse-event
            // frequency from GPU work is what removes the drag stutter.
            this.stroke.Advance(
                worldPos,
                brush.spacing,
                brush.flow,
                (stampPos, flow) => this.stampBuffer.Enqueue(stampPos));

            // Opportunistic drain: if the cadence interval already elapsed, flush this tick instead
            // of waiting for the next editor update.
            this.MaybeDrain(painter);
        }

        private void HandleMouseUp(WorldPainter painter)
        {
            // Drain any stamps buffered but not yet dispatched by the last drain tick, so the full
            // stroke is committed (DoStamp populates strokeTouchedCoords, which TeardownActiveStroke
            // reads) before teardown flushes the density RTs. LastStrokedTileSet is committed by
            // TeardownActiveStroke's CommitLastStrokedState below (unchanged from the pre-buffer path).
            while (this.stampBuffer.TryDequeue(out Vector3 pending))
                this.DoStamp(painter, pending);

            // Capture the stroke's layer kind + active grass layer before teardown clears state.
            LayerType strokeKind = WorldPainterState.EffectiveLayerType(painter);
            GrassLayer? grassLayer = strokeKind == LayerType.Grass
                ? BrushToolTargets.ResolveActiveGrassLayer(painter)
                : null;

            this.TeardownActiveStroke(painter);
            this.CommitLastStrokedState();

            // Collapse Unity Undo group so one Ctrl+Z reverts the whole stroke.
            if (this.undoGroupId >= 0)
            {
                Undo.CollapseUndoOperations(this.undoGroupId);
                this.undoGroupId = -1;
            }

            // Live edit-mode preview: a scatter stroke changed the committed layer data — rebuild so
            // the Scene view shows it. TeardownActiveStroke already flushed density writeback above.
            if (strokeKind == LayerType.Grass && grassLayer != null)
            {
                // DEFERRED rebuild (not the immediate RebuildScatterPreview): engines retired by the
                // last in-drag drain keep their frame margin and dispose via the tick countdown,
                // instead of being force-freed here while their indirect draw is still pending
                // (end-of-stroke flicker). Grass-only — a density stroke leaves props untouched.
                painter.RebuildGrassLayerDeferred(grassLayer);
                UnityEditor.SceneView.RepaintAll();
            }
            else if (strokeKind == LayerType.Props)
            {
                painter.RebuildScatterPreview();
                UnityEditor.SceneView.RepaintAll();
            }
        }

        // ── Buffered drain (smooth-paint pipeline) ────────────────────────────

        /// <summary>
        /// Drains the stamp buffer + refreshes the live scatter preview when the ~15 Hz cadence
        /// interval has elapsed. Called from the editor update loop (covers a paused-but-held drag)
        /// and opportunistically at the end of <see cref="HandleMouseDrag"/>.
        /// </summary>
        private void MaybeDrain(WorldPainter painter)
        {
            if (this.stampBuffer.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - this.lastDrainTime < DRAIN_INTERVAL_SEC) return;
            this.lastDrainTime = now;
            this.DrainAndPreview(painter);
        }

        /// <summary>Pops ALL buffered stamps, dispatches them, then refreshes the live scatter preview.</summary>
        private void DrainAndPreview(WorldPainter painter)
        {
            while (this.stampBuffer.TryDequeue(out Vector3 pos))
                this.DoStamp(painter, pos);
            this.CommitLastStrokedState();
            this.PreviewActiveScatter(painter);
        }

        /// <summary>
        /// Live density-paint feedback for the active GRASS layer during a stroke. Flushes the
        /// touched-tile density RTs synchronously (so the committed density texture stays in step
        /// with what was painted), then repaints the Scene view so the density heatmap overlay
        /// (<see cref="DrawDensityOverlay"/>) reflects the just-painted footprint.
        ///
        /// NOTE: the grass-blade scatter is deliberately NOT rebuilt here. Re-scattering every
        /// painted tile each ~15 Hz drain (<see cref="DrainAndPreview"/>) is the cost that made
        /// large-area grass painting lag — it scales with total painted area, not brush size. The
        /// single blade rebuild now happens once on mouse-up (<see cref="HandleMouseUp"/>); during
        /// the drag the heatmap overlay stands in for the blades as the painted-area indicator.
        ///
        /// Props are not previewed here — prop placement is click-based, not a density drag.
        /// </summary>
        private void PreviewActiveScatter(WorldPainter painter)
        {
            if (WorldPainterState.EffectiveLayerType(painter) != LayerType.Grass) return;
            GrassLayer? layer = BrushToolTargets.ResolveActiveGrassLayer(painter);
            if (layer == null) return;

            this.FlushAllDensityRTs();
            UnityEditor.SceneView.RepaintAll();
        }

        // ── Teardown ──────────────────────────────────────────────────────────

        private void TeardownActiveStroke(WorldPainter? painter)
        {
            this.writeback.CancelPending();
            this.densityEncoder.CancelPending();
            this.alphamapEncoder.CancelPending();
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();

            if (painter != null)
            {
                // Drop stale physics colliders for tiles whose heights actually changed this
                // stroke. The collider streamer only re-cooks on ring entry, so without this an
                // in-ring tile keeps pre-sculpt collision geometry (raycasts land on the old surface).
                var colliderStreamer = painter.GetComponent<TerrainColliderStreamer>();
                foreach (var coord in this.strokeTouchedCoords)
                {
                    var tile = this.FindTile(painter, coord);
                    var gpu  = this.FindGpu(painter, coord);
                    if (tile != null && gpu != null &&
                        this.rtCache.TryGet(coord, out var hRT))
                    {
                        this.writeback.ExecuteSync(tile, gpu, hRT);
                        if (colliderStreamer != null)
                            colliderStreamer.InvalidateCollider(coord);
                    }
                }
            }

            // Flush all per-tile density + alphamap RTs on mouse-up (synchronous final persist).
            this.FlushAllDensityRTs();
            this.ReleaseAllDensityRTs();
            this.FlushAllAlphamapRTs();
            // Restore the committed Texture2D alphamap bindings before releasing the RTs so the
            // terrain shader doesn't keep sampling a soon-to-be-disposed RenderTexture.
            this.RestoreAllAlphamapBindings(painter);
            this.ReleaseAllAlphamapRTs();

            this.stroke.End();
            this.rtCache.ReleaseAll();
            this.strokeTouchedCoords.Clear();
            this.undoPushedCoords.Clear();
        }

        // ── Per-stamp dispatch ────────────────────────────────────────────────

        private void DoStamp(WorldPainter painter, Vector3 worldPos)
        {
            var brush = WorldPainterState.Brush;

            // Props are LAYER-GLOBAL (one AuthoredInstances list per layer, no per-tile bucket
            // at brush time). Dispatching per overlapped tile would call InstancePlaceTool.Apply
            // once per tile, multiplying placement by tile count whenever the brush footprint
            // straddles >1 tile (the "click → many instances on large mesh" bug). Just dispatch
            // once at the cursor's tile.
            LayerType activeKind = WorldPainterState.EffectiveLayerType(painter);
            if (activeKind == LayerType.Props)
            {
                Vector2Int cursorCoord = TerrainWorldGrid.WorldToTileCoord(worldPos.x, worldPos.z);
                this.DispatchOneTile(painter, worldPos, cursorCoord);
                return;
            }

            this.resolveResults.Clear();
            TerrainPaintTargetResolver.Resolve(
                new Vector2(worldPos.x, worldPos.z),
                brush.size,
                residencySet: null,
                this.resolveResults);

            // Pre-create the working RT for every overlapped tile BEFORE dispatching any of them,
            // so the seam-aware Smooth kernel can sample a straddled neighbour's RT — which must
            // already exist when the first tile of the stamp dispatches. GetOrCreate is idempotent;
            // DispatchOneTile re-fetches the same RT.
            foreach (var coord in this.resolveResults)
            {
                var prepGpu = this.FindGpu(painter, coord);
                if (prepGpu != null)
                    this.rtCache.GetOrCreate(coord, prepGpu, out _);
            }

            foreach (var coord in this.resolveResults)
                this.DispatchOneTile(painter, worldPos, coord);
        }

        private void DispatchOneTile(WorldPainter painter, Vector3 worldPos, Vector2Int coord)
        {
            var tile = this.FindTile(painter, coord);
            var gpu  = this.FindGpu(painter, coord);
            if (tile == null || gpu == null) return;

            if (!this.rtCache.GetOrCreate(coord, gpu, out var heightRT))
                return;

            bool isFirstTouch = !this.strokeTouchedCoords.Contains(coord);
            if (isFirstTouch && !this.undoPushedCoords.Contains(coord))
            {
                // Push undo snapshot before first edit on this tile.
                WorldPainterAuthoring.UndoStack.Push(tile);
                this.undoPushedCoords.Add(coord);
            }

            this.strokeTouchedCoords.Add(coord);

            this.BindAndDispatch(worldPos, tile, heightRT);

            // Throttled live writeback (for VTF preview).
            this.writeback.RequestAsync(tile, gpu, heightRT);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        // BindAndDispatch and kernel methods are in WorldPainterSculptTool.Kernels.cs

        internal TerrainTileAsset? FindTile(WorldPainter painter, Vector2Int coord)
        {
            // Map-based SSOT path (P2+): painter.Tiles is empty when a WorldMapAsset is used.
            if (painter.Map != null)
                return painter.Map.GetTile(coord);

            foreach (var entry in painter.Tiles)
                if (entry.coord == coord && entry.tileAsset != null)
                    return entry.tileAsset;
            return null;
        }

        private TerrainTileGpuResources? FindGpu(WorldPainter painter, Vector2Int coord)
            => painter.ResourcesForCoord(coord);

        /// <summary>
        /// Resolves the brush contact point in PAINTING space (= WorldPainter root local space).
        /// Thin wrapper over the SSOT <see cref="WorldPainterTerrainRaycast.TryPick"/> (shared with
        /// the click-to-select terrain picker) — see that helper for the two-pass analytic-surface
        /// rationale.
        /// </summary>
        private bool TryGetBrushWorldPoint(Ray worldRay, WorldPainter painter, out Vector3 paintingPoint)
            => WorldPainterTerrainRaycast.TryPick(worldRay, painter, out paintingPoint);

        private void CommitLastStrokedState()
        {
            WorldPainterState.LastStrokedTileSet.Clear();
            foreach (var c in this.strokeTouchedCoords)
                WorldPainterState.LastStrokedTileSet.Add(c);

            Vector2Int? primary = null;
            foreach (var c in this.strokeTouchedCoords) { primary = c; break; }
            WorldPainterState.LastStrokedCoord = primary;
        }

        /// <summary>
        /// Enumerates all (coord, tile, gpu) triples in the painter for seam-sync neighbour
        /// registration. The GPU resources let seam sync re-upload a neighbour's height texture
        /// after it rewrites the neighbour's shared edge (null for non-resident tiles, which are
        /// not rendered). Covers both the map-based SSOT path (P2+) and the legacy inline-Tiles path.
        /// </summary>
        private static IEnumerable<(Vector2Int coord, TerrainTileAsset tile, TerrainTileGpuResources? gpu)>
            EnumerateTileEntries(WorldPainter painter)
        {
            if (painter.Map != null)
            {
                foreach (var coord in painter.Map.EnumerateTileCoords())
                {
                    var tile = painter.Map.GetTile(coord);
                    if (tile != null)
                        yield return (coord, tile, painter.ResourcesForCoord(coord));
                }
                yield break;
            }

            foreach (var entry in painter.Tiles)
            {
                if (entry.tileAsset != null)
                    yield return (entry.coord, entry.tileAsset!, painter.ResourcesForCoord(entry.coord));
            }
        }

    }
}
