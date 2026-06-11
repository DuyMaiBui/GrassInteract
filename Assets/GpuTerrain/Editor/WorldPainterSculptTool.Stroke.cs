#nullable enable
using GrassInteract;
using UnityEditor;
using UnityEngine;

namespace GpuTerrain.Editor
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

            // Begin Unity Undo group — one Ctrl+Z per stroke.
            Undo.IncrementCurrentGroup();
            this.undoGroupId = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("WorldPainter Sculpt Stroke");

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

            this.stroke.End();
            this.rtCache.ReleaseAll();
            this.strokeTouchedCoords.Clear();
            this.undoPushedCoords.Clear();
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

            this.BindAndDispatch(worldPos, tile, heightRT, splatRT);

            // Throttled live writeback (for VTF preview).
            this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
        }

        private void BindAndDispatch(
            Vector3 worldPos,
            TerrainTileAsset tile,
            RenderTexture heightRT,
            RenderTexture splatRT)
        {
            if (this.brushCompute == null) return;

            var painter = WorldPainterState.ActivePainter;

            var brush = WorldPainterState.Brush;
            var worldXZ = new Vector2(worldPos.x, worldPos.z);
            TerrainPaintTargetResolver.WorldBrushToTileUV(
                worldXZ, brush.size, tile.tileCoord,
                out Vector2 centerUV, out float radiusUV);

            int rtRes  = TerrainSculptConfig.BRUSH_RT_RES;
            int groups = Mathf.CeilToInt((float)rtRes / TerrainSculptConfig.THREAD_GROUP_SIZE);

            this.brushCompute.SetVector("_BrushCenterUV", centerUV);
            this.brushCompute.SetFloat("_BrushRadiusUV",  radiusUV);
            this.brushCompute.SetFloat("_Strength",        brush.strength);
            this.brushCompute.SetInt("_RTRes",             rtRes);

            // Determine kernel by active layer type.
            // Height → RaiseLower, Splat → PaintSplat, Grass → PaintDensity.
            LayerType activeType = LayerType.Height;
            int splatChannel = -1;
            if (painter != null)
                activeType = WorldPainterState.ActiveLayerType(painter, out splatChannel);

            if (activeType == LayerType.Splat)
            {
                this.DispatchSplatKernel(groups, splatChannel, splatRT);
            }
            else if (activeType == LayerType.Grass && painter != null)
            {
                int scatterIdx = WorldPainterState.ActiveScatterIndex(painter);
                if (scatterIdx >= 0 && scatterIdx < painter.ScatterLayers.Count)
                {
                    var scatterLayer = painter.ScatterLayers[scatterIdx] as GrassInteract.DensityScatterLayer;
                    if (scatterLayer != null)
                    {
                        var dRT = this.GetOrCreateDensityRT(scatterLayer);
                        if (dRT != null)
                            this.DispatchDensityKernel(groups, dRT);
                    }
                }
            }
            else
            {
                this.DispatchHeightKernel(groups, heightRT);
            }
        }

        private void DispatchHeightKernel(int groups, RenderTexture heightRT)
        {
            if (this.brushCompute == null) return;

            int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_RAISE_LOWER);
            this.falloffLut.BindToCompute(this.brushCompute, k);
            this.brushCompute.SetTexture(k, "_HeightRT", heightRT);
            this.brushCompute.SetFloat("_RaiseSign", 1f);
            this.brushCompute.Dispatch(k, groups, groups, 1);
        }

        private void DispatchSplatKernel(int groups, int splatChannel, RenderTexture splatRT)
        {
            if (this.brushCompute == null) return;

            int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_SPLAT);
            this.falloffLut.BindToCompute(this.brushCompute, k);
            this.brushCompute.SetTexture(k, "_SplatRT", splatRT);
            this.brushCompute.SetInt("_SplatLayer", Mathf.Clamp(splatChannel, 0, TerrainSculptConfig.MAX_SPLAT_LAYERS - 1));
            this.brushCompute.Dispatch(k, groups, groups, 1);
        }

        private void DispatchDensityKernel(int groups, RenderTexture densityRT)
        {
            if (this.brushCompute == null) return;

            int k = this.brushCompute.FindKernel(TerrainSculptConfig.KERNEL_PAINT_DENSITY);
            this.falloffLut.BindToCompute(this.brushCompute, k);
            this.brushCompute.SetTexture(k, "_DensityRT", densityRT);
            // Mode 0 = Paint. Erase/smooth extend via different _DensityMode values (P4+).
            this.brushCompute.SetInt("_DensityMode", 0);
            this.brushCompute.Dispatch(k, groups, groups, 1);

            // Queue throttled async writeback to the density layer.
            if (this.activeDensityLayer != null)
                this.densityEncoder.RequestAsync(this.activeDensityLayer, densityRT);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            {
                worldPoint = hit.point; return true;
            }
            foreach (var entry in painter.Tiles)
            {
                if (entry.tileAsset == null) continue;
                var origin2d = TerrainWorldGrid.TileOriginWorld(entry.coord);
                float midY   = (entry.tileAsset.minHeight + entry.tileAsset.maxHeight) * 0.5f;
                var plane    = new Plane(Vector3.up, new Vector3(origin2d.x, midY, origin2d.y));
                if (plane.Raycast(ray, out float dist) && dist > 0f && dist < 1e6f)
                {
                    worldPoint = ray.GetPoint(dist); return true;
                }
            }
            worldPoint = Vector3.zero;
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
    }
}
