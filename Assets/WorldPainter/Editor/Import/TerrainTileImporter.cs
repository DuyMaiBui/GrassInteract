#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Minimal R16 .raw file → TerrainTileAsset importer for Phase 0.
    /// Full authoring (EXR support, multi-tile batch, sculpt round-trip) is Phase 5.
    ///
    /// Usage: call ImportR16Raw from an Editor menu or the Phase 5 importer window.
    /// Creates the asset at the given output path via AssetDatabase.CreateAsset.
    /// </summary>
    public static class TerrainTileImporter
    {
        /// <summary>
        /// Import a raw R16 .raw file (little-endian ushort grid) into a TerrainTileAsset
        /// and save it as a Unity asset.
        /// </summary>
        /// <param name="rawFilePath">Absolute or project-relative path to the .raw file.</param>
        /// <param name="outputAssetPath">Asset-relative output path, e.g. "Assets/Terrain/Tile_0_0.asset".</param>
        /// <param name="heightRes">Texel count per edge (file must contain heightRes² × 2 bytes).</param>
        /// <param name="tileCoord">Integer tile coordinate to assign.</param>
        /// <param name="minHeight">World Y at R16 = 0.</param>
        /// <param name="maxHeight">World Y at R16 = 65535.</param>
        /// <returns>The created TerrainTileAsset, or null if import failed.</returns>
        public static TerrainTileAsset? ImportR16Raw(
            string rawFilePath,
            string outputAssetPath,
            int heightRes,
            Vector2Int tileCoord,
            float minHeight = 0f,
            float maxHeight = 512f)
        {
            if (!File.Exists(rawFilePath))
            {
                Debug.LogError($"[TerrainTileImporter] File not found: {rawFilePath}");
                return null;
            }

            int expectedBytes = heightRes * heightRes * TerrainHeightFormat.BYTES_PER_SAMPLE;
            byte[] rawBytes = File.ReadAllBytes(rawFilePath);
            if (rawBytes.Length != expectedBytes)
            {
                Debug.LogError($"[TerrainTileImporter] File size mismatch: expected {expectedBytes} bytes " +
                               $"for {heightRes}² R16, got {rawBytes.Length}. Path: {rawFilePath}");
                return null;
            }

            var asset = ScriptableObject.CreateInstance<TerrainTileAsset>();
            asset.tileCoord  = tileCoord;
            asset.heightRes  = heightRes;
            asset.heightData = rawBytes;
            asset.minHeight  = minHeight;
            asset.maxHeight  = maxHeight;
            // (Phase 3 cleanup — splat fields removed; per-tile palette weights live in alphamaps[].)

            AssetDatabase.CreateAsset(asset, outputAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return asset;
        }
    }
}
