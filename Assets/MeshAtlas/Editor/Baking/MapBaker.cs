using System.Collections.Generic;
using UnityEngine;

namespace MeshAtlas.Editor.Baking
{
    /// <summary>
    /// Bakes the four channels into four atlases that share one layout. GPU work
    /// (clear-to-default RenderTexture → DrawTexture each source into its sub-rect →
    /// ReadPixels) plus CPU scalar-fold and edge dilation. Returns a Texture2D per
    /// enabled channel (null for disabled), indexed by <see cref="BakeChannel"/>.
    ///
    /// NOTE (verify in-editor): DrawTexture uses a top-left screen origin while atlas
    /// UVs are bottom-left, so the per-region screen rect flips Y. If a sample bake
    /// shows inverted content, flip <see cref="ToScreenRect"/>'s Y term — one line.
    /// </summary>
    public sealed class MapBaker
    {
        private static Texture2D flatNormalTex;

        public Texture2D[] Bake(IReadOnlyList<BakeInput> inputs, int atlasSize, bool[] enabledChannels, int dilation)
        {
            var result = new Texture2D[BakeChannelInfo.COUNT];
            for (var c = 0; c < BakeChannelInfo.COUNT; c++)
            {
                if (enabledChannels != null && c < enabledChannels.Length && !enabledChannels[c])
                {
                    continue;
                }
                result[c] = this.BakeChannelMap((BakeChannel)c, inputs, atlasSize, dilation);
            }
            return result;
        }

        private Texture2D BakeChannelMap(BakeChannel channel, IReadOnlyList<BakeInput> inputs, int atlasSize, int dilation)
        {
            var linear = BakeChannelInfo.IsLinear(channel);
            var rw = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt = new RenderTexture(atlasSize, atlasSize, 0, RenderTextureFormat.ARGB32, rw);
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, DefaultColor(channel));
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, atlasSize, atlasSize, 0);
                foreach (var input in inputs)
                {
                    var src = input.SourceFor(channel) ?? DefaultTexture(channel);
                    Graphics.DrawTexture(ToScreenRect(input.SubRect, atlasSize), src);
                }
                GL.PopMatrix();

                var tex = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, mipChain: false, linear: linear);
                tex.ReadPixels(new Rect(0, 0, atlasSize, atlasSize), 0, 0);
                tex.Apply();

                this.PostProcess(tex, channel, inputs, atlasSize, dilation);
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        private void PostProcess(Texture2D tex, BakeChannel channel, IReadOnlyList<BakeInput> inputs, int atlasSize, int dilation)
        {
            var pixels = tex.GetPixels();
            var covered = new bool[pixels.Length];
            foreach (var input in inputs)
            {
                var rect = ToPixelRect(input.SubRect, atlasSize);
                FoldRegion(pixels, covered, atlasSize, rect, channel, input.Factors);
            }
            if (dilation > 0)
            {
                EdgeDilation.Dilate(pixels, covered, atlasSize, atlasSize, dilation);
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private static void FoldRegion(Color[] pixels, bool[] covered, int atlasSize, RectInt rect,
            BakeChannel channel, ScalarFactors factors)
        {
            for (var y = rect.yMin; y < rect.yMax; y++)
            {
                if (y < 0 || y >= atlasSize)
                {
                    continue;
                }
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    if (x < 0 || x >= atlasSize)
                    {
                        continue;
                    }
                    var idx = (y * atlasSize) + x;
                    covered[idx] = true;
                    if (channel == BakeChannel.Albedo)
                    {
                        pixels[idx] = ScalarFold.FoldAlbedo(pixels[idx], factors.BaseColor);
                    }
                    else if (channel == BakeChannel.MaskMetallicSmoothness)
                    {
                        pixels[idx] = ScalarFold.FoldMask(pixels[idx], factors.Metallic, factors.Smoothness);
                    }
                }
            }
        }

        private static Color DefaultColor(BakeChannel channel)
        {
            switch (channel)
            {
                case BakeChannel.Normal: return new Color(0.5f, 0.5f, 1f, 1f); // flat normal
                case BakeChannel.Emission: return Color.black;
                default: return Color.white; // albedo + mask default to white (fold yields tint / factors)
            }
        }

        private static Texture DefaultTexture(BakeChannel channel)
        {
            if (channel == BakeChannel.Normal)
            {
                return FlatNormal();
            }
            return channel == BakeChannel.Emission ? Texture2D.blackTexture : Texture2D.whiteTexture;
        }

        private static Texture2D FlatNormal()
        {
            if (flatNormalTex == null)
            {
                flatNormalTex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                flatNormalTex.SetPixel(0, 0, new Color(0.5f, 0.5f, 1f, 1f));
                flatNormalTex.Apply();
            }
            return flatNormalTex;
        }

        private static Rect ToScreenRect(Rect subRect, int atlasSize)
        {
            var r = ToPixelRect(subRect, atlasSize);
            // Flip Y: screen origin is top-left, atlas/UV origin is bottom-left.
            return new Rect(r.x, atlasSize - r.yMax, r.width, r.height);
        }

        private static RectInt ToPixelRect(Rect subRect, int atlasSize)
            => new RectInt(
                Mathf.RoundToInt(subRect.x * atlasSize),
                Mathf.RoundToInt(subRect.y * atlasSize),
                Mathf.RoundToInt(subRect.width * atlasSize),
                Mathf.RoundToInt(subRect.height * atlasSize));
    }
}
