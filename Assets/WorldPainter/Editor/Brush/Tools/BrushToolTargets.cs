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
        /// The density <see cref="Texture2D"/> the brush should paint: the active unified GrassLayer
        /// variant's map (SurfaceLayers) when one is selected, else null.
        /// </summary>
        public static Texture2D? ResolveDensityTarget(WorldPainter painter)
        {
            return ResolveActiveGrassVariantDensity(painter);
        }

        /// <summary>
        /// The active unified GrassLayer variant's density map for the given tile coordinate, or null
        /// when no grass variant is selected or no density texture exists for that tile.
        /// </summary>
        public static Texture2D? ResolveGrassVariantDensityForTile(WorldPainter painter, Vector2Int coord)
        {
            if (WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.Meadow)
                return null;

            WorldMapAsset? map = painter.Map;
            if (map == null) return null;

            string id = WorldPainterState.ActiveLayerId;
            int vi    = WorldPainterState.ActiveGrassVariantIndex;

            foreach (var sl in map.SurfaceLayers)
            {
                if (sl is GrassLayer g && g.name == id)
                {
                    if (vi >= 0 && vi < g.Palette.Count)
                        return GrassLayer.GetTileDensity(g.Palette[vi], coord);
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// The active unified GrassLayer variant's density map, or null when no grass variant is
        /// selected. Active iff <see cref="WorldPainterState.ActiveLayerKind"/> is Meadow and the
        /// active id names a <see cref="GrassLayer"/> in <c>map.SurfaceLayers</c>.
        /// Uses (0,0) tile coord as a legacy fallback — prefer
        /// <see cref="ResolveGrassVariantDensityForTile"/> for per-tile paint routing.
        /// </summary>
        public static Texture2D? ResolveActiveGrassVariantDensity(WorldPainter painter)
        {
            if (WorldPainterState.ActiveLayerKind != WorldPainterState.PaintLayerKind.Meadow)
                return null;

            WorldMapAsset? map = painter.Map;
            if (map == null) return null;

            string id = WorldPainterState.ActiveLayerId;
            int vi    = WorldPainterState.ActiveGrassVariantIndex;

            foreach (var sl in map.SurfaceLayers)
            {
                if (sl is GrassLayer g && g.name == id)
                {
                    if (vi >= 0 && vi < g.Palette.Count)
                    {
                        // Return the first available tile's density texture as a legacy fallback.
                        var tiles = g.Palette[vi].densityTiles;
                        if (tiles != null && tiles.Length > 0) return tiles[0].tex;
                    }
                    return null;
                }
            }
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
    }
}
