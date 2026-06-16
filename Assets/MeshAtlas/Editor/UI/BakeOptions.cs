namespace MeshAtlas.Editor.UI
{
    /// <summary>Serializable wizard options driving a bake.</summary>
    [System.Serializable]
    public sealed class BakeOptions
    {
        public int maxAtlasSize = 4096;
        public int padding = 4;
        public bool bakeAlbedo = true;
        public bool bakeNormal = true;
        public bool bakeMask = true;
        public bool bakeEmission = true;
        public string outputFolder = "Assets/MeshAtlas/Output";
        public string baseName = "Atlas";

        /// <summary>Channel toggles indexed by BakeChannel order (Albedo, Normal, Mask, Emission).</summary>
        public bool[] EnabledChannels()
            => new[] { this.bakeAlbedo, this.bakeNormal, this.bakeMask, this.bakeEmission };
    }
}
