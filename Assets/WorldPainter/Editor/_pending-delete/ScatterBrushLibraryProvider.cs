#nullable enable
using UnityEditor;
using UnityEngine;

namespace GrassInteract.Editor
{
    /// <summary>
    /// Locates or creates the single project-wide <see cref="ScatterBrushLibrary"/> asset.
    /// The library is stored at a fixed, well-known path inside the Editor-only Settings folder
    /// so it is never included in a player build.
    ///
    /// Access the library exclusively through <see cref="Library"/>; do NOT create the asset
    /// manually via <c>Assets &gt; Create</c>.
    /// </summary>
    public static class ScatterBrushLibraryProvider
    {
        private const string SETTINGS_FOLDER = "Assets/GrassInteract/Editor/Settings";
        private const string LIBRARY_ASSET_PATH = SETTINGS_FOLDER + "/ScatterBrushLibrary.asset";

        private static ScatterBrushLibrary? cachedLibrary;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the single project-wide <see cref="ScatterBrushLibrary"/>, creating it on
        /// first access if it does not yet exist.
        /// The asset is persisted at <c>Assets/GrassInteract/Editor/Settings/ScatterBrushLibrary.asset</c>.
        /// </summary>
        public static ScatterBrushLibrary Library
        {
            get
            {
                if (ScatterBrushLibraryProvider.cachedLibrary != null)
                    return ScatterBrushLibraryProvider.cachedLibrary;

                // Try to load an already-existing asset first — never create a duplicate.
                var loaded = AssetDatabase.LoadAssetAtPath<ScatterBrushLibrary>(LIBRARY_ASSET_PATH);
                if (loaded != null)
                {
                    ScatterBrushLibraryProvider.cachedLibrary = loaded;
                    return ScatterBrushLibraryProvider.cachedLibrary;
                }

                // Asset does not exist yet — ensure the folder hierarchy exists, then create it.
                EnsureSettingsFolder();

                var library = ScriptableObject.CreateInstance<ScatterBrushLibrary>();
                AssetDatabase.CreateAsset(library, LIBRARY_ASSET_PATH);
                AssetDatabase.SaveAssets();

                Debug.Log("[ScatterBrushLibraryProvider] Created project brush library at: " + LIBRARY_ASSET_PATH);

                ScatterBrushLibraryProvider.cachedLibrary = library;
                return ScatterBrushLibraryProvider.cachedLibrary;
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Ensures <c>Assets/GrassInteract/Editor/Settings</c> exists, creating each
        /// missing folder segment via <see cref="AssetDatabase.CreateFolder"/>.
        /// This avoids a direct <c>Directory.CreateDirectory</c> call so Unity tracks the
        /// folders in its asset database.
        /// </summary>
        private static void EnsureSettingsFolder()
        {
            // Walk the path segments; create any that are missing.
            // Segments: Assets → GrassInteract → Editor → Settings
            string[] segments = SETTINGS_FOLDER.Split('/');
            string current = segments[0]; // "Assets" always exists

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, segments[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogError($"[ScatterBrushLibraryProvider] Failed to create folder '{next}'. " +
                                       "The brush library asset could not be persisted.");
                        return;
                    }
                }
                current = next;
            }
        }

        /// <summary>
        /// Clears the in-memory cache. Called automatically by the domain-reload
        /// <see cref="InitializeOnLoadMethod"/> so the provider re-loads from disk
        /// after every script recompile.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ClearCacheOnDomainReload()
        {
            ScatterBrushLibraryProvider.cachedLibrary = null;
        }
    }
}
