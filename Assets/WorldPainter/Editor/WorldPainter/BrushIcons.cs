#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Resolves per-tool icon textures for the BrushDock contextual tool palette. Loads
    /// 16x16 PNGs from <c>Assets/WorldPainter/Editor/Resources/Icons/Brush/&lt;toolId&gt;.png</c>,
    /// with a <c>_d.png</c> suffix variant for the pro/dark editor skin. Missing icons are
    /// cached as null so callers fall back to text labels without re-hitting AssetDatabase
    /// on every repaint.
    ///
    /// Layout convention (drop these PNGs in to enable icons):
    ///   <code>
    ///   Assets/WorldPainter/Editor/Resources/Icons/Brush/
    ///     paint.png      paint_d.png
    ///     erase.png      erase_d.png
    ///     smooth.png     smooth_d.png
    ///     flatten.png    flatten_d.png
    ///     raise.png      raise_d.png
    ///     lower.png      lower_d.png
    ///     place.png      place_d.png
    ///     single.png     single_d.png
    ///   </code>
    /// Tool ids supplied by callers are mapped to file stems by stripping the kind prefix
    /// (e.g. <c>"density.paint"</c> → <c>"paint"</c>, <c>"height.raise"</c> → <c>"raise"</c>).
    /// </summary>
    internal static class BrushIcons
    {
        private const string ICONS_FOLDER = "Assets/WorldPainter/Editor/Resources/Icons/Brush";

        // Cache survives a session; cleared on domain reload (static fields reset).
        private static readonly Dictionary<string, Texture2D?> cache = new();

        /// <summary>Returns the icon for <paramref name="toolId"/>, or null when no PNG is authored.</summary>
        public static Texture2D? For(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return null;
            if (cache.TryGetValue(toolId, out var cached)) return cached;

            string stem    = StemOf(toolId);
            string suffix  = EditorGUIUtility.isProSkin ? "_d" : string.Empty;
            string path    = $"{ICONS_FOLDER}/{stem}{suffix}.png";
            var    tex     = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            // Light-skin fallback to dark variant if only one is authored, and vice versa.
            if (tex == null)
            {
                string altPath = $"{ICONS_FOLDER}/{stem}.png";
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(altPath);
            }

            cache[toolId] = tex;
            return tex;
        }

        /// <summary>Clears the cache (call after authoring/importing new PNGs).</summary>
        public static void Invalidate() => cache.Clear();

        // ── Tool-id → file stem ──────────────────────────────────────────────
        // Strips the layer-kind prefix so e.g. "density.paint" → "paint". Falls back to
        // the original id when there's no dot, so future single-token ids still work.
        private static string StemOf(string toolId)
        {
            int dot = toolId.IndexOf('.');
            return dot < 0 ? toolId : toolId[(dot + 1)..];
        }
    }
}
