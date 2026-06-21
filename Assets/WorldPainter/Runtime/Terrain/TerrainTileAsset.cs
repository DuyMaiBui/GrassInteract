#nullable enable
using System;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// ScriptableObject holding one terrain tile's raw height and splat data.
    /// Does NOT store Texture2D references — GPU textures are built on demand by
    /// TerrainTileGpuResources and released when the tile is evicted.
    ///
    /// ── Height data ───────────────────────────────────────────────────────────
    /// heightData: byte[] of (heightRes × heightRes × 2) bytes, R16 little-endian.
    /// Decode formula (SSOT: TerrainHeightFormat.DecodeHeight):
    ///   worldY = minHeight + (raw / 65535.0) * (maxHeight - minHeight)
    ///
    /// ── Splat data ────────────────────────────────────────────────────────────
    /// splatData: byte[] of (splatRes × splatRes × 4) bytes, RGBA32.
    /// SSOT channel → layer mapping (Phase 2 fragment shader cites this):
    ///   R = layer 0 (base ground)
    ///   G = layer 1 (grass overlay)
    ///   B = layer 2 (rock / cliff)
    ///   A = layer 3 (path / road)
    /// Weights are stored as 8-bit unsigned [0,255] where 255 = full weight.
    /// Phase 2 normalises the four weights in the fragment shader.
    ///
    /// ── Shared-edge convention (see TerrainWorldGrid) ─────────────────────────
    /// heightData stores heightRes texels per edge; the last row/column is the
    /// shared border texel with the adjacent tile (1-texel overlap).
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Terrain Tile", fileName = "TerrainTile")]
    public sealed class TerrainTileAsset : ScriptableObject
    {
        // ── Tile identity ──────────────────────────────────────────────────────
        /// <summary>Integer tile coordinate in the world grid (see TerrainWorldGrid).</summary>
        [SerializeField] public Vector2Int tileCoord = Vector2Int.zero;

        // ── Height ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Raw R16 height bytes: heightRes × heightRes × 2 bytes, row-major, little-endian.
        /// Index: (z * heightRes + x) * 2 = low byte, +1 = high byte.
        /// Edit only via editor brushes — the raw array is hidden in the Inspector.
        /// </summary>
        [HideInInspector] [SerializeField] public byte[] heightData = System.Array.Empty<byte>();

        /// <summary>Number of texels per edge in the height map (including shared boundary texel).</summary>
        [SerializeField] public int heightRes = TerrainWorldGrid.DEFAULT_HEIGHT_RES;

        /// <summary>World Y at R16 = 0. Part of the decode formula SSOT.</summary>
        [SerializeField] public float minHeight = 0f;

        /// <summary>World Y at R16 = 65535. Part of the decode formula SSOT.</summary>
        [SerializeField] public float maxHeight = 512f;

        // (Phase 3 cleanup — splatData / splatRes / IsSplatValid removed. Per-tile palette
        // weights live in the `alphamaps[]` Texture2D array below.)

        // ── Baked normal (Phase 5b) ────────────────────────────────────────────
        /// <summary>
        /// RG octahedral-encoded normal, one byte per channel per texel: normalRes × normalRes × 2 bytes.
        /// Baked alongside heightData by WorldMapBaker.BakeOneTile.  Null or empty on pre-Phase-5 tiles
        /// (runtime falls back to 4-tap height derivation via _WP_LIVE_SCULPT keyword).
        ///
        /// Encoding: R = oct.x mapped [−1,1]→[0,255], G = oct.y mapped [−1,1]→[0,255].
        /// The shader reconstructs Z from oct-fold and normalizes.  Resolution matches heightRes.
        /// </summary>
        [HideInInspector] [SerializeField] public byte[] normalData = System.Array.Empty<byte>();

        /// <summary>Resolution of the baked normal map in texels per edge (matches heightRes).</summary>
        public int NormalRes => this.heightRes;

        /// <summary>Returns true when normalData is valid (non-empty, correct size for heightRes).</summary>
        public bool IsNormalValid =>
            this.normalData != null && this.normalData.Length == this.heightRes * this.heightRes * 2;

        // ── Validation helpers ─────────────────────────────────────────────────

        /// <summary>Expected byte length of heightData = heightRes² × 2.</summary>
        public int ExpectedHeightBytes => this.heightRes * this.heightRes * TerrainHeightFormat.BYTES_PER_SAMPLE;

        /// <summary>Returns true if heightData is non-null and has the expected length.</summary>
        public bool IsHeightValid =>
            this.heightData != null && this.heightData.Length == this.ExpectedHeightBytes;

        // ── Per-tile alphamaps (TerrainLayer palette weights) ──────────────────
        //
        // One RGBA32 Texture2D sub-asset per 4 palette layers. alphamaps[k] holds weights
        // for palette indices [4k .. 4k+3] on its RGBA channels respectively. The terrain
        // shader samples these to blend palette[i].diffuseTexture into the final color.
        // Created lazily by the editor lifecycle when the palette grows or a tile is added.

        [Tooltip("Per-tile alphamap textures — one RGBA32 per 4 TerrainPalette entries.")]
        [SerializeField] private Texture2D[] alphamaps = System.Array.Empty<Texture2D>();

        /// <summary>Read-only view of this tile's alphamap textures.</summary>
        public System.Collections.Generic.IReadOnlyList<Texture2D> Alphamaps => this.alphamaps;

        /// <summary>Number of alphamap textures currently owned by this tile.</summary>
        public int AlphamapCount => this.alphamaps != null ? this.alphamaps.Length : 0;

        /// <summary>Editor lifecycle: replaces the alphamap array.</summary>
        internal void EditorSetAlphamaps(Texture2D[] maps) =>
            this.alphamaps = maps ?? System.Array.Empty<Texture2D>();

        /// <summary>Editor lifecycle: gets the mutable alphamap array (do NOT cache).</summary>
        internal Texture2D[] EditorAlphamaps => this.alphamaps;
    }
}
