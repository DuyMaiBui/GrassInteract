#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Resolves the scatter-layer target for the active selection, supporting BOTH selection
    /// paths: the P5 SSOT (<c>ActiveLayerId</c>, set by the splat/scatter palette) and the
    /// legacy index path (<c>ActiveScatterIndex</c>, set by the layer stack). Prefers the id
    /// match, then falls back to the index — mirroring the original BindAndDispatch lookups.
    /// </summary>
    internal static class BrushToolTargets
    {
        /// <summary>The active density (meadow) scatter layer, or null when none/other.</summary>
        public static DensityScatterLayer? ResolveDensityLayer(WorldPainter painter)
        {
            string id = WorldPainterState.ActiveLayerId;
            foreach (var layer in painter.ScatterLayers)
                if (layer is DensityScatterLayer dl && dl.name == id)
                    return dl;

            int idx = WorldPainterState.ActiveScatterIndex(painter);
            if (idx >= 0 && idx < painter.ScatterLayers.Count)
                return painter.ScatterLayers[idx] as DensityScatterLayer;

            return null;
        }

        /// <summary>
        /// The density <see cref="Texture2D"/> the brush should paint: the active unified GrassLayer
        /// variant's map (SurfaceLayers) when one is selected, else the legacy DensityScatterLayer's map.
        /// </summary>
        public static Texture2D? ResolveDensityTarget(WorldPainter painter)
        {
            Texture2D? variantMap = ResolveActiveGrassVariantDensity(painter);
            if (variantMap != null) return variantMap;
            return ResolveDensityLayer(painter)?.DensityMap;
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

        /// <summary>The active instance (prop) scatter layer, or null when none/other.</summary>
        public static InstanceScatterLayer? ResolveInstanceLayer(WorldPainter painter)
        {
            string id = WorldPainterState.ActiveLayerId;
            foreach (var layer in painter.ScatterLayers)
                if (layer is InstanceScatterLayer il && il.name == id)
                    return il;

            int idx = WorldPainterState.ActiveScatterIndex(painter);
            if (idx >= 0 && idx < painter.ScatterLayers.Count)
                return painter.ScatterLayers[idx] as InstanceScatterLayer;

            return null;
        }
    }
}
