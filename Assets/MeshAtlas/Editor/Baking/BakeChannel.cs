namespace MeshAtlas.Editor.Baking
{
    /// <summary>
    /// The four texture channels the baker can atlas. All four share ONE atlas layout
    /// (computed once from albedo) so maps stay pixel-registered across channels.
    /// </summary>
    public enum BakeChannel
    {
        Albedo = 0,
        Normal = 1,
        MaskMetallicSmoothness = 2,
        Emission = 3,
    }

    public static class BakeChannelInfo
    {
        public const int COUNT = 4;

        /// <summary>Albedo + Emission are color (sRGB); Normal + Mask are data (linear).</summary>
        public static bool IsLinear(BakeChannel channel)
            => channel == BakeChannel.Normal || channel == BakeChannel.MaskMetallicSmoothness;
    }
}
