#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — INCREMENTAL tile add/remove for the live render.
    ///
    /// The frozen <see cref="TryBuild"/> (WorldPainter.Render.cs) is all-or-nothing: it
    /// <c>DisposeEngines()</c> (tears down EVERY tile's GPU engine + re-uploads every tile) then
    /// rebuilds all tiles. Editor authoring (the tile add/remove overlay) only changes ONE tile,
    /// so routing it through TryBuild rebuilds the whole world — a visible, wasteful hitch.
    ///
    /// These methods reuse the existing per-tile builder <c>BuildOneTileAsset</c> (which APPENDS
    /// one engine without clearing the others) and add a matching per-tile teardown, so adding or
    /// removing a neighbour tile touches only that tile's GPU resources.
    ///
    /// This is a NEW partial file — <c>WorldPainter.cs</c> and <c>WorldPainter.Render.cs</c> are
    /// frozen and are not modified. Same partial class ⇒ access to the private engine lists,
    /// <c>coordToIndex</c>, <c>BuildOneTileAsset</c>, <c>ResolveInfra</c>, and the <c>IsBuilt</c>
    /// setter.
    /// </summary>
    public sealed partial class WorldPainter
    {
        /// <summary>
        /// Builds + uploads a single newly-added tile into the live render WITHOUT rebuilding the
        /// existing tiles. Falls back to a full <see cref="TryBuild"/> only when nothing is built
        /// yet (first tile, or infra not yet resolved). No-op in play mode (play-mode tile topology
        /// is streaming-driven).
        /// </summary>
        internal void AddTileToRender(TerrainTileAsset? tile)
        {
            if (tile == null) return;
            if (Application.isPlaying) return;

            if (!this.IsBuilt)
            {
                // Nothing built yet (or infra unresolved) — the normal build path includes this tile.
                this.TryBuild();
            }
            else if (!this.coordToIndex.ContainsKey(tile.tileCoord))
            {
                if (!this.ResolveInfra()) return;
                this.BuildOneTileAsset(tile);          // appends one engine; existing engines untouched
                this.IsBuilt = this.engines.Count > 0;
            }

#if UNITY_EDITOR
            // AddTileToRender only built the new tile's TERRAIN engine. Force the edit-mode scatter
            // to rebuild so the new tile's grass engine is also built (RebuildSurfaceLayers iterates
            // every tile) — otherwise the new tile renders ground but no grass until a density stroke.
            this.editScatterBuilt = false;
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        /// <summary>
        /// Tears down a single removed tile's GPU engine/resources WITHOUT rebuilding the rest, then
        /// re-indexes <c>coordToIndex</c> for the tiles whose list slot shifted down. No-op in play
        /// mode, or when no engine exists for <paramref name="coord"/>.
        /// </summary>
        internal void RemoveTileFromRender(Vector2Int coord)
        {
            if (Application.isPlaying) return;
            if (!this.coordToIndex.TryGetValue(coord, out int idx)) return;

            this.engines[idx].Dispose();
            this.gpuResources[idx].Dispose();
            this.engines.RemoveAt(idx);
            this.gpuResources.RemoveAt(idx);
            this.coordToIndex.Remove(coord);

            // RemoveAt shifted every slot after idx down by one — fix each affected coord's index.
            var affected = new List<Vector2Int>(this.coordToIndex.Keys);
            foreach (var c in affected)
                if (this.coordToIndex[c] > idx)
                    this.coordToIndex[c] -= 1;

            this.IsBuilt = this.engines.Count > 0;

#if UNITY_EDITOR
            // Rebuild edit-mode scatter so the removed tile's grass engine is disposed too.
            this.editScatterBuilt = false;
            UnityEditor.SceneView.RepaintAll();
#endif
        }
    }
}
