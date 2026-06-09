#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Factory for creating and persisting density-map texture assets.
    /// Centralises the R8 / readable / uncompressed import-settings contract so there is
    /// exactly ONE place in the codebase that knows how to create or persist a density map.
    ///
    /// Risk mitigation (R8 importer-before-read, score 12):
    ///   1. <see cref="CreateBlank"/> writes an initial PNG, sets the <see cref="TextureImporter"/>
    ///      (R8 / isReadable=true / Uncompressed) and calls
    ///      <see cref="AssetDatabase.ImportAsset"/> with <see cref="ImportAssetOptions.ForceUpdate"/>
    ///      BEFORE any <c>GetPixels</c> / <c>LoadAssetAtPath</c> read.
    ///   2. The method then re-loads the asset via <see cref="AssetDatabase.LoadAssetAtPath"/>,
    ///      and asserts <see cref="Texture2D.isReadable"/> so failures surface immediately rather
    ///      than as silent corrupt reads.
    ///
    /// <see cref="PersistPixels"/> is extracted verbatim from the old
    /// <c>DensityPaintTool.SaveToAsset</c> — it is now the sole call site for PNG persistence,
    /// so both the factory and the paint tool share the same implementation.
    /// </summary>
    internal static class DensityMapFactory
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const string DEFAULT_FOLDER = "Assets/GrassInteract/DensityMaps";
        private const string DEFAULT_NAME   = "DensityMap";

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a blank R8, readable, uncompressed density-map texture at a path derived from
        /// <paramref name="owner"/>'s asset location and returns the re-imported, readable asset.
        ///
        /// <para>
        /// The import-settings are applied and <see cref="AssetDatabase.ImportAsset"/> is called
        /// BEFORE any <c>LoadAssetAtPath</c> or <c>GetPixels</c> read — this is the mandatory
        /// ordering to avoid the "not readable" fault.
        /// </para>
        /// </summary>
        /// <param name="size">Texture resolution (square). Clamped to at least 1.</param>
        /// <param name="owner">
        /// The project asset that will reference this density map (e.g. a
        /// <see cref="DensityScatterLayer"/> sub-asset). Used to derive a sibling folder path.
        /// May be null; falls back to <see cref="DEFAULT_FOLDER"/>.
        /// </param>
        /// <returns>
        /// The newly created, re-imported <see cref="Texture2D"/> with
        /// <see cref="Texture2D.isReadable"/> == <c>true</c>; or <c>null</c> if creation fails.
        /// </returns>
        internal static Texture2D? CreateBlank(int size, Object? owner)
        {
            size = Mathf.Max(1, size);

            // ── Resolve target folder ─────────────────────────────────────────
            string folder = DEFAULT_FOLDER;
            if (owner != null)
            {
                string ownerPath = AssetDatabase.GetAssetPath(owner);
                if (!string.IsNullOrEmpty(ownerPath))
                    folder = Path.GetDirectoryName(ownerPath)?.Replace('\\', '/') ?? DEFAULT_FOLDER;
            }

            EnsureFolder(folder);

            // ── Generate a unique file path ───────────────────────────────────
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{DEFAULT_NAME}.png");

            // ── Write an initial all-black PNG ────────────────────────────────
            var tmp = new Texture2D(size, size, TextureFormat.RGBA32, false);
            // All pixels default to (0,0,0,1) — blank/no-density.
            var black = new Color[size * size];
            for (int i = 0; i < black.Length; ++i)
                black[i] = new Color(0f, 0f, 0f, 1f);
            tmp.SetPixels(black);
            tmp.Apply(false);
            byte[] png = tmp.EncodeToPNG();
            Object.DestroyImmediate(tmp);
            if (png == null)
            {
                Debug.LogError("[DensityMapFactory] CreateBlank: EncodeToPNG returned null — texture could not be created.");
                return null;
            }

            // Write the file and import it once so AssetDatabase knows about it.
            File.WriteAllBytes(assetPath, png);
            AssetDatabase.ImportAsset(assetPath); // initial import (no force yet — importer not configured)

            // ── Set importer BEFORE reading pixels ────────────────────────────
            // Risk R8 mitigation: configure the TextureImporter and ForceUpdate BEFORE
            // any LoadAssetAtPath / GetPixels read.
            if (!ApplyR8ImportSettings(assetPath))
            {
                Debug.LogError($"[DensityMapFactory] CreateBlank: failed to apply R8 import settings to '{assetPath}'.");
                AssetDatabase.DeleteAsset(assetPath);
                return null;
            }

            // ── Load the re-imported asset ────────────────────────────────────
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null)
            {
                Debug.LogError($"[DensityMapFactory] CreateBlank: LoadAssetAtPath returned null for '{assetPath}'.");
                return null;
            }

            // ── Assert isReadable ─────────────────────────────────────────────
            if (!tex.isReadable)
            {
                Debug.LogError($"[DensityMapFactory] CreateBlank: texture at '{assetPath}' is NOT readable after " +
                               "import — something overrode the TextureImporter settings. Check for platform overrides.");
                return null;
            }

            Debug.Log($"[DensityMapFactory] Created blank R8 density map at '{assetPath}' ({size}x{size}).");
            return tex;
        }

        /// <summary>
        /// Persists the in-memory pixel array back to the texture's source PNG so painted edits
        /// survive a domain reload. This is the SSOT for density-map persistence — extracted
        /// verbatim from the old <c>DensityPaintTool.SaveToAsset</c>.
        ///
        /// <para>
        /// Paint→domain-reload→pixels-survive reasoning:
        ///   On stroke end, <c>DensityPaintTool.EndStroke</c> calls
        ///   <c>SetPixels + Apply</c> on the in-memory <see cref="Texture2D"/> (GPU upload) then
        ///   calls this method. This method writes the PNG to disk and calls
        ///   <see cref="AssetDatabase.ImportAsset"/> with <see cref="ImportAssetOptions.ForceUpdate"/>
        ///   which re-imports the file using the existing R8 / readable / uncompressed importer
        ///   settings. The importer settings were established by <see cref="CreateBlank"/> (or the
        ///   original manual import) and are stored in the <c>.meta</c> file — they persist across
        ///   reloads. After domain reload Unity re-deserialises the asset from the same PNG + meta,
        ///   so <c>GetPixels</c> reads the painted data correctly.
        /// </para>
        /// </summary>
        /// <param name="map">The density map being persisted. Must be a project asset.</param>
        /// <param name="pixels">The current pixel data to write.</param>
        /// <param name="w">Texture width.</param>
        /// <param name="h">Texture height.</param>
        internal static void PersistPixels(Texture2D map, Color[] pixels, int w, int h)
        {
            string path = AssetDatabase.GetAssetPath(map);
            if (string.IsNullOrEmpty(path)) return; // runtime-only texture — nothing to persist

            // Encode via a temp RGBA32 texture (R8 is not directly EncodeToPNG-able).
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tmp.SetPixels(pixels);
            tmp.Apply(false);
            byte[] png = tmp.EncodeToPNG();
            Object.DestroyImmediate(tmp);
            if (png == null) return;

            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); // re-imports with the map's R8 settings
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Sets the TextureImporter at <paramref name="assetPath"/> to R8 / isReadable=true /
        /// Uncompressed, then calls <see cref="AssetDatabase.ImportAsset"/> with ForceUpdate.
        /// Returns <c>true</c> on success, <c>false</c> if the importer cannot be found.
        /// </summary>
        private static bool ApplyR8ImportSettings(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[DensityMapFactory] No TextureImporter at '{assetPath}'.");
                return false;
            }

            importer.textureType        = TextureImporterType.Default;
            importer.isReadable         = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            // Force single-channel R8 via the platform-default format override.
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.overridden = true;
            platform.format     = TextureImporterFormat.R8;
            importer.SetPlatformTextureSettings(platform);

            EditorUtility.SetDirty(importer);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        /// <summary>
        /// Ensures every folder segment of <paramref name="folderPath"/> exists in the
        /// AssetDatabase, creating any missing segments via
        /// <see cref="AssetDatabase.CreateFolder"/>.
        /// </summary>
        private static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string current = segments[0]; // "Assets" always exists

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, segments[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogError($"[DensityMapFactory] EnsureFolder: failed to create '{next}'.");
                        return;
                    }
                }
                current = next;
            }
        }
    }
}
