#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// ScriptableObject that holds up to <see cref="TerrainShadingConfig.MAX_SPLAT_LAYERS"/> layer
    /// albedo textures and builds a runtime <see cref="Texture2DArray"/> from them.
    ///
    /// Layer ordering matches the RGBA→layer SSOT in <see cref="TerrainTileAsset"/>:
    ///   index 0 (R) = base ground, 1 (G) = grass, 2 (B) = rock/cliff, 3 (A) = path/road.
    ///
    /// If more than MAX_SPLAT_LAYERS textures are assigned, excess entries are silently
    /// truncated and a warning is logged (see <see cref="BuildArray"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Terrain Layer Set", fileName = "TerrainLayerSet")]
    public sealed class TerrainLayerSet : ScriptableObject
    {
        // ── Inspector fields ──────────────────────────────────────────────────

        /// <summary>
        /// Albedo textures for each splat layer (index 0..MAX_SPLAT_LAYERS-1).
        /// Order: R=layer0, G=layer1, B=layer2, A=layer3.
        /// Excess entries beyond MAX_SPLAT_LAYERS are ignored.
        /// </summary>
        [SerializeField] private Texture2D[] layerAlbedos = Array.Empty<Texture2D>();

        /// <summary>UV tiling scale applied to all layers in the shader.</summary>
        [SerializeField] private float layerTiling = TerrainShadingConfig.DEFAULT_LAYER_TILING;

        // ── Properties ────────────────────────────────────────────────────────

        /// <summary>UV tiling scale for layer textures.</summary>
        public float LayerTiling => this.layerTiling;

        /// <summary>
        /// Number of layers that will be baked (min of assigned count and MAX_SPLAT_LAYERS).
        /// </summary>
        public int ActiveLayerCount =>
            this.layerAlbedos == null
                ? 0
                : Mathf.Min(this.layerAlbedos.Length, TerrainShadingConfig.MAX_SPLAT_LAYERS);

        /// <summary>
        /// Editor-only read accessor for the albedo array.
        /// Used by <see cref="WorldPainter.Editor.WorldMapAssetLifecycle"/> to enumerate
        /// albedo sub-assets for cleanup on layer removal.
        /// </summary>
        internal IReadOnlyList<Texture2D> EditorAlbedos =>
            this.layerAlbedos ?? Array.Empty<Texture2D>();

        // ── Array build ───────────────────────────────────────────────────────

        /// <summary>
        /// Build a <see cref="Texture2DArray"/> from the assigned layer albedos.
        /// Returns null if no layers are assigned.
        ///
        /// Truncates at MAX_SPLAT_LAYERS and logs a warning if the source count exceeds the cap.
        /// All source textures must have the same dimensions and format; mismatched textures fall
        /// back to a 4x4 white placeholder to avoid GPU resource corruption.
        ///
        /// The returned array is NOT registered as an asset — caller is responsible for
        /// lifetime management (typically via <see cref="TerrainLayerSetBinder"/>).
        /// </summary>
        public Texture2DArray? BuildArray()
        {
            if (this.layerAlbedos == null || this.layerAlbedos.Length == 0)
                return null;

            if (this.layerAlbedos.Length > TerrainShadingConfig.MAX_SPLAT_LAYERS)
            {
                Debug.LogWarning(
                    $"[TerrainLayerSet] '{this.name}' has {this.layerAlbedos.Length} layers — " +
                    $"truncating to MAX_SPLAT_LAYERS={TerrainShadingConfig.MAX_SPLAT_LAYERS}.",
                    this);
            }

            int sliceCount = this.ActiveLayerCount;

            // Resolve dimensions from the first valid texture (or fallback).
            Texture2D? reference = FindFirstValid();
            int width   = reference != null ? reference.width   : 4;
            int height  = reference != null ? reference.height  : 4;
            bool mips   = reference != null && reference.mipmapCount > 1;
            TextureFormat format = MobileCompatibleFormat(reference);

            var array = new Texture2DArray(width, height, sliceCount,
                format, mipChain: mips, linear: false)
            {
                wrapMode   = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            for (int i = 0; i < sliceCount; i++)
            {
                Texture2D? src = this.layerAlbedos[i];
                bool mipCountMismatch = src != null && mips != (src.mipmapCount > 1);
                if (src == null || src.width != width || src.height != height || src.format != format || mipCountMismatch)
                {
                    if (src != null)
                    {
                        Debug.LogWarning(
                            $"[TerrainLayerSet] Layer {i} texture '{src.name}' has mismatched " +
                            $"size/format/mips (expected {width}x{height} {format} mips={mips}); using fallback white.",
                            this);
                    }

                    src = CreateFallback(width, height, format, mips);
                }

                Graphics.CopyTexture(src, 0, array, i);
            }

            array.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return array;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Texture2D? FindFirstValid()
        {
            int cap = Mathf.Min(this.layerAlbedos.Length, TerrainShadingConfig.MAX_SPLAT_LAYERS);
            for (int i = 0; i < cap; i++)
            {
                if (this.layerAlbedos[i] != null)
                    return this.layerAlbedos[i];
            }

            return null;
        }

        /// <summary>
        /// Returns a mobile-compatible format from the source texture or a safe default.
        /// Prefers the source format when it is already ASTC/ETC2/DXT; falls back to RGBA32.
        /// </summary>
        private static TextureFormat MobileCompatibleFormat(Texture2D? src)
        {
            if (src == null) return TextureFormat.RGBA32;

            switch (src.format)
            {
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.DXT5:
                case TextureFormat.RGBA32:
                case TextureFormat.RGB24:
                    return src.format;
                default:
                    return TextureFormat.RGBA32;
            }
        }

        private static Texture2D CreateFallback(int width, int height, TextureFormat format, bool mips = false)
        {
            // For compressed formats, use RGBA32 for the CPU-side fallback then let
            // CopyTexture handle the conversion implicitly (if supported).
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mips);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: mips);
            return tex;
        }
    }
}
