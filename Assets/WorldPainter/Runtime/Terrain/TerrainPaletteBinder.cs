#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Owns the lifetime of the runtime <see cref="Texture2DArray"/> built from a
    /// <see cref="WorldMapAsset.TerrainPalette"/>. ONE per map — the array is map-level and
    /// shared across every per-tile <see cref="GpuTerrainEngine"/>.
    ///
    /// Replaces the legacy <see cref="TerrainLayerSetBinder"/>. The 4-slice fixed-RGBA layout
    /// becomes a sliced array sized to <c>min(palette.Count, MAX_TERRAIN_LAYERS)</c>; slot
    /// <c>i</c> samples <c>palette[i].diffuseTexture</c>.
    ///
    /// <see cref="Build"/> is idempotent — disposes the previous array before rebuilding —
    /// so it is safe to call on every <c>TryBuild</c>. Callers also receive a parallel
    /// <see cref="Vector4"/>[] of per-layer (tileSize.xy, tileOffset.zw) that drives the
    /// shader's per-layer UV scaling.
    /// </summary>
    public sealed class TerrainPaletteBinder : IDisposable
    {
        private Texture2DArray? array;
        private Vector4[]       tilings = System.Array.Empty<Vector4>();
        private int             activeCount;

        /// <summary>The built diffuse array, or null when the palette is empty.</summary>
        public Texture2DArray? Array => this.array;

        /// <summary>Per-layer (tileSize.xy, tileOffset.zw). Length == <see cref="MAX_LAYERS"/>.</summary>
        public Vector4[] Tilings => this.tilings;

        /// <summary>Number of active palette slices in <see cref="Array"/>.</summary>
        public int ActiveCount => this.activeCount;

        /// <summary>Mirror of <see cref="TerrainShadingConfig.MAX_TERRAIN_LAYERS"/> for shader sizing.</summary>
        public static int MAX_LAYERS => TerrainShadingConfig.MAX_TERRAIN_LAYERS;

        /// <summary>
        /// Rebuilds the array + tilings from the supplied palette. Null/empty palettes release
        /// the array and reset state; the patch material's existing binding is then untouched.
        /// </summary>
        public void Build(IReadOnlyList<TerrainLayer>? palette)
        {
            this.Dispose();

            // Always size the tilings array to MAX_LAYERS so the shader uniform stays the
            // same shape. Unused slots get (1,1,0,0) (1m tiling, zero offset) — harmless.
            this.tilings = new Vector4[MAX_LAYERS];
            for (int i = 0; i < MAX_LAYERS; i++)
                this.tilings[i] = new Vector4(1f, 1f, 0f, 0f);

            if (palette == null || palette.Count == 0)
            {
                this.activeCount = 0;
                return;
            }

            int count = Mathf.Min(palette.Count, MAX_LAYERS);
            this.activeCount = count;

            // Choose a unified target size from the largest diffuse so every layer keeps as much
            // detail as possible. Format is pinned to RGBA32 (mobile-compatible everywhere) and
            // mismatched diffuses are blit-transcoded into that size — Unity built-in Terrain
            // does the same thing internally, which is why heterogeneous TerrainLayer assets
            // just work in vanilla terrain.
            (int width, int height) = ResolveTargetSize(palette, count);
            const TextureFormat format = TextureFormat.RGBA32;
            const bool mips = false;

            this.array = new Texture2DArray(width, height, count,
                format, mipChain: mips, linear: false)
            {
                wrapMode   = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            for (int i = 0; i < count; i++)
            {
                TerrainLayer? layer    = palette[i];
                Texture2D?   diffuse   = layer != null ? layer.diffuseTexture : null;
                Vector2      tileSize  = layer != null ? layer.tileSize       : new Vector2(1f, 1f);
                Vector2      tileOff   = layer != null ? layer.tileOffset     : Vector2.zero;

                // Tilings are world-units PER repeat. Guard against (0,0) so the shader's
                // divide-by-tileSize in PaletteUV doesn't NaN. C# clamp mirrors the HLSL one.
                if (tileSize.x <= 0f) tileSize.x = 1f;
                if (tileSize.y <= 0f) tileSize.y = 1f;
                this.tilings[i] = new Vector4(tileSize.x, tileSize.y, tileOff.x, tileOff.y);

                if (diffuse == null)
                {
                    var fallback = CreateFallbackWhite(width, height, format, mips);
                    Graphics.CopyTexture(fallback, 0, 0, this.array, i, 0);
                    UnityEngine.Object.DestroyImmediate(fallback);
                    continue;
                }

                BlitDiffuseIntoArraySlice(diffuse, this.array, i, width, height);
            }

            this.array.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        /// <summary>
        /// Blit-transcode <paramref name="diffuse"/> through a temporary ARGB32 RenderTexture at
        /// <paramref name="width"/>×<paramref name="height"/>, read it back into an RGBA32
        /// <see cref="Texture2D"/>, then copy that into slice <paramref name="slice"/> of
        /// <paramref name="array"/>. Handles any source size/format (including compressed) — this
        /// is the path Unity Terrain uses internally to support heterogeneous layer textures.
        /// </summary>
        private static void BlitDiffuseIntoArraySlice(Texture2D diffuse, Texture2DArray array,
            int slice, int width, int height)
        {
            var prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(diffuse, rt);
                RenderTexture.active = rt;

                var tmp = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: false);
                tmp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tmp.Apply(updateMipmaps: false);
                Graphics.CopyTexture(tmp, 0, 0, array, slice, 0);
                UnityEngine.Object.DestroyImmediate(tmp);
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static (int width, int height) ResolveTargetSize(IReadOnlyList<TerrainLayer> palette, int cap)
        {
            int w = 0, h = 0;
            for (int i = 0; i < cap; i++)
            {
                var d = palette[i] != null ? palette[i].diffuseTexture : null;
                if (d == null) continue;
                if (d.width  > w) w = d.width;
                if (d.height > h) h = d.height;
            }
            if (w <= 0) w = 4;
            if (h <= 0) h = 4;
            return (w, h);
        }

        public void Dispose()
        {
            if (this.array == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(this.array);
            else UnityEngine.Object.DestroyImmediate(this.array);
            this.array = null;
            this.activeCount = 0;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Texture2D CreateFallbackWhite(int width, int height, TextureFormat format, bool mips)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mips);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: mips);
            return tex;
        }
    }
}
