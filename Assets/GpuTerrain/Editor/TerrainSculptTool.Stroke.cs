#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Stroke event handlers for <see cref="TerrainSculptTool"/> (partial).
    ///
    /// Uses <see cref="TileRtCache"/> for per-coord RT lifetime management.
    ///
    /// L2 fix: undo snapshot is pushed at first actual dispatch per coord (not pre-resolved),
    /// keeping "snapshot pushed ↔ coord in LastStrokedTileSet" in lockstep.
    ///
    /// M1 fix: OnWillBeDeactivated calls TeardownActiveStroke before releasing RTs so
    /// engines never hold a dangling RT as _HeightTex.
    /// </summary>
    internal sealed partial class TerrainSculptTool
    {
        // ── Per-stroke state ──────────────────────────────────────────────────

        private readonly TileRtCache rtCache = new TileRtCache();

        // Coords for which an undo snapshot was already pushed this stroke.
        private readonly HashSet<Vector2Int> undoPushedCoords  = new HashSet<Vector2Int>();

        // Coords that received at least one successful dispatch this stroke.
        private readonly HashSet<Vector2Int> strokeTouchedCoords = new HashSet<Vector2Int>();

        // Scratch list reused per resolve — avoids per-frame GC.
        private readonly List<Vector2Int> resolveResults = new List<Vector2Int>(TileRtCache.MAX_ENTRIES);

        // ── Mouse event handlers (called from OnToolGUI in main partial) ───────

        internal void HandleMouseDown(
            GpuTerrainRenderer renderer, Vector3 worldPoint, int controlId)
        {
            this.undoPushedCoords.Clear();
            this.strokeTouchedCoords.Clear();
            this.rtCache.ReleaseAll();            // discard any leftover RT from prior stroke

            this.resolveResults.Clear();
            TerrainPaintTargetResolver.Resolve(
                new Vector2(worldPoint.x, worldPoint.z),
                TerrainSculptState.BrushSize, residencySet: null, this.resolveResults);

            GUIUtility.hotControl = controlId;
            this.stroke!.BeginStroke(this.resolveResults);   // sets InStroke = true (no undo push)

            foreach (var coord in this.resolveResults)
                this.DispatchOneTile(renderer, worldPoint, coord, force: true);

            this.CommitLastStrokedState();
        }

        internal void HandleMouseDrag(GpuTerrainRenderer renderer, Vector3 worldPoint)
        {
            this.resolveResults.Clear();
            TerrainPaintTargetResolver.Resolve(
                new Vector2(worldPoint.x, worldPoint.z),
                TerrainSculptState.BrushSize, residencySet: null, this.resolveResults);

            foreach (var coord in this.resolveResults)
                this.DispatchOneTile(renderer, worldPoint, coord, force: false);

            this.CommitLastStrokedState();
        }

        internal void HandleMouseUp(GpuTerrainRenderer renderer)
        {
            this.TeardownActiveStroke(renderer);
            this.CommitLastStrokedState();
        }

        // ── Mid-stroke deactivation teardown (M1 fix) ─────────────────────────

        /// <summary>
        /// Shared teardown used by both HandleMouseUp and OnWillBeDeactivated.
        /// Finalises writeback and rebinds committed Texture2D on every touched tile
        /// so no engine is left holding a dangling RT as _HeightTex.
        /// Renderer may be null when called from OnWillBeDeactivated if it was destroyed.
        /// </summary>
        internal void TeardownActiveStroke(GpuTerrainRenderer? renderer)
        {
            if (renderer != null)
            {
                foreach (var coord in this.strokeTouchedCoords)
                {
                    var tile = FindTileForCoord(renderer, coord);
                    var gpu  = renderer.ResourcesForCoord(coord);
                    if (tile != null && gpu != null &&
                        this.rtCache.TryGet(coord, out var heightRT, out var splatRT))
                    {
                        this.writeback.RequestAsync(tile, gpu, heightRT, splatRT);
                    }
                    // Rebind committed Texture2D — safe because Upload reuses same object (T3 fix).
                    renderer.EndSculptPreview(coord);
                }
            }

            this.stroke!.EndInStroke();
            this.rtCache.ReleaseAll();
            this.strokeTouchedCoords.Clear();
            this.undoPushedCoords.Clear();
        }

        // ── Per-tile dispatch ─────────────────────────────────────────────────

        /// <summary>
        /// Ensure working RTs exist for coord (seeded on first touch), bind VTF preview,
        /// push undo snapshot on first touch, dispatch compute kernel + throttled writeback.
        /// Returns false if the coord is not in the renderer's tiles list or RT cap exceeded.
        /// </summary>
        private void DispatchOneTile(
            GpuTerrainRenderer renderer, Vector3 worldPoint, Vector2Int coord, bool force)
        {
            if (renderer.EngineForCoord(coord) == null) return;
            var tile = FindTileForCoord(renderer, coord);
            var gpu  = renderer.ResourcesForCoord(coord);
            if (tile == null || gpu == null) return;

            bool isFirstTouch = !this.strokeTouchedCoords.Contains(coord);

            if (!this.rtCache.GetOrCreate(coord, gpu, out var heightRT, out var splatRT))
                return;  // cap exceeded — warning already logged by TileRtCache

            // L2: push undo snapshot at first actual dispatch, not pre-resolve,
            // so snapshot ↔ LastStrokedTileSet stay in lockstep.
            if (isFirstTouch && !this.undoPushedCoords.Contains(coord))
            {
                TerrainSculptState.Undo.Push(tile);
                this.undoPushedCoords.Add(coord);
                renderer.BeginSculptPreview(coord, heightRT);
            }

            this.strokeTouchedCoords.Add(coord);
            this.stroke!.Dispatch(worldPoint, tile, this.brushCompute!, heightRT, splatRT);
            this.stroke.ThrottledWriteback(force, tile, gpu, heightRT, splatRT);
        }

        // ── Shared SSOT update ────────────────────────────────────────────────

        private void CommitLastStrokedState()
        {
            TerrainSculptState.LastStrokedTileSet.Clear();
            foreach (var c in this.strokeTouchedCoords)
                TerrainSculptState.LastStrokedTileSet.Add(c);

            Vector2Int? primary = null;
            foreach (var c in this.strokeTouchedCoords) { primary = c; break; }
            TerrainSculptState.LastStrokedCoord = primary;
        }
    }
}
