#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// One entry in the bake manifest: maps a tile coordinate to its standalone baked
    /// <see cref="TerrainTileAsset"/> asset path (relative to the project root, e.g.
    /// <c>Assets/WorldPainter/Baked/MyMap/Tile_0_0.asset</c>).
    /// </summary>
    [Serializable]
    public sealed class TileBakeManifestEntry
    {
        /// <summary>Integer tile coordinate.</summary>
        [SerializeField] public Vector2Int coord;

        /// <summary>
        /// Project-relative asset path to the standalone baked <see cref="TerrainTileAsset"/>.
        /// Suitable for <c>AssetDatabase.LoadAssetAtPath</c> (Editor) or Addressables (runtime).
        /// </summary>
        [SerializeField] public string assetPath = string.Empty;
    }

    /// <summary>
    /// Bake manifest ScriptableObject: coord → standalone baked <see cref="TerrainTileAsset"/>
    /// asset path. Created by <c>WorldMapBaker.Bake</c>; consumed by
    /// <see cref="WorldPainter"/> (<c>WorldPainter.Bake.cs</c> partial) and
    /// <see cref="TerrainStreamingManager"/> at runtime.
    ///
    /// ── Design ───────────────────────────────────────────────────────────────
    /// The manifest is a <em>standalone</em> asset (not a sub-asset) so Addressables
    /// can load it independently of any individual tile. The baked tiles are also
    /// standalone — one asset file per tile — enabling true per-tile streaming.
    ///
    /// ── Editor Play path ─────────────────────────────────────────────────────
    /// When no manifest is assigned on the <see cref="WorldPainter"/> component, the
    /// runtime falls back to reading the <see cref="WorldMapAsset"/> container
    /// directly (fast iteration without a prior bake step).
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Tile Bake Manifest", fileName = "TileBakeManifest")]
    public sealed class TileBakeManifest : ScriptableObject, ISerializationCallbackReceiver
    {
        // ── Serialized entries ────────────────────────────────────────────────

        [SerializeField]
        private List<TileBakeManifestEntry> entries = new();

        /// <summary>All manifest entries (read-only for runtime callers).</summary>
        public IReadOnlyList<TileBakeManifestEntry> Entries => this.entries;

        /// <summary>Number of tiles in this manifest.</summary>
        public int Count => this.entries.Count;

        // ── Fast lookup ───────────────────────────────────────────────────────

        [NonSerialized]
        private Dictionary<Vector2Int, string> pathByCoord = new();

        /// <summary>Returns the baked asset path for <paramref name="coord"/>, or null if absent.</summary>
        public string? GetAssetPath(Vector2Int coord) =>
            this.pathByCoord.TryGetValue(coord, out string? path) ? path : null;

        // ── ISerializationCallbackReceiver ────────────────────────────────────

        public void OnBeforeSerialize() { /* entries list is canonical */ }

        public void OnAfterDeserialize() => this.RebuildLookup();

        private void OnEnable() => this.RebuildLookup();

        private void RebuildLookup()
        {
            this.pathByCoord = new Dictionary<Vector2Int, string>(this.entries.Count);
            foreach (var e in this.entries)
            {
                if (!string.IsNullOrEmpty(e.assetPath))
                    this.pathByCoord[e.coord] = e.assetPath;
            }
        }

        // ── Internal mutation API (used only by WorldMapBaker) ────────────────

        /// <summary>
        /// Appends a new entry or overwrites an existing one for <paramref name="coord"/>.
        /// Called exclusively by <c>WorldMapBaker</c> during a bake.
        /// </summary>
        internal void SetEntry(Vector2Int coord, string assetPath)
        {
            for (int i = 0; i < this.entries.Count; i++)
            {
                if (this.entries[i].coord == coord)
                {
                    this.entries[i].assetPath = assetPath;
                    this.pathByCoord[coord]   = assetPath;
                    return;
                }
            }

            this.entries.Add(new TileBakeManifestEntry { coord = coord, assetPath = assetPath });
            this.pathByCoord[coord] = assetPath;
        }

        /// <summary>
        /// Removes the entry for <paramref name="coord"/>. No-op if absent.
        /// Called by <c>WorldMapBaker</c> to prune entries for tiles that no longer exist.
        /// </summary>
        internal void RemoveEntry(Vector2Int coord)
        {
            for (int i = this.entries.Count - 1; i >= 0; i--)
            {
                if (this.entries[i].coord == coord)
                {
                    this.entries.RemoveAt(i);
                    this.pathByCoord.Remove(coord);
                    return;
                }
            }
        }

        /// <summary>Clears all entries. Called by <c>WorldMapBaker</c> before a full bake.</summary>
        internal void Clear()
        {
            this.entries.Clear();
            this.pathByCoord.Clear();
        }
    }
}
