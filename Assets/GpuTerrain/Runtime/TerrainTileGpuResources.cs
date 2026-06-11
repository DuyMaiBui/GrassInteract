#nullable enable
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GpuTerrain
{
    /// <summary>
    /// Per-tile GPU textures built from a <see cref="TerrainTileAsset"/>: one height texture
    /// (R16 / RHalf fallback) and one splat texture (RGBA32). IDisposable — release when
    /// the tile is evicted from the residency ring (Phase 3).
    ///
    /// Mirrors ChunkedBladeBuffer's dispose discipline: null-safe Release() on each texture,
    /// null fields after release so double-dispose is a no-op.
    ///
    /// ── Format probe ─────────────────────────────────────────────────────────
    /// Primary format: TextureFormat.R16 (16-bit per texel, same bit-depth as source).
    /// Fallback format: TextureFormat.RHalf (16-bit float — supported on all mobile GPUs
    ///   tested). The slight precision loss vs R16 is acceptable for height (~0.008 m at 512m
    ///   range), consistent with Phase 1 risk assessment.
    /// The chosen format is recorded in <see cref="HeightFormat"/> for the renderer to bind
    /// the correct shader variant (Phase 1).
    /// </summary>
    public sealed class TerrainTileGpuResources : IDisposable
    {
        // ── Chosen texture formats ────────────────────────────────────────────
        private static readonly TextureFormat PRIMARY_HEIGHT_FORMAT  = TextureFormat.R16;
        private static readonly TextureFormat FALLBACK_HEIGHT_FORMAT = TextureFormat.RHalf;
        private static readonly TextureFormat SPLAT_FORMAT           = TextureFormat.RGBA32;

        // ── GPU textures ──────────────────────────────────────────────────────
        private Texture2D? heightTex;
        private Texture2D? splatTex;

        // ── Public accessors ──────────────────────────────────────────────────

        /// <summary>Height texture (R16 or RHalf) built from the tile's heightData.</summary>
        public Texture2D? HeightTexture => this.heightTex;

        /// <summary>Splat texture (RGBA32) built from the tile's splatData.</summary>
        public Texture2D? SplatTexture => this.splatTex;

        /// <summary>
        /// The height texture format actually chosen: R16 if supported, RHalf otherwise.
        /// Phase 1 GPU renderer reads this to select the correct shader keyword.
        /// </summary>
        public TextureFormat HeightFormat { get; private set; } = PRIMARY_HEIGHT_FORMAT;

        /// <summary>True after <see cref="Upload"/> has completed without error.</summary>
        public bool IsUploaded { get; private set; }

        // ── Upload ────────────────────────────────────────────────────────────

        /// <summary>
        /// Build GPU textures from the tile asset's raw byte arrays and upload to VRAM.
        /// Call from the main thread (Texture2D creation requires it).
        /// Disposes any previously uploaded textures before re-uploading.
        /// </summary>
        /// <param name="tile">Source tile asset; must satisfy IsHeightValid and IsSplatValid.</param>
        /// <exception cref="ArgumentNullException">If tile is null.</exception>
        /// <exception cref="InvalidOperationException">If tile data is invalid.</exception>
        public void Upload(TerrainTileAsset tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (!tile.IsHeightValid)
                throw new InvalidOperationException(
                    $"[TerrainTileGpuResources] Tile ({tile.tileCoord}) heightData invalid: " +
                    $"expected {tile.ExpectedHeightBytes} bytes, got {tile.heightData?.Length ?? 0}.");

            this.Dispose();

            // ── Height texture ───────────────────────────────────────────────
            TextureFormat heightFmt = SystemInfo.SupportsTextureFormat(PRIMARY_HEIGHT_FORMAT)
                ? PRIMARY_HEIGHT_FORMAT
                : FALLBACK_HEIGHT_FORMAT;
            this.HeightFormat = heightFmt;

            this.heightTex = new Texture2D(tile.heightRes, tile.heightRes, heightFmt,
                mipChain: false, linear: true)
            {
                name         = $"TerrainHeight_{tile.tileCoord.x}_{tile.tileCoord.y}",
                wrapMode     = TextureWrapMode.Clamp,
                filterMode   = FilterMode.Bilinear,
            };

            if (heightFmt == PRIMARY_HEIGHT_FORMAT)
            {
                // R16: raw bytes map 1:1 to the texture's native R16 format.
                this.heightTex.LoadRawTextureData(tile.heightData);
            }
            else
            {
                // RHalf fallback: convert R16 raw ushort → half float per texel.
                this.heightTex.LoadRawTextureData(this.ConvertR16ToRHalf(tile));
            }
            this.heightTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            // ── Splat texture (only if data is valid) ────────────────────────
            if (tile.IsSplatValid)
            {
                this.splatTex = new Texture2D(tile.splatRes, tile.splatRes, SPLAT_FORMAT,
                    mipChain: false, linear: true)
                {
                    name         = $"TerrainSplat_{tile.tileCoord.x}_{tile.tileCoord.y}",
                    wrapMode     = TextureWrapMode.Clamp,
                    filterMode   = FilterMode.Bilinear,
                };
                this.splatTex.LoadRawTextureData(tile.splatData);
                this.splatTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }

            this.IsUploaded = true;

            Debug.Log($"[TerrainTileGpuResources] Uploaded tile {tile.tileCoord}: " +
                      $"height={tile.heightRes}² ({heightFmt}), " +
                      $"splat={tile.splatRes}² ({SPLAT_FORMAT}), " +
                      $"range=[{tile.minHeight},{tile.maxHeight}]");
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        /// <summary>
        /// Release GPU textures. Null-safe; safe to call multiple times (no-op after first).
        /// Mirrors ChunkedBladeBuffer.Dispose(): null-guard → Release → null.
        /// </summary>
        public void Dispose()
        {
            if (this.heightTex != null)
            {
                UnityEngine.Object.Destroy(this.heightTex);
                this.heightTex = null;
            }
            if (this.splatTex != null)
            {
                UnityEngine.Object.Destroy(this.splatTex);
                this.splatTex = null;
            }
            this.IsUploaded = false;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Convert R16 raw bytes to RHalf raw bytes for the fallback path.
        /// Each ushort R16 sample is converted to a half-float in [0,1].
        /// </summary>
        private byte[] ConvertR16ToRHalf(TerrainTileAsset tile)
        {
            int texelCount = tile.heightRes * tile.heightRes;
            // RHalf = 2 bytes per texel (IEEE 754 half-float).
            byte[] halfBytes = new byte[texelCount * 2];
            for (int i = 0; i < texelCount; ++i)
            {
                ushort raw = TerrainHeightFormat.ReadRaw(tile.heightData, i);
                // Normalize to [0,1] then store as half-float.
                float normalized = raw / (float)TerrainHeightFormat.R16_MAX_VALUE;
                ushort half = Mathf.FloatToHalf(normalized);
                halfBytes[i * 2]     = (byte)(half & 0xFF);
                halfBytes[i * 2 + 1] = (byte)(half >> 8);
            }
            return halfBytes;
        }
    }
}
