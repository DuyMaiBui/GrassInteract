#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Bake step: iterates <see cref="WorldMapAsset.EnumerateTiles"/> and writes one
    /// standalone <see cref="TerrainTileAsset"/> per tile into a bake output folder.
    /// Emits a <see cref="TileBakeManifest"/> mapping each coord → asset path.
    ///
    /// ── Full bake vs incremental bake ───────────────────────────────────────
    /// <see cref="BakeAll"/>  — bakes every tile; rebuilds and re-saves all output assets.
    /// <see cref="BakeTile"/> — bakes a single tile (used by incremental bake on dirty tiles).
    ///
    /// ── Output layout ────────────────────────────────────────────────────────
    /// <c>outputFolder/</c> (e.g. <c>Assets/WorldPainter/Baked/MyMap/</c>)
    ///   Tile_0_0.asset   — standalone TerrainTileAsset (coord 0,0)
    ///   Tile_1_0.asset   — standalone TerrainTileAsset (coord 1,0)
    ///   ...
    ///   BakeManifest.asset — TileBakeManifest  (coord → asset path)
    ///
    /// ── Editor-only ──────────────────────────────────────────────────────────
    /// All methods call AssetDatabase APIs — only valid inside the Unity Editor.
    /// </summary>
    public static class WorldMapBaker
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const string MANIFEST_FILENAME = "BakeManifest.asset";

        // ── Public entry points ───────────────────────────────────────────────

        /// <summary>
        /// Full bake: iterates all tiles in <paramref name="map"/> and writes a standalone
        /// <see cref="TerrainTileAsset"/> per tile plus a <see cref="TileBakeManifest"/> to
        /// <paramref name="outputFolder"/>.
        ///
        /// Existing baked assets for coords still in the map are overwritten (data copy).
        /// Baked assets for coords that are no longer in the map are deleted and pruned
        /// from the manifest.
        ///
        /// Returns the manifest asset (already saved).
        /// </summary>
        /// <param name="map">Source map container.</param>
        /// <param name="outputFolder">
        /// Project-relative folder path (e.g. <c>Assets/WorldPainter/Baked/MyMap</c>).
        /// Created if it does not exist.
        /// </param>
        /// <returns>The saved <see cref="TileBakeManifest"/>.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="map"/> is null.</exception>
        public static TileBakeManifest BakeAll(WorldMapAsset map, string outputFolder)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            EnsureFolder(outputFolder);

            // Load or create the manifest asset.
            TileBakeManifest manifest = LoadOrCreateManifest(outputFolder);

            // Track which coords exist in the current map so we can prune stale entries.
            var currentCoords = new HashSet<Vector2Int>(map.EnumerateTileCoords());

            // Bake each tile.
            int bakCount = 0;
            foreach (Vector2Int coord in map.EnumerateTileCoords())
            {
                TerrainTileAsset? sourceTile = map.GetTile(coord);
                if (sourceTile == null) continue;

                string tilePath = TileAssetPath(outputFolder, coord);
                BakeOneTile(sourceTile, tilePath);
                manifest.SetEntry(coord, tilePath);
                bakCount++;
            }

            // Prune manifest entries for coords that no longer exist in the map.
            PruneStaleEntries(manifest, currentCoords, outputFolder);

            SaveManifest(manifest);

            Debug.Log($"[WorldMapBaker] Full bake complete: {bakCount} tile(s) → '{outputFolder}'.");
            return manifest;
        }

        /// <summary>
        /// Incremental bake: bakes only the single tile at <paramref name="coord"/> in
        /// <paramref name="map"/>. Updates the manifest in place.
        ///
        /// This is the low-cost path called by <see cref="WorldPainterIncrementalBakeDriver"/>
        /// for dirty tiles after a brush stroke.
        ///
        /// If the tile does not exist in the map, no-ops and returns false.
        /// </summary>
        /// <param name="map">Source map container.</param>
        /// <param name="coord">Tile coordinate to re-bake.</param>
        /// <param name="outputFolder">Bake output folder (same as used for the full bake).</param>
        /// <param name="manifest">Existing manifest to update (loaded by the caller).</param>
        /// <returns>True if the tile was baked; false if coord is not in the map.</returns>
        public static bool BakeTile(
            WorldMapAsset      map,
            Vector2Int         coord,
            string             outputFolder,
            TileBakeManifest   manifest)
        {
            if (map == null)     throw new ArgumentNullException(nameof(map));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            TerrainTileAsset? sourceTile = map.GetTile(coord);
            if (sourceTile == null) return false;

            EnsureFolder(outputFolder);

            string tilePath = TileAssetPath(outputFolder, coord);
            BakeOneTile(sourceTile, tilePath);
            manifest.SetEntry(coord, tilePath);
            SaveManifest(manifest);

            Debug.Log($"[WorldMapBaker] Incremental bake: tile {coord} → '{tilePath}'.");
            return true;
        }

        /// <summary>
        /// Loads the <see cref="TileBakeManifest"/> from <paramref name="outputFolder"/> if one
        /// exists, otherwise returns null. Does NOT create a new manifest.
        /// </summary>
        public static TileBakeManifest? TryLoadManifest(string outputFolder)
        {
            string manifestPath = ManifestPath(outputFolder);
            return AssetDatabase.LoadAssetAtPath<TileBakeManifest>(manifestPath);
        }

        // ── Core bake helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Copies the source tile's data into a standalone TerrainTileAsset at
        /// <paramref name="tilePath"/>. Creates the asset if it does not exist;
        /// overwrites data if it does.
        /// </summary>
        private static void BakeOneTile(TerrainTileAsset sourceTile, string tilePath)
        {
            // Load existing baked asset or create a new one.
            var baked = AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(tilePath);
            bool isNew = baked == null;
            if (isNew)
            {
                baked = ScriptableObject.CreateInstance<TerrainTileAsset>();
            }

            // Copy all data fields from the source tile.
            CopyTileData(sourceTile, baked!);

            if (isNew)
            {
                AssetDatabase.CreateAsset(baked, tilePath);
            }
            else
            {
                EditorUtility.SetDirty(baked);
            }
        }

        /// <summary>
        /// Deep-copies all data from <paramref name="src"/> into <paramref name="dst"/>.
        /// The name is left unchanged (the baked asset has its own filename-based name).
        /// </summary>
        internal static void CopyTileData(TerrainTileAsset src, TerrainTileAsset dst)
        {
            dst.tileCoord = src.tileCoord;
            dst.heightRes = src.heightRes;
            dst.minHeight = src.minHeight;
            dst.maxHeight = src.maxHeight;

            // Deep-copy byte arrays.
            dst.heightData = src.heightData != null
                ? (byte[])src.heightData.Clone()
                : Array.Empty<byte>();
            // (Phase 3 cleanup — splatData copy removed; per-tile palette weights live in alphamaps[].)

            // Deep-copy density channels via JsonUtility round-trip (safe for plain-data structs).
            // We cannot call AllocateDensityChannel on dst because it's internal to lifecycle.
            // Instead, we use the serialized form directly via EditorJsonUtility copy.
            string json = EditorJsonUtility.ToJson(src);
            EditorJsonUtility.FromJsonOverwrite(json, dst);

            // Re-overwrite identity fields that must stay as in dst (tileCoord already set above).
            dst.tileCoord = src.tileCoord;
        }

        // ── Manifest management ───────────────────────────────────────────────

        private static TileBakeManifest LoadOrCreateManifest(string outputFolder)
        {
            string path = ManifestPath(outputFolder);
            var existing = AssetDatabase.LoadAssetAtPath<TileBakeManifest>(path);
            if (existing != null) return existing;

            var manifest = ScriptableObject.CreateInstance<TileBakeManifest>();
            AssetDatabase.CreateAsset(manifest, path);
            return manifest;
        }

        private static void SaveManifest(TileBakeManifest manifest)
        {
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssetIfDirty(manifest);
        }

        private static void PruneStaleEntries(
            TileBakeManifest  manifest,
            HashSet<Vector2Int> currentCoords,
            string            outputFolder)
        {
            // Collect stale coords (entries whose tiles no longer exist in the map).
            var stale = new List<Vector2Int>();
            foreach (var entry in manifest.Entries)
            {
                if (!currentCoords.Contains(entry.coord))
                    stale.Add(entry.coord);
            }

            foreach (Vector2Int coord in stale)
            {
                string tilePath = TileAssetPath(outputFolder, coord);
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(tilePath)))
                    AssetDatabase.DeleteAsset(tilePath);
                manifest.RemoveEntry(coord);
            }
        }

        // ── Path helpers ──────────────────────────────────────────────────────

        private static string ManifestPath(string outputFolder) =>
            $"{outputFolder}/{MANIFEST_FILENAME}";

        /// <summary>
        /// Project-relative path for the baked standalone tile asset at <paramref name="coord"/>.
        /// Uses the same sign-safe naming as <see cref="WorldMapAssetLifecycle.TileSubAssetName"/>.
        /// </summary>
        public static string TileAssetPath(string outputFolder, Vector2Int coord) =>
            $"{outputFolder}/{TileFileName(coord)}";

        /// <summary>
        /// Filename (with extension) for the baked standalone tile asset at <paramref name="coord"/>.
        /// Sign-safe: negative coordinates use "n" prefix (e.g. <c>Tile_n1_0.asset</c>).
        /// </summary>
        public static string TileFileName(Vector2Int coord)
        {
            string xStr = coord.x < 0 ? $"n{-coord.x}" : $"{coord.x}";
            string yStr = coord.y < 0 ? $"n{-coord.y}" : $"{coord.y}";
            return $"Tile_{xStr}_{yStr}.asset";
        }

        private static void EnsureFolder(string folderPath)
        {
            // Unity requires each path segment to exist.
            string[] parts = folderPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
