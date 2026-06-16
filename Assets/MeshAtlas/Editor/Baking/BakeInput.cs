using UnityEngine;

namespace MeshAtlas.Editor.Baking
{
    /// <summary>
    /// One material's contribution to the atlas: its four (nullable) source maps, the
    /// folded scalar factors, and the normalized sub-rect it occupies in every channel.
    /// </summary>
    public sealed class BakeInput
    {
        public Texture Albedo { get; set; }
        public Texture Normal { get; set; }
        public Texture Mask { get; set; }
        public Texture Emission { get; set; }
        public ScalarFactors Factors { get; set; } = ScalarFactors.Identity;
        public Rect SubRect { get; set; }

        public Texture SourceFor(BakeChannel channel)
        {
            switch (channel)
            {
                case BakeChannel.Albedo: return this.Albedo;
                case BakeChannel.Normal: return this.Normal;
                case BakeChannel.MaskMetallicSmoothness: return this.Mask;
                case BakeChannel.Emission: return this.Emission;
                default: return null;
            }
        }
    }
}
