#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using GrassInteract;

namespace GpuTerrain
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

    /// <summary>Per-splat-layer surface definition (≤4 layers → RGBA32 splat RT).</summary>
    [Serializable]
    public struct SplatLayerDef
    {
        [Tooltip("Display name shown in the layer stack.")]
        public string name;

        [Tooltip("Albedo texture.")]
        public Texture2D? albedo;

        [Tooltip("Normal map texture.")]
        public Texture2D? normal;

        [Tooltip("UV tiling (X=scale, Y=scale) applied in the terrain shader.")]
        public Vector2 tiling;
    }

    // ── WorldPainter Tier-A data (partial) ────────────────────────────────────

    public sealed partial class WorldPainter
    {
        /// <summary>Hard cap on splat layers — one RGBA32 channel per layer.</summary>
        public const int MAX_SPLAT_LAYERS = 4;

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

        // ── Splat layers (Tier A inline, ≤4) ─────────────────────────────────

        [Header("Splat Layers (≤4)")]
        [SerializeField] private List<SplatLayerDef> splatLayers = new();

        [Tooltip("Index of the currently active splat layer for painting (0-based).")]
        [SerializeField] private int activeSplatLayerIndex = 0;

        /// <summary>Surface splat definitions. Limited to 4 by the RGBA32 splat RT layout.</summary>
        public List<SplatLayerDef> SplatLayers => this.splatLayers;

        /// <summary>Index of the active splat layer used by the paint kernel (0-based).</summary>
        public int ActiveSplatLayerIndex
        {
            get => this.activeSplatLayerIndex;
            set => this.activeSplatLayerIndex = Mathf.Clamp(value, 0, Mathf.Max(0, this.splatLayers.Count - 1));
        }

        /// <summary>
        /// Adds a new splat layer.
        /// Throws <see cref="InvalidOperationException"/> if the 4-layer cap would be exceeded.
        /// Error is surfaced rather than silently dropped (errors-over-fallbacks rule).
        /// </summary>
        /// <exception cref="InvalidOperationException">Raised when splatLayers.Count == MAX_SPLAT_LAYERS.</exception>
        public void AddSplatLayer(SplatLayerDef layer)
        {
            if (this.splatLayers.Count >= MAX_SPLAT_LAYERS)
                throw new InvalidOperationException(
                    $"[WorldPainter] Cannot add splat layer '{layer.name}': " +
                    $"the RGBA32 splat RT supports at most {MAX_SPLAT_LAYERS} layers. " +
                    $"Remove an existing layer before adding a new one.");

            this.splatLayers.Add(layer);
        }

        // ── Scatter layer refs (Tier C) ───────────────────────────────────────

        [Header("Scatter Layers")]
        [SerializeField] private List<ScatterLayer> scatterLayers = new();

        /// <summary>Grass/prop scatter layer asset references (LODs + density maps + records).</summary>
        public List<ScatterLayer> ScatterLayers => this.scatterLayers;

        // ── Biome presets (Tier-C refs) — P5 ─────────────────────────────────

        [Header("Biome Presets")]
        [SerializeField] private List<BiomePreset> biomes = new();

        /// <summary>Biome preset references. Tier-A WorldPainter → Tier-C BiomePreset refs.</summary>
        public List<BiomePreset> Biomes => this.biomes;

        // ── Deferred to P6 ────────────────────────────────────────────────────

        // brushPresets : List<BrushPreset ref>   — deferred to P6
        // BrushPreset type does NOT exist yet; do not create it here.
    }
}
