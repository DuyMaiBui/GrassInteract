#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// One R8 density channel keyed by layer ID. Stored as a sub-struct list on
    /// <see cref="TerrainTileAsset.densityChannels"/>.
    /// densityData is (WorldMapGrid.DENSITY_RES Ã— WorldMapGrid.DENSITY_RES) bytes, single-channel R8.
    /// </summary>
    [Serializable]
    public sealed class TileDensityChannel
    {
        /// <summary>Unique string ID matching the owning <see cref="ScatterLayer"/>'s name/ID.</summary>
        [SerializeField] public string layerId = string.Empty;

        /// <summary>
        /// R8 density bytes: WorldMapGrid.DENSITY_RES Ã— WorldMapGrid.DENSITY_RES (256Ã—256 = 65536 bytes).
        /// 0 = no density, 255 = full density.
        /// </summary>
        [HideInInspector] [SerializeField] public byte[] densityData = Array.Empty<byte>();
    }


    /// <summary>
    /// ScriptableObject holding one terrain tile's raw height and splat data.
    /// Does NOT store Texture2D references â€” GPU textures are built on demand by
    /// TerrainTileGpuResources and released when the tile is evicted.
    ///
    /// â”€â”€ Height data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// heightData: byte[] of (heightRes Ã— heightRes Ã— 2) bytes, R16 little-endian.
    /// Decode formula (SSOT: TerrainHeightFormat.DecodeHeight):
    ///   worldY = minHeight + (raw / 65535.0) * (maxHeight - minHeight)
    ///
    /// â”€â”€ Splat data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// splatData: byte[] of (splatRes Ã— splatRes Ã— 4) bytes, RGBA32.
    /// SSOT channel â†’ layer mapping (Phase 2 fragment shader cites this):
    ///   R = layer 0 (base ground)
    ///   G = layer 1 (grass overlay)
    ///   B = layer 2 (rock / cliff)
    ///   A = layer 3 (path / road)
    /// Weights are stored as 8-bit unsigned [0,255] where 255 = full weight.
    /// Phase 2 normalises the four weights in the fragment shader.
    ///
    /// â”€â”€ Shared-edge convention (see TerrainWorldGrid) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    /// heightData stores heightRes texels per edge; the last row/column is the
    /// shared border texel with the adjacent tile (1-texel overlap).
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Terrain Tile", fileName = "TerrainTile")]
    public sealed class TerrainTileAsset : ScriptableObject
    {
        // â”€â”€ Tile identity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>Integer tile coordinate in the world grid (see TerrainWorldGrid).</summary>
        [SerializeField] public Vector2Int tileCoord = Vector2Int.zero;

        // â”€â”€ Height â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Raw R16 height bytes: heightRes Ã— heightRes Ã— 2 bytes, row-major, little-endian.
        /// Index: (z * heightRes + x) * 2 = low byte, +1 = high byte.
        /// Edit only via editor brushes â€” the raw array is hidden in the Inspector.
        /// </summary>
        [HideInInspector] [SerializeField] public byte[] heightData = System.Array.Empty<byte>();

        /// <summary>Number of texels per edge in the height map (including shared boundary texel).</summary>
        [SerializeField] public int heightRes = TerrainWorldGrid.DEFAULT_HEIGHT_RES;

        /// <summary>World Y at R16 = 0. Part of the decode formula SSOT.</summary>
        [SerializeField] public float minHeight = 0f;

        /// <summary>World Y at R16 = 65535. Part of the decode formula SSOT.</summary>
        [SerializeField] public float maxHeight = 512f;

        // â”€â”€ Splat â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// RGBA32 splat bytes: splatRes Ã— splatRes Ã— 4 bytes, row-major.
        /// Channel mapping SSOT: R=layer0, G=layer1, B=layer2, A=layer3 (see class doc).
        /// Edit only via editor brushes â€” the raw array is hidden in the Inspector.
        /// </summary>
        [HideInInspector] [SerializeField] public byte[] splatData = System.Array.Empty<byte>();

        /// <summary>Number of texels per edge in the splat map.</summary>
        [SerializeField] public int splatRes = TerrainWorldGrid.DEFAULT_SPLAT_RES;

        // â”€â”€ Density channels (layerId â†’ R8 256Ã—256) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Per-layer density channels. One entry per active <see cref="ScatterLayer"/> at the time
        /// the layer was added to the map. Managed by WorldMapAssetLifecycle â€” do NOT add/remove
        /// entries by hand.
        /// </summary>
        [SerializeField] private List<TileDensityChannel> densityChannels = new();

        /// <summary>Read-only access to density channels. Use lifecycle API to add/remove.</summary>
        public IReadOnlyList<TileDensityChannel> DensityChannels => this.densityChannels;

        /// <summary>Finds the density channel for <paramref name="layerId"/>, or null if absent.</summary>
        public TileDensityChannel? GetDensityChannel(string layerId)
        {
            for (int i = 0; i < this.densityChannels.Count; i++)
            {
                if (this.densityChannels[i].layerId == layerId)
                    return this.densityChannels[i];
            }
            return null;
        }

        // â”€â”€ Internal density channel lifecycle (called by WorldMapAssetLifecycle) â”€â”€

        /// <summary>
        /// Allocates an empty R8 density channel for <paramref name="layerId"/> if one doesn't exist.
        /// Returns the existing channel if already present.
        /// </summary>
        internal TileDensityChannel AllocateDensityChannel(string layerId)
        {
            var existing = this.GetDensityChannel(layerId);
            if (existing != null) return existing;

            var channel = new TileDensityChannel
            {
                layerId     = layerId,
                densityData = new byte[WorldMapGrid.DENSITY_RES * WorldMapGrid.DENSITY_RES],
            };
            this.densityChannels.Add(channel);
            return channel;
        }

        /// <summary>
        /// Frees (removes) the density channel for <paramref name="layerId"/>.
        /// No-op if absent.
        /// </summary>
        internal void FreeDensityChannel(string layerId)
        {
            for (int i = this.densityChannels.Count - 1; i >= 0; i--)
            {
                if (this.densityChannels[i].layerId == layerId)
                {
                    this.densityChannels.RemoveAt(i);
                    return;
                }
            }
        }

        // â”€â”€ Per-tile prop TRS buckets (for P7 prop placement) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Per-tile prop TRS bucket: records authored prop instances that belong to this tile.
        /// Keyed by layerId. Managed by WorldMapAssetLifecycle.
        /// </summary>
        [SerializeField] private List<TilePropBucket> propBuckets = new();

        /// <summary>Read-only prop buckets for this tile.</summary>
        public IReadOnlyList<TilePropBucket> PropBuckets => this.propBuckets;

        /// <summary>Finds the prop bucket for <paramref name="layerId"/>, or null.</summary>
        public TilePropBucket? GetPropBucket(string layerId)
        {
            for (int i = 0; i < this.propBuckets.Count; i++)
            {
                if (this.propBuckets[i].layerId == layerId)
                    return this.propBuckets[i];
            }
            return null;
        }

        /// <summary>Allocates an empty prop bucket for <paramref name="layerId"/> if absent.</summary>
        internal TilePropBucket AllocatePropBucket(string layerId)
        {
            var existing = this.GetPropBucket(layerId);
            if (existing != null) return existing;

            var bucket = new TilePropBucket { layerId = layerId };
            this.propBuckets.Add(bucket);
            return bucket;
        }

        /// <summary>Frees the prop bucket for <paramref name="layerId"/>. No-op if absent.</summary>
        internal void FreePropBucket(string layerId)
        {
            for (int i = this.propBuckets.Count - 1; i >= 0; i--)
            {
                if (this.propBuckets[i].layerId == layerId)
                {
                    this.propBuckets.RemoveAt(i);
                    return;
                }
            }
        }

        // â”€â”€ Validation helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Expected byte length of heightData = heightResÂ² Ã— 2.</summary>
        public int ExpectedHeightBytes => this.heightRes * this.heightRes * TerrainHeightFormat.BYTES_PER_SAMPLE;

        /// <summary>Expected byte length of splatData = splatResÂ² Ã— 4 (RGBA32).</summary>
        public int ExpectedSplatBytes => this.splatRes * this.splatRes * 4;

        /// <summary>Returns true if heightData is non-null and has the expected length.</summary>
        public bool IsHeightValid =>
            this.heightData != null && this.heightData.Length == this.ExpectedHeightBytes;

        /// <summary>Returns true if splatData is non-null and has the expected length.</summary>
        public bool IsSplatValid =>
            this.splatData != null && this.splatData.Length == this.ExpectedSplatBytes;
    }

    /// <summary>
    /// Per-tile authored prop instances for one layer. Stores a flat list of TRS records
    /// belonging to this tile for the given scatter layer.
    /// </summary>
    [Serializable]
    public sealed class TilePropBucket
    {
        /// <summary>Layer ID matching the owning <see cref="InstanceScatterLayer"/>.</summary>
        [SerializeField] public string layerId = string.Empty;

        /// <summary>Authored instance records for this tile (TRS + optional collider config).</summary>
        [SerializeField] public List<InstanceRecord> records = new();
    }
}
