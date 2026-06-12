#nullable enable
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// One-time migration menu: <c>Tools/WorldPainter/Migrate from GpuTerrainRenderer</c>.
    ///
    /// Reads a selected/scene <see cref="GpuTerrainRenderer"/>'s tiles and
    /// a scene <see cref="TerrainScatterConfig"/>'s layers, then builds the
    /// WorldPainter Tier-A data on a (new or selected) <see cref="WorldPainter"/> component.
    ///
    /// REFERENCES EXISTING ASSETS IN PLACE — does not copy/move/delete anything.
    /// Shows a dry-run report first; only applies on explicit confirm.
    /// </summary>
    internal static class WorldPainterMigration
    {
        private const string MENU = "Tools/WorldPainter/Migrate from GpuTerrainRenderer";

        // ── Menu item ─────────────────────────────────────────────────────────

        [MenuItem(MENU)]
        private static void MigrateMenu()
        {
            // 1. Locate renderer and scatter config.
            var renderer = Object.FindFirstObjectByType<GpuTerrainRenderer>();
            var config   = FindScatterConfig();

            if (renderer == null && config == null)
            {
                EditorUtility.DisplayDialog("WorldPainter Migration",
                    "No GpuTerrainRenderer or TerrainScatterConfig found in the scene.\n" +
                    "Open the scene that contains the terrain before migrating.", "OK");
                return;
            }

            // 2. Build dry-run report.
            var report = BuildReport(renderer, config);

            // 3. Show report and ask for confirmation.
            bool proceed = EditorUtility.DisplayDialog(
                "WorldPainter Migration — Dry Run",
                report + "\n\nApply migration? Originals are NOT modified.",
                "Apply", "Cancel");

            if (!proceed) return;

            // 4. Locate or create WorldPainter target.
            var painter = FindOrCreateWorldPainter();
            if (painter == null)
            {
                Debug.LogError("[WorldPainterMigration] Could not find or create a WorldPainter.");
                return;
            }

            // 5. Apply.
            ApplyMigration(painter, renderer, config);
        }

        [MenuItem(MENU, validate = true)]
        private static bool MigrateMenuValidate() => true;

        // ── Report builder ────────────────────────────────────────────────────

        private static string BuildReport(
            GpuTerrainRenderer? renderer,
            TerrainScatterConfig? config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== WorldPainter Migration Dry Run ===");
            sb.AppendLine();

            // Tiles.
            var tiles = GetTiles(renderer);
            sb.AppendLine($"Tiles from GpuTerrainRenderer ({tiles.Count}):");
            if (tiles.Count == 0)
                sb.AppendLine("  (none found)");
            foreach (var t in tiles)
                sb.AppendLine($"  coord={t.tileCoord}  asset='{t.name}'");

            sb.AppendLine();

            // Scatter layers.
            var layers = GetLayers(config);
            sb.AppendLine($"ScatterLayers from TerrainScatterConfig ({layers.Count}):");
            if (layers.Count == 0)
                sb.AppendLine("  (none found)");
            foreach (var l in layers)
                sb.AppendLine($"  layer='{l.name}'  type={l.GetType().Name}");

            sb.AppendLine();
            sb.AppendLine("Target: existing WorldPainter in scene, or a new one on a new GameObject.");
            sb.AppendLine("Originals are REFERENCED IN PLACE — nothing is copied, moved, or deleted.");

            return sb.ToString();
        }

        // ── Migration apply ───────────────────────────────────────────────────

        private static void ApplyMigration(
            WorldPainter painter,
            GpuTerrainRenderer? renderer,
            TerrainScatterConfig? config)
        {
            Undo.RecordObject(painter, "WorldPainter Migration");

            // Map tiles.
            var tileAssets = GetTiles(renderer);
            painter.Tiles.Clear();
            foreach (var tileAsset in tileAssets)
            {
                painter.Tiles.Add(new TileEntry
                {
                    coord     = tileAsset.tileCoord,
                    tileAsset = tileAsset,
                });
            }

            // Carry tile size + height/splat resolution from source tiles into WorldGridConfig.
            ApplyWorldGridConfig(painter, tileAssets);

            // Map scatter layers.
            var layers = GetLayers(config);
            painter.ScatterLayers.Clear();
            foreach (var layer in layers)
                painter.ScatterLayers.Add(layer);

            EditorUtility.SetDirty(painter);

            // Trigger render build so tiles show immediately.
            painter.TryBuild();

            Debug.Log($"[WorldPainterMigration] Done: {tileAssets.Count} tile(s), " +
                      $"{layers.Count} scatter layer(s) migrated onto '{painter.name}'.");
        }

        // ── WorldGrid config carry-over ───────────────────────────────────────

        /// <summary>
        /// Reads tile size + height/splat resolution from the first source tile and
        /// writes them into <see cref="WorldPainter.WorldGridConfig"/>.
        ///
        /// If tiles disagree (mixed resolutions), the first tile's values win and a
        /// warning is logged so the user can decide whether to re-author.
        /// If no tiles are present, WorldGrid.Default is kept unchanged.
        /// </summary>
        private static void ApplyWorldGridConfig(
            WorldPainter painter,
            List<TerrainTileAsset> tileAssets)
        {
            if (tileAssets.Count == 0) return;

            var first = tileAssets[0];
            var derived = new WorldGrid
            {
                tileSizeM = TerrainWorldGrid.TILE_SIZE_M,
                heightRes  = first.heightRes,
                splatRes   = first.splatRes,
            };

            // Validate consistency across tiles and warn if they diverge.
            bool allMatch = true;
            foreach (var t in tileAssets)
            {
                if (t.heightRes != derived.heightRes || t.splatRes != derived.splatRes)
                {
                    allMatch = false;
                    break;
                }
            }

            if (!allMatch)
            {
                Debug.LogWarning(
                    $"[WorldPainterMigration] Source tiles have mixed resolutions. " +
                    $"Using first tile ({first.name}): heightRes={derived.heightRes}, " +
                    $"splatRes={derived.splatRes}. Review WorldPainter.WorldGridConfig manually.");
            }

            painter.WorldGridConfig = derived;
        }

        // ── Scene queries ─────────────────────────────────────────────────────

        private static List<TerrainTileAsset> GetTiles(GpuTerrainRenderer? renderer)
        {
            var result = new List<TerrainTileAsset>();
            if (renderer == null) return result;
            foreach (var tile in renderer.Tiles)
                if (tile != null) result.Add(tile);
            return result;
        }

        private static List<ScatterLayer> GetLayers(TerrainScatterConfig? config)
        {
            var result = new List<ScatterLayer>();
            if (config == null) return result;
            foreach (var l in config.Layers)
                if (l != null) result.Add(l);
            return result;
        }

        private static TerrainScatterConfig? FindScatterConfig()
        {
            // Look for a TerrainScatterConfig referenced by any ScatterField in the scene.
            var fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            foreach (var sf in fields)
            {
                var cfg = sf.Config;
                if (cfg != null) return cfg;
            }
            return null;
        }

        private static WorldPainter? FindOrCreateWorldPainter()
        {
            var existing = Object.FindFirstObjectByType<WorldPainter>();
            if (existing != null) return existing;

            // Create a new GameObject + WorldPainter.
            var go = new GameObject("WorldPainter");
            Undo.RegisterCreatedObjectUndo(go, "Create WorldPainter");
            return go.AddComponent<WorldPainter>();
        }
    }
}
