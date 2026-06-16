using MeshAtlas.Editor.Baking;
using UnityEngine;

namespace MeshAtlas.Editor.Output
{
    /// <summary>
    /// Builds a URP/Lit material wired to the four atlas textures. Scalar factors were
    /// already folded into the pixels (see <see cref="ScalarFold"/>), so the material's
    /// own factors are set neutral to avoid double-applying them.
    /// </summary>
    public static class UrpLitMaterialFactory
    {
        private const string URP_LIT_SHADER = "Universal Render Pipeline/Lit";

        public static Material Create(Texture2D[] atlasesByChannel, string materialName)
        {
            var shader = Shader.Find(URP_LIT_SHADER);
            if (shader == null)
            {
                Debug.LogError($"[MeshAtlas] Shader '{URP_LIT_SHADER}' not found. Is URP installed?");
                return null;
            }

            var material = new Material(shader) { name = materialName };

            // Neutral factors — baked pixels already carry tint / metallic / smoothness.
            // White (not black) and 1.0 (not 0.0) are the multiplicative identities here:
            // URP samples map.rgb × _BaseColor and map.R × _Metallic / map.A × _Smoothness,
            // so the folded values in the atlas pass through unchanged at factor 1.
            if (material.HasProperty("_BaseColor")) { material.SetColor("_BaseColor", Color.white); }
            if (material.HasProperty("_Metallic")) { material.SetFloat("_Metallic", 1f); }
            if (material.HasProperty("_Smoothness")) { material.SetFloat("_Smoothness", 1f); }

            Assign(material, atlasesByChannel, BakeChannel.Albedo, "_BaseMap", null);
            Assign(material, atlasesByChannel, BakeChannel.Normal, "_BumpMap", "_NORMALMAP");
            Assign(material, atlasesByChannel, BakeChannel.MaskMetallicSmoothness, "_MetallicGlossMap", "_METALLICSPECGLOSSMAP");
            Assign(material, atlasesByChannel, BakeChannel.Emission, "_EmissionMap", "_EMISSION");
            if (atlasesByChannel[(int)BakeChannel.Emission] != null)
            {
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                if (material.HasProperty("_EmissionColor")) { material.SetColor("_EmissionColor", Color.white); }
            }
            return material;
        }

        private static void Assign(Material material, Texture2D[] atlases, BakeChannel channel, string texProp, string keyword)
        {
            var tex = atlases != null && (int)channel < atlases.Length ? atlases[(int)channel] : null;
            if (tex == null || !material.HasProperty(texProp))
            {
                return;
            }
            material.SetTexture(texProp, tex);
            if (!string.IsNullOrEmpty(keyword))
            {
                material.EnableKeyword(keyword);
            }
        }
    }
}
