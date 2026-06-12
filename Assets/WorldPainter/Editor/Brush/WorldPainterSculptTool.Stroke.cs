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
            TerrainPaintTargetResolver.PinnedCoords.Clear();

            // Register the neighbour map for seam sync (P6) — must be done before the
            // first stamp so ApplySeamSync can find adjacent tiles at writeback time.
            this.writeback.RegisterNeighbours(WorldPainterSculptTool.EnumerateTileEntries(painter));

            // Begin Unity Undo group — one Ctrl+Z per stroke.
            Undo.IncrementCurrentGroup();
            this.undoGroupId = Undo.GetCurrentGroup();

            // Use biome-specific group name when painting a biome layer (task 6).
            LayerType startType = WorldPainterState.ActiveLayerType(painter, out _);
            string groupName = startType == LayerType.Biome
                ? "WorldPainter Biome Stroke"
                : "WorldPainter Sculpt Stroke";
            Undo.SetCurrentGroupName(groupName);

            GUIUtility.hotControl = controlId;
            this.stroke.Begin(worldPos);

            // Initial stamp at mouse-down position.
            this.DoStamp(painter, worldPos);
            this.CommitLastStrokedState();
        }

        private void HandleMouseDrag(WorldPainter painter, Vector3 worldPos)
        {
            var brush = WorldPainterState.Brush;

            // Spacing-stamping: stamps every spacingM metres along path.
            this.stroke.Advance(
                worldPos,
                brush.spacing,
                brush.flow,
                (stampPos, flow) => this.DoStamp(painter, stampPos));

            this.CommitLastStrokedState();
        }

        private void HandleMouseUp(WorldPainter painter)
        {
            this.TeardownActiveStroke(painter);
            this.CommitLastStrokedState();

            // Collapse Unity Undo group so one Ctrl+Z reverts the whole stroke.
            if (this.undoGroupId >= 0)
            {
                Undo.CollapseUndoOperations(this.undoGroupId);
                this.undoGroupId = -1;
            }
        }

        // ── Teardown ──────────────────────────────────────────────────────────

        private void TeardownActiveStroke(WorldPainter? painter)
        {
            this.writeback.CancelPending();
            this.densityEncoder.CancelPending();
            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();

            if (painter != null)
            {
                foreach (var coord in this.strokeTouchedCoords)
                {
                    var tile = this.FindTile(painter, coord);
                    var gpu  = this.FindGpu(painter, coord);
                    if (tile != null && gpu != null &&
                        this.rtCache.TryGet(coord, out var hRT, out var sRT))
                    {
                        this.writeback.ExecuteSync(tile, gpu, hRT, sRT);
                    }
                }
            }

            // Flush density encoder on mouse-up (synchronous final persist).
            if (this.activeDensityLayer != null && this.densityRT != null)
                this.densityEncoder.ExecuteSync(this.activeDensityLayer, this.densityRT);

            this.ReleaseDensityRT();

            // Release biome composite stamp density RTs.
            this.biomeStamp?.ReleaseDensityRTs();

            this.stroke.End();
            this.rtCache.ReleaseAll();
            this.strokeTouchedCoords.Clear();
            this.undoPushedCoords.Clear();
            TerrainPaintTargetResolver.PinnedCoords.Clear();
        }

        // ── Per-stamp dispatch ────────────────────────────────────────────────

        private void DoStamp(WorldPainter painter, Vector3 worldPos)
        {
            var brush = WorldPainterState.Brush;

            this.resolveResults.Clear();
            TerrainPaintTargetResolver.Resolve(
                new Vector2(worldPos.x, worldPos.z),
                brush.size,
                residencySet: null,
                this.resolveResults);

            foreach (var coord in this.resolveResults)
                this.DispatchOneTile(painter, worldPos, coord);
        }

        private void DispatchOneTile(WorldPainter painter, Vector3 worldPos, Vector2Int coord)
        {
            var tile = this.FindTile(painter, coord);
            var gpu  = this.FindGpu(painter, coord);
            if (tile == null || gpu == null) return;

            if (!this.rtCache.GetOrCreate(coord, gpu, out var heightRT, out var splatRT))
                return;

            bool isFirstTouch = !this.strokeTouchedCoords.Contains(coord);
            if (isFirstTouch && !this.undoPushedCoords.Contains(coord))
            {
                // Push undo snapshot before first edit on this tile.
                WorldPainterAuthoring.UndoStack.Push(tile);
                this.undoPushedCoords.Add(coord);
            }

            this.strokeTouchedCoords.Add(coord);

            // Pin touched tiles against stream-out for biome stroke duration (P5 task 5).
            var activePainter = WorldPainterState.ActivePainter;
            if (activePainter != null)
            {
                LayerType lt = WorldPainterState.ActiveLayerType(activePainter, out _);
                if (lt == LayerType.Biome)
                    TerrainPaintTargetResolver.PinnedCoords.Add(coord);
            }

            this.BindAndDispatch(worldPos, tile, heightRT, splatRT);

            // Throttled live writeback (for VTF preview).
            this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        // BindAndDispatch and kernel methods are in WorldPainterSculptTool.Kernels.cs

        internal TerrainTileAsset? FindTile(WorldPainter painter, Vector2Int coord)
        {
            foreach (var entry in painter.Tiles)
                if (entry.coord == coord && entry.tileAsset != null)
                    return entry.tileAsset;
            return null;
        }

        private TerrainTileGpuResources? FindGpu(WorldPainter painter, Vector2Int coord)
            => painter.ResourcesForCoord(coord);

        private bool TryGetBrushWorldPoint(Ray ray, WorldPainter painter, out Vector3 worldPoint)
        {
            // Primary: a real collider (play mode, scatter prop colliders, any pickable surface).
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                worldPoint = hit.point; return true;
            }

            // Edit-mode fallback: the GPU terrain is drawn via RenderMeshIndirect and has NO
            // collider, so Physics.Raycast never hits it. Intersect the ray with the tile
            // surface analytically. Tiles live in the WorldMapAsset (SSOT, P2+); the inline
            // painter.Tiles list is empty under the map path and kept only for back-compat.
            if (painter.Map != null && TryMapSurfaceHit(ray, painter.Map, out worldPoint))
                return true;

            foreach (var entry in painter.Tiles)
            {
                if (entry.tileAsset == null) continue;
                float midY = (entry.tileAsset.minHeight + entry.tileAsset.maxHeight) * 0.5f;
                var plane  = new Plane(Vector3.up, new Vector3(0f, midY, 0f));
                if (plane.Raycast(ray, out float dist) && dist > 0f && dist < 1e6f)
                {
                    worldPoint = ray.GetPoint(dist); return true;
                }
            }
            worldPoint = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Analytic ray↔terrain-surface intersection for edit mode (no collider on the GPU
        /// terrain). Two-pass: intersect a coarse mid-height plane to get an approximate XZ,
        /// sample the real surface height there, then re-intersect at that height so the brush
        /// sits on the terrain (exact for flat tiles, close for gentle slopes).
        /// </summary>
        private static bool TryMapSurfaceHit(Ray ray, WorldMapAsset map, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            foreach (var tile in map.EnumerateTiles())
            {
                if (tile == null) continue;
                float midY = (tile.minHeight + tile.maxHeight) * 0.5f;
                var coarse = new Plane(Vector3.up, new Vector3(0f, midY, 0f));
                if (!coarse.Raycast(ray, out float d0) || d0 <= 0f || d0 >= 1e6f) continue;

                Vector3    approx = ray.GetPoint(d0);
                Vector2Int coord  = TerrainWorldGrid.WorldToTileCoord(approx.x, approx.z);
                var surfaceTile   = map.GetTile(coord);
                if (surfaceTile != null &&
                    TerrainHeightSampleCpu.TrySampleWorld(surfaceTile, approx.x, approx.z, out float surfaceY))
                {
                    var surface = new Plane(Vector3.up, new Vector3(0f, surfaceY, 0f));
                    if (surface.Raycast(ray, out float d1) && d1 > 0f && d1 < 1e6f)
                    {
                        worldPoint = ray.GetPoint(d1); return true;
                    }
                }

                worldPoint = approx; return true; // coarse hit when off-tile / sample failed
            }
            return false;
        }

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
        /// Enumerates all (coord, tile) pairs in the painter for seam-sync neighbour registration.
        /// </summary>
        private static IEnumerable<(Vector2Int coord, TerrainTileAsset tile)> EnumerateTileEntries(
            WorldPainter painter)
        {
            foreach (var entry in painter.Tiles)
            {
                if (entry.tileAsset != null)
                    yield return (entry.coord, entry.tileAsset!);
            }
        }

    }
}
