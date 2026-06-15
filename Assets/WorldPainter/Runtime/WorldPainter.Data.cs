#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldPainter;

namespace WorldPainter
{
    // ── Tier-A inline schema structs ──────────────────────────────────────────

    /// <summary>Global world grid dimensions (Tier-A config inline on WorldPainter).</summary>
    [Serializable]
    public struct WorldGrid
    {
        [Tooltip("Size of each terrain tile in world-space metres.")]
        public float tileSizeM;

        [Tooltip("Height RT resolution per tile (e.g. 257 = 257×257 R16).")]
        public int heightRes;

        [Tooltip("Splat RT resolution per tile (e.g. 512 = 512×512 RGBA32).")]
        public int splatRes;

        public static WorldGrid Default => new WorldGrid
        {
            tileSizeM = 256f,
            heightRes  = 257,
            splatRes   = 512,
        };
    }

    /// <summary>Coord → tile asset mapping for one terrain tile.</summary>
    [Serializable]
    public struct TileEntry
    {
        [Tooltip("Integer grid coordinate of this tile (X = column, Y = row).")]
        public Vector2Int coord;

        [Tooltip("Disk asset holding the R16 height + RGBA32 splat bytes for this tile.")]
        public TerrainTileAsset? tileAsset;
    }

    // ── WorldPainter Tier-A data (partial) ────────────────────────────────────

    public sealed partial class WorldPainter
    {
        // ── WorldMapAsset container (P2 — SSOT for tiles + layers) ───────────

        [Header("Map Container (P2)")]
        [Tooltip("The WorldMapAsset that owns all tiles and scatter layer defs. " +
                 "When assigned, tiles + layers are read from this container rather than " +
                 "the inline lists below (which remain for backwards-compat until P3 removes them).")]
        [SerializeField] private WorldMapAsset? map;

        /// <summary>
        /// The referenced WorldMapAsset container. When non-null, scatter and terrain
        /// reads go through this asset's APIs (GetTile / EnumerateTiles / SurfaceLayers).
        /// </summary>
        public WorldMapAsset? Map
        {
            get => this.map;
            set
            {
                if (this.map == value) return;
                this.map = value;
#if UNITY_EDITOR
                // Edit-mode authoring: assigning a map must immediately (re)build engines and
                // repaint. Otherwise the newly created tile(s) only appear on the next incidental
                // Scene/Game-view repaint — which often never happens right after the factory
                // creates them, leaving the user with a created-but-not-rendered tile.
                // Play mode is left to LateUpdate's TryBuild (this guard avoids premature builds
                // during scene load).
                if (!Application.isPlaying)
                {
                    this.TryBuild();
                    UnityEditor.SceneView.RepaintAll();
                }
#endif
            }
        }

        // ── World grid ────────────────────────────────────────────────────────

        [Header("World Grid")]
        [SerializeField] private WorldGrid worldGrid = WorldGrid.Default;

        /// <summary>Global grid dimensions (tile size, height/splat resolutions).</summary>
        public WorldGrid WorldGridConfig
        {
            get => this.worldGrid;
            set => this.worldGrid = value;
        }

        // ── Tile roster (Tier B refs) ─────────────────────────────────────────

        [Header("Tiles")]
        [SerializeField] private List<TileEntry> tiles = new();

        /// <summary>All registered tiles: coord → <see cref="TerrainTileAsset"/> ref.</summary>
        public List<TileEntry> Tiles => this.tiles;

        // ── Deferred to P6 ────────────────────────────────────────────────────

        // brushPresets : List<BrushPreset ref>   — deferred to P6
        // BrushPreset type does NOT exist yet; do not create it here.
    }
}
