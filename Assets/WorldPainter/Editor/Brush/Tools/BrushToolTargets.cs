#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Resolves the active layer target for the current selection (unified SurfaceLayers SSOT).
    /// </summary>
    internal static class BrushToolTargets
    {
        /// <summary>
        /// The density <see cref="Texture2D"/> the brush should paint: the active unified GrassLayer's
        /// map (SurfaceLayers) when one is selected, else null.
        /// </summary>
        public static Texture2D? ResolveDensityTarget(WorldPainter painter)
        {
            return ResolveGrassDensityForTile(painter, Vector2Int.zero);
        }

        /// <summary>
        /// The active unified GrassLayer's density map for the given tile coordinate, or null
        /// when no grass layer is selected or no density texture exists for that tile.
        /// </summary>
        public static Texture2D? ResolveGrassDensityForTile(WorldPainter painter, Vector2Int coord)
        {
            if (WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.Meadow)
                return null;

            WorldMapAsset? map = painter.Map;
            if (map == null) return null;

            string id = WorldPainterState.ActiveLayerId;

            foreach (var sl in map.SurfaceLayers)
            {
                if (sl is GrassLayer g && g.name == id)
                    return g.GetTileDensity(coord);
            }
            return null;
        }

        /// <summary>
        /// The active unified <see cref="GrassLayer"/> from <c>map.SurfaceLayers</c>, or null when no
        /// grass layer is selected. Used by the brush stroke loop to drive live per-stroke rebuild.
        /// </summary>
        public static GrassLayer? ResolveActiveGrassLayer(WorldPainter painter)
        {
            if (WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.Meadow)
                return null;
            WorldMapAsset? map = painter.Map;
            if (map == null) return null;
            string id = WorldPainterState.ActiveLayerId;
            foreach (var sl in map.SurfaceLayers)
                if (sl is GrassLayer g && g.name == id) return g;
            return null;
        }

        /// <summary>
        /// The active unified <see cref="PropLayer"/> from <c>map.SurfaceLayers</c>, or null when no
        /// prop layer is selected. Mirror of <see cref="ResolveActiveGrassLayer"/>.
        /// </summary>
        public static PropLayer? ResolveActivePropLayerForStroke(WorldPainter painter)
        {
            if (WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.Prop)
                return null;
            WorldMapAsset? map = painter.Map;
            if (map == null) return null;
            string id = WorldPainterState.ActiveLayerId;
            foreach (var sl in map.SurfaceLayers)
                if (sl is PropLayer p && p.name == id) return p;
            return null;
        }

        /// <summary>
        /// The active unified <see cref="PropLayer"/> from <c>map.SurfaceLayers</c>, or null when:
        ///   - <see cref="WorldPainterState.ActiveLayerKind"/> is not <see cref="WorldPainterState.PaintLayerKind.Prop"/>, OR
        ///   - no <see cref="PropLayer"/> in <c>map.SurfaceLayers</c> has a name matching
        ///     <see cref="WorldPainterState.ActiveLayerId"/>.
        ///
        /// Callers (prop brush tools) should prefer this over <see cref="ResolveInstanceLayer"/>
        /// when the unified path is active, and fall back to <see cref="ResolveInstanceLayer"/>
        /// when this returns null.
        /// </summary>
        public static PropLayer? ResolvePropLayer(WorldPainter painter)
        {
            if (WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.Prop)
                return null;

            WorldMapAsset? map = painter.Map;
            if (map == null) return null;

            string id = WorldPainterState.ActiveLayerId;
            foreach (var sl in map.SurfaceLayers)
                if (sl is PropLayer pl && pl.name == id)
                    return pl;

            return null;
        }

        /// <summary>
        /// Phase 2b: resolves the per-tile alphamap that owns the active palette layer for
        /// painting/erasing. Returns the tuple (texture, channel) where channel ∈ [0,3] is
        /// the RGBA index within the alphamap; null when no palette layer is selected, the
        /// painter has no map, or the tile lacks the necessary alphamap slot.
        /// </summary>
        public static (Texture2D Texture, int AlphamapIndex, int Channel)? ResolveActiveAlphamap(
            WorldPainter painter, Vector2Int coord)
        {
            int paletteIndex = WorldPainterState.ActivePaletteIndex;
            if (paletteIndex < 0) return null;

            WorldMapAsset? map = painter.Map;
            if (map == null) return null;

            // Guard against the palette shrinking after a stroke started.
            if (paletteIndex >= map.TerrainPalette.Count) return null;

            TerrainTileAsset? tile = map.GetTile(coord);
            if (tile == null) return null;

            int alphamapIndex = paletteIndex / 4;
            int channel       = paletteIndex % 4;
            if (alphamapIndex >= tile.AlphamapCount) return null;

            Texture2D? tex = tile.Alphamaps[alphamapIndex];
            if (tex == null) return null;

            return (tex, alphamapIndex, channel);
        }
    }
}
