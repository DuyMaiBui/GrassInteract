using UnityEngine;

namespace MeshAtlas.Editor.UI
{
    /// <summary>Serializable wizard options driving a bake or an import.</summary>
    [System.Serializable]
    public sealed class BakeOptions
    {
        public AtlasMode mode = AtlasMode.Bake;

        // Bake-mode atlas settings.
        public int maxAtlasSize = 4096;
        public int padding = 4;
        public bool bakeAlbedo = true;
        public bool bakeNormal = true;
        public bool bakeMask = true;
        public bool bakeEmission = true;

        // Import-mode: pre-made atlas textures referenced directly (no bake). Albedo required.
        public Texture2D importedAlbedo;
        public Texture2D importedNormal;
        public Texture2D importedMask;
        public Texture2D importedEmission;

        // Output.
        public string outputFolder = "Assets/MeshAtlas/Output";
        public string baseName = "Atlas";

        /// <summary>Channel toggles indexed by BakeChannel order (Albedo, Normal, Mask, Emission).</summary>
        public bool[] EnabledChannels()
            => new[] { this.bakeAlbedo, this.bakeNormal, this.bakeMask, this.bakeEmission };
    }
}
