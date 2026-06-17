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
            // Capture the stroke's layer kind before teardown clears active-layer state.
            LayerType strokeKind = WorldPainterState.EffectiveLayerType(painter);

            this.TeardownActiveStroke(painter);
            this.CommitLastStrokedState();

            // Collapse Unity Undo group so one Ctrl+Z reverts the whole stroke.
            if (this.undoGroupId >= 0)
            {
                Undo.CollapseUndoOperations(this.undoGroupId);
                this.undoGroupId = -1;
            }

            // Live edit-mode preview: a scatter stroke (grass density or prop instances) changed
            // the committed layer data — rebuild the scatter engines so the Scene view shows it.
            // TeardownActiveStroke has already flushed the density writeback synchronously above.
            if (strokeKind == LayerType.Grass || strokeKind == LayerType.Props)
            {
                painter.RebuildScatterPreview();
                UnityEditor.SceneView.RepaintAll();
            }
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
        /// <para>
        /// The analytical terrain raycasts (TryMapSurfaceHit / TryInlineTilesSurfaceHit) operate
        /// entirely in painting space, so the incoming world-space camera ray is converted to
        /// painting space before those casts. The Physics.Raycast fallback (for scatter-prop
        /// colliders) still fires the original world-space ray and converts the result back to
        /// painting space via InverseTransformPoint. For an identity root the transform is a
        /// passthrough — zero behaviour change.
        /// </para>
        /// </summary>
        private bool TryGetBrushWorldPoint(Ray worldRay, WorldPainter painter, out Vector3 paintingPoint)
        {
            // CPU-authoritative terrain hit FIRST (SSOT with the rendered mesh). The GPU terrain
            // renders from the full-res (257) height texture via VTF, but the streamed physics
            // collider is a low-res (65) nearest-neighbour downsample that can sit metres off the
            // visible surface on slopes — and it is stale right after a sculpt. Sampling the SAME
            // height the mesh renders (TerrainHeightSampleCpu) makes the brush / object placement
            // land exactly on the visible terrain. Physics.Raycast is the fallback for non-terrain
            // pickables (scatter prop colliders) only when the ray is not over a terrain tile.
            // Tiles live in the WorldMapAsset (SSOT, P2+); painter.Tiles is the legacy back-compat list.

            // Convert the world-space scene camera ray to painting space so the analytical terrain
            // methods receive coordinates consistent with TerrainWorldGrid / tile heightmaps.
            Transform root = painter.transform;
            var paintingRay = new Ray(
                root.InverseTransformPoint(worldRay.origin),
                root.InverseTransformDirection(worldRay.direction));

            if (painter.Map != null && TryMapSurfaceHit(paintingRay, painter.Map, out paintingPoint))
                return true;
            if (TryInlineTilesSurfaceHit(paintingRay, painter, out paintingPoint))
                return true;

            // Physics.Raycast operates in world space — use the original world-space ray and
            // convert the hit point back to painting space.
            if (Physics.Raycast(worldRay, out RaycastHit hit, Mathf.Infinity))
            {
                paintingPoint = root.InverseTransformPoint(hit.point); return true;
            }

            paintingPoint = Vector3.zero;
            return false;
        }

        /// <summary>
        /// CPU-authoritative ray↔terrain-surface intersection for the legacy inline
        /// <c>painter.Tiles</c> list (empty under the WorldMapAsset SSOT path). Same two-pass
        /// coarse-plane → CPU height sample → re-intersect as <see cref="TryMapSurfaceHit"/>,
        /// guarding that the coarse XZ actually falls inside the candidate tile.
        /// </summary>
        private static bool TryInlineTilesSurfaceHit(Ray ray, WorldPainter painter, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            foreach (var entry in painter.Tiles)
            {
                var tile = entry.tileAsset;
                if (tile == null) continue;
                // Coarse plane at minHeight (flat zero-filled tiles sit exactly here; the second
                // pass corrects Y on sculpted tiles). midY would float above the camera on a fresh
                // flat tile and make Plane.Raycast miss — see TryMapSurfaceHit for the rationale.
                var coarse = new Plane(Vector3.up, new Vector3(0f, tile.minHeight, 0f));
                if (!coarse.Raycast(ray, out float d0) || d0 <= 0f || d0 >= 1e6f) continue;

                Vector3 approx = ray.GetPoint(d0);
                if (TerrainWorldGrid.WorldToTileCoord(approx.x, approx.z) != tile.tileCoord) continue;

                if (TerrainHeightSampleCpu.TrySampleWorld(tile, approx.x, approx.z, out float surfaceY))
                {
                    var surface = new Plane(Vector3.up, new Vector3(0f, surfaceY, 0f));
                    if (surface.Raycast(ray, out float d1) && d1 > 0f && d1 < 1e6f)
                    {
                        worldPoint = ray.GetPoint(d1); return true;
                    }
                }

                worldPoint = approx; return true; // coarse hit when sample failed
            }
            return false;
        }

        /// <summary>
        /// Analytic ray↔terrain-surface intersection for edit mode (no collider on the GPU
        /// terrain). Two-pass: intersect a coarse surface plane to get an approximate XZ,
        /// sample the real surface height there, then re-intersect at that height so the brush
        /// sits on the terrain (exact for flat tiles, close for gentle slopes).
        ///
        /// Uses <c>minHeight</c> for the coarse plane, NOT <c>midY=(min+max)/2</c>. The mid-
        /// point would be above the scene camera for a newly-created flat tile (minHeight=0,
        /// maxHeight=512 ⇒ midY=256), causing <c>Plane.Raycast</c> to miss and returning no
        /// hit, which makes every frame report <c>hasHit=false</c> — disabling both the preview
        /// ring and all sculpt dispatch.
        /// </summary>
        private static bool TryMapSurfaceHit(Ray ray, WorldMapAsset map, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            foreach (var tile in map.EnumerateTiles())
            {
                if (tile == null) continue;
                // Coarse plane at minHeight — the surface for a flat zero-filled tile is exactly
                // here. For sculpted tiles with varying height the second pass corrects the Y.
                var coarse = new Plane(Vector3.up, new Vector3(0f, tile.minHeight, 0f));
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
