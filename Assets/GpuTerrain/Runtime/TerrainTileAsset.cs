#nullable enable
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// ScriptableObject holding one terrain tile's raw height and splat data.
    /// Does NOT store Texture2D references — GPU textures are built on demand by
    /// TerrainTileGpuResources and released when the tile is evicted.
    ///
    /// ── Height data ─────────────────────────────────────────────────────────
    /// heightData: byte[] of (heightRes × heightRes × 2) bytes, R16 little-endian.
    /// Decode formula (SSOT: TerrainHeightFormat.DecodeHeight):
    ///   worldY = minHeight + (raw / 65535.0) * (maxHeight - minHeight)
    ///
    /// ── Splat data ───────────────────────────────────────────────────────────
    /// splatData: byte[] of (splatRes × splatRes × 4) bytes, RGBA32.
    /// SSOT channel → layer mapping (Phase 2 fragment shader cites this):
    ///   R = layer 0 (base ground)
    ///   G = layer 1 (grass overlay)
    ///   B = layer 2 (rock / cliff)
    ///   A = layer 3 (path / road)
    /// Weights are stored as 8-bit unsigned [0,255] where 255 = full weight.
    /// Phase 2 normalises the four weights in the fragment shader.
    ///
    /// ── Shared-edge convention (see TerrainWorldGrid) ────────────────────────
    /// heightData stores heightRes texels per edge; the last row/column is the
    /// shared border texel with the adjacent tile (1-texel overlap).
    /// </summary>
    [CreateAssetMenu(menuName = "GpuTerrain/Terrain Tile", fileName = "TerrainTile")]
    public sealed class TerrainTileAsset : ScriptableObject
    {
        // ── Tile identity ─────────────────────────────────────────────────────
        /// <summary>Integer tile coordinate in the world grid (see TerrainWorldGrid).</summary>
        [SerializeField] public Vector2Int tileCoord = Vector2Int.zero;

        // ── Height ────────────────────────────────────────────────────────────
        /// <summary>
        /// Raw R16 height bytes: heightRes × heightRes × 2 bytes, row-major, little-endian.
        /// Index: (z * heightRes + x) * 2 = low byte, +1 = high byte.
        /// </summary>
        [SerializeField] public byte[] heightData = System.Array.Empty<byte>();

        /// <summary>Number of texels per edge in the height map (including shared boundary texel).</summary>
        [SerializeField] public int heightRes = TerrainWorldGrid.DEFAULT_HEIGHT_RES;

        /// <summary>World Y at R16 = 0. Part of the decode formula SSOT.</summary>
        [SerializeField] public float minHeight = 0f;

        /// <summary>World Y at R16 = 65535. Part of the decode formula SSOT.</summary>
        [SerializeField] public float maxHeight = 512f;

        // ── Splat ─────────────────────────────────────────────────────────────
        /// <summary>
        /// RGBA32 splat bytes: splatRes × splatRes × 4 bytes, row-major.
        /// Channel mapping SSOT: R=layer0, G=layer1, B=layer2, A=layer3 (see class doc).
        /// </summary>
        [SerializeField] public byte[] splatData = System.Array.Empty<byte>();

        /// <summary>Number of texels per edge in the splat map.</summary>
        [SerializeField] public int splatRes = TerrainWorldGrid.DEFAULT_SPLAT_RES;

        // ── Validation helpers ────────────────────────────────────────────────

        /// <summary>Expected byte length of heightData = heightRes² × 2.</summary>
        public int ExpectedHeightBytes => this.heightRes * this.heightRes * TerrainHeightFormat.BYTES_PER_SAMPLE;

        /// <summary>Expected byte length of splatData = splatRes² × 4 (RGBA32).</summary>
        public int ExpectedSplatBytes => this.splatRes * this.splatRes * 4;

        /// <summary>Returns true if heightData is non-null and has the expected length.</summary>
        public bool IsHeightValid =>
            this.heightData != null && this.heightData.Length == this.ExpectedHeightBytes;

        /// <summary>Returns true if splatData is non-null and has the expected length.</summary>
        public bool IsSplatValid =>
            this.splatData != null && this.splatData.Length == this.ExpectedSplatBytes;
    }
}
