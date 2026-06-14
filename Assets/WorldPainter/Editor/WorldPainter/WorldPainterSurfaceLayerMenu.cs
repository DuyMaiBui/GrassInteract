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

        // (Phase 3 cleanup — "Add Splat Layer" menu removed. Use the BrushDock TerrainPalette strip.)

        [MenuItem(ROOT + "Add Grass Layer (blades + seeded density)")]
        private static void AddGrassLayer()
        {
            if (Selection.activeObject is not WorldMapAsset map)
            {
                Debug.LogWarning("[WorldPainter] Select a WorldMapAsset first.");
                return;
            }

            GrassLayer layer = WorldMapAssetLifecycle.AddGrassLayerWithBlades(map, "Grass");
            Debug.Log($"[WorldPainter] Added '{layer.name}' with a procedural blade mesh and per-tile density. " +
                      "Open the Control Panel to paint.", layer);
        }

        [MenuItem(ROOT + "Create Demo (grass)")]
        private static void CreateDemo()
        {
            if (Selection.activeObject is not WorldMapAsset map)
            {
                Debug.LogWarning("[WorldPainter] Select a WorldMapAsset first.");
                return;
            }

            WorldMapAssetLifecycle.CreateDemoSurfaceLayers(map);
            Debug.Log("[WorldPainter] Created demo SurfaceLayers: 1 grass (blades, seeded density per tile).", map);
        }
    }
}
