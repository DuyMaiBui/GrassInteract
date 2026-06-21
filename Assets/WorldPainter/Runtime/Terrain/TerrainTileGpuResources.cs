#nullable enable
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace WorldPainter
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

        // ── GPU textures ──────────────────────────────────────────────────────
        private Texture2D? heightTex;
        // Phase 5b: RG8 baked normal texture (oct-encoded XZ, Y derived in shader).
        private Texture2D? normalTex;

        // ── Public accessors ──────────────────────────────────────────────────

        /// <summary>Height texture (R16 or RHalf) built from the tile's heightData.</summary>
        public Texture2D? HeightTexture => this.heightTex;

        /// <summary>
        /// Phase 5b: RG8 baked normal texture.  Null when the tile has no normalData
        /// (pre-Phase-5 tiles not yet re-baked); the runtime falls back to live derivation
        /// via the _WP_LIVE_SCULPT keyword path.
        /// </summary>
        public Texture2D? NormalTexture => this.normalTex;

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

            // Compute chosenHeightFmt BEFORE the reuse check so the format comparison is correct.
            // R16 is bit-exact but NOT reliably VTF-sampleable / bilinear-filterable on mobile GPUs
            // (Adreno): SystemInfo.SupportsTextureFormat(R16) reports STORAGE support, not vertex-fetch
            // filtering. A failed height VTF in TerrainPatch.vert yields garbage worldY → vertices
            // collapse off-screen → INVISIBLE terrain in mobile builds (the editor on a desktop GPU is
            // fine, which is why this only showed on-device). RHalf (R16_SFloat) is VTF-safe on all
            // tested mobile GPUs. Use R16 only off-mobile and only when storage-supported.
            bool useR16 = !Application.isMobilePlatform
                && SystemInfo.SupportsTextureFormat(PRIMARY_HEIGHT_FORMAT);
            TextureFormat chosenHeightFmt = useR16 ? PRIMARY_HEIGHT_FORMAT : FALLBACK_HEIGHT_FORMAT;
            this.HeightFormat = chosenHeightFmt;
            // [terrain-build-diag] confirm the format actually chosen on-device.
            WpLog.Log($"[TerrainTileGpuResources] Tile {tile.tileCoord}: heightFmt={chosenHeightFmt} " +
                      $"(isMobile={Application.isMobilePlatform}, R16storageSupported={SystemInfo.SupportsTextureFormat(PRIMARY_HEIGHT_FORMAT)}, res={tile.heightRes})");

            // ── Height texture: reuse same Texture2D when res/format match ────
            // Keeping the same object preserves the material's _HeightTex binding after commit
            // (stale-rebind fix). Only allocate-new when res or format changed.
            bool reuseHeight = this.heightTex != null
                && this.heightTex.width  == tile.heightRes
                && this.heightTex.height == tile.heightRes
                && this.heightTex.format == chosenHeightFmt;

            if (!reuseHeight)
            {
                if (this.heightTex != null) { SafeDestroy(this.heightTex); this.heightTex = null; }
                this.heightTex = new Texture2D(tile.heightRes, tile.heightRes, chosenHeightFmt,
                    mipChain: false, linear: true)
                {
                    name       = $"TerrainHeight_{tile.tileCoord.x}_{tile.tileCoord.y}",
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            if (chosenHeightFmt == PRIMARY_HEIGHT_FORMAT)
                this.heightTex!.LoadRawTextureData(tile.heightData);
            else
                this.heightTex!.LoadRawTextureData(this.ConvertR16ToRHalf(tile));
            this.heightTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            // Phase 5b: upload baked normal texture (RG8, oct-encoded XZ).
            // Falls through gracefully when normalData is absent (pre-Phase-5 tiles).
            if (tile.IsNormalValid)
            {
                bool reuseNormal = this.normalTex != null
                    && this.normalTex.width  == tile.NormalRes
                    && this.normalTex.height == tile.NormalRes;

                if (!reuseNormal)
                {
                    if (this.normalTex != null) { SafeDestroy(this.normalTex); this.normalTex = null; }
                    // RG16 (= RG8 aliased as 2-byte per texel) — use TextureFormat.RG16 for exact layout.
                    // Mip chain enabled: the baked normal benefits from mip filtering at distance.
                    this.normalTex = new Texture2D(tile.NormalRes, tile.NormalRes,
                        TextureFormat.RG16, mipChain: true, linear: true)
                    {
                        name       = $"TerrainNormal_{tile.tileCoord.x}_{tile.tileCoord.y}",
                        wrapMode   = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                    };
                }
                this.normalTex!.LoadRawTextureData(tile.normalData);
                this.normalTex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            }
            else if (this.normalTex != null)
            {
                // Tile lost its normal data (e.g. re-bake cleared it) — release the stale texture.
                SafeDestroy(this.normalTex);
                this.normalTex = null;
            }

            this.IsUploaded = true;
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
                SafeDestroy(this.heightTex);
                this.heightTex = null;
            }
            // Phase 5b: release baked normal texture.
            if (this.normalTex != null)
            {
                SafeDestroy(this.normalTex);
                this.normalTex = null;
            }
            this.IsUploaded = false;
        }

        private static void SafeDestroy(UnityEngine.Object? obj)
        {
            if (obj == null) return;
            if (UnityEngine.Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
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
