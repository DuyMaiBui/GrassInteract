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

        [MenuItem(ROOT + "Add Grass Layer (blades + 2 variants)")]
        private static void AddGrassLayer()
        {
            if (Selection.activeObject is not WorldMapAsset map)
            {
                Debug.LogWarning("[WorldPainter] Select a WorldMapAsset first.");
                return;
            }

            GrassLayer layer = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "Grass", variantCount: 2);
            Debug.Log($"[WorldPainter] Added '{layer.name}' with a procedural blade mesh + 2 seeded variants. " +
                      "Optionally assign per-variant _BaseMap textures; open the Control Panel to paint.", layer);
        }

        [MenuItem(ROOT + "Create Demo (splat + grass)")]
        private static void CreateDemo()
        {
            if (Selection.activeObject is not WorldMapAsset map)
            {
                Debug.LogWarning("[WorldPainter] Select a WorldMapAsset first.");
                return;
            }

            WorldMapAssetLifecycle.CreateDemoSurfaceLayers(map);
            Debug.Log("[WorldPainter] Created demo SurfaceLayers: 1 splat (assign albedos to its TerrainLayerSet) " +
                      "+ 1 grass (blades & 2 variants). Grass renders immediately if the map has tiles.", map);
        }
    }
}
