#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// Watches the AssetDatabase for imports that touch a <see cref="TerrainScatterConfig"/> .asset
    /// file (the parent asset path is what appears in the imported array even for sub-asset edits).
    ///
    /// When a config is reimported, every enabled <see cref="ScatterField"/> that references that
    /// config is scheduled for a full <see cref="ScatterField.Rebuild"/>. Multiple simultaneous
    /// imports are coalesced into a single <c>EditorApplication.delayCall</c> via a HashSet dedup,
    /// so a bulk reimport triggers at most one rebuild per field per frame.
    ///
    /// Rebuild is NOT called synchronously inside <c>OnPostprocessAllAssets</c> — doing so during
    /// an AssetPostprocessor callback causes assertion warnings and occasional Unity instability.
    ///
    /// Performance: the filter path (EndsWith ".asset" + LoadAssetAtPath check) is fast. The
    /// FindObjectsByType call only runs when at least one .asset matches, keeping it out of the
    /// hot-path for non-config imports (shaders, textures, audio, etc.).
    /// </summary>
    internal sealed class ScatterAssetPostprocessor : AssetPostprocessor
    {
        // Dedup set — cleared after every flush.
        private static readonly HashSet<ScatterField> dirtyFields = new();
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] imported,
            string[] deleted,
            string[] moved,
            string[] movedFrom)
        {
            if (imported.Length == 0)
                return;

            // Filter: only process .asset paths that resolve to a TerrainScatterConfig.
            // A sub-asset texture edited externally re-imports its PARENT .asset path —
            // matching the parent is sufficient and avoids sub-asset address parsing.
            foreach (string path in imported)
            {
                if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var config = AssetDatabase.LoadAssetAtPath<TerrainScatterConfig>(path);
                if (config == null)
                    continue;

                // Phase C: apply naming convention whenever a config is reimported.
                ApplyNamingConvention(config);

                // Find all enabled ScatterFields in the active scene(s) that reference this config.
                ScatterField[] fields = Object.FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
                foreach (ScatterField f in fields)
                {
                    if (f != null && f.isActiveAndEnabled && f.Config == config)
                        dirtyFields.Add(f);
                }
            }

            if (dirtyFields.Count > 0 && !scheduled)
            {
                scheduled = true;
                EditorApplication.delayCall += FlushRebuilds;
            }
        }

        private static void FlushRebuilds()
        {
            scheduled = false;

            foreach (ScatterField f in dirtyFields)
            {
                if (f != null && f.isActiveAndEnabled)
                    f.Rebuild();
            }

            dirtyFields.Clear();
            SceneView.RepaintAll();
        }

        // Phase C: cascade-rename sub-assets when a layer is renamed or reordered.
        // Delegates to ScatterAssetNaming.ScheduleNamingConvention to avoid mutating
        // the AssetDatabase inside the postprocessor callback (Unity assertion warning risk).
        private static void ApplyNamingConvention(TerrainScatterConfig cfg)
        {
            if (cfg != null)
                ScatterAssetNaming.ScheduleNamingConvention(cfg);
        }
    }
}
