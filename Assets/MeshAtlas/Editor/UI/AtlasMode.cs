namespace MeshAtlas.Editor.UI
{
    /// <summary>How the atlas textures backing the combined material are produced.</summary>
    public enum AtlasMode
    {
        /// <summary>Pack + bake the source material maps into a freshly generated atlas.</summary>
        Bake = 0,

        /// <summary>
        /// Reference a pre-made atlas texture and only re-align each submesh's UVs into a
        /// manually assigned sub-rect. No textures are baked or written.
        /// </summary>
        Import = 1,
    }
}
