#nullable enable
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Menu entries to author unified <c>SurfaceLayers</c> on the selected <see cref="WorldMapAsset"/>.
    /// A stop-gap authoring path until the SurfaceLayers inspector cards land — see the plan's Phase 3b.
    /// </summary>
    internal static class WorldPainterSurfaceLayerMenu
    {
        private const string ROOT = "Tools/WorldPainter/Surface Layers/";

        [MenuItem(ROOT + "Add Splat Layer")]
        private static void AddSplatLayer()
        {
            if (Selection.activeObject is not WorldMapAsset map)
            {
                Debug.LogWarning("[WorldPainter] Select a WorldMapAsset first.");
                return;
            }

            SplatLayer layer = WorldMapAssetLifecycle.AddSplatLayer(map, "Ground");
            Debug.Log($"[WorldPainter] Added '{layer.name}'. Assign albedo textures (≤4) on its " +
                      "TerrainLayerSet sub-asset, then paint with the splat tool to blend.", layer.LayerSet);
        }

        [MenuItem(ROOT + "Add Grass Layer (2 seeded variants)")]
        private static void AddGrassLayer()
        {
            if (Selection.activeObject is not WorldMapAsset map)
            {
                Debug.LogWarning("[WorldPainter] Select a WorldMapAsset first.");
                return;
            }

            GrassLayer layer = WorldMapAssetLifecycle.AddGrassLayer(map, "Grass");
            WorldMapAssetLifecycle.AddGrassVariant(map, layer, "VariantA", seedFullDensity: true);
            WorldMapAssetLifecycle.AddGrassVariant(map, layer, "VariantB", seedFullDensity: true);
            Debug.Log($"[WorldPainter] Added '{layer.name}' with 2 seeded variants. Assign a blade Mesh " +
                      "to its Render → LODs (and optional per-variant textures) to see grass scatter.", layer);
        }
    }
}
