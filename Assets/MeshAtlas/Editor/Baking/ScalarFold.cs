using UnityEngine;

namespace MeshAtlas.Editor.Baking
{
    /// <summary>
    /// Per-material scalar factors (URP Lit) read with safe documented defaults, plus
    /// the pure fold math that bakes those factors into atlas pixels. Folding lets two
    /// materials that differ only by tint / metallic / smoothness merge correctly.
    /// Fold functions are pure — EditMode-unit-testable.
    /// </summary>
    public readonly struct ScalarFactors
    {
        public Color BaseColor { get; }
        public float Metallic { get; }
        public float Smoothness { get; }

        public ScalarFactors(Color baseColor, float metallic, float smoothness)
        {
            this.BaseColor = baseColor;
            this.Metallic = metallic;
            this.Smoothness = smoothness;
        }

        // Documented defaults when a property is absent — NOT silent-wrong:
        //  tint = white (no tint), metallic = 0 (dielectric), smoothness = 0.5 (URP default).
        public static readonly ScalarFactors Identity = new ScalarFactors(Color.white, 0f, 0.5f);

        public static ScalarFactors FromMaterial(Material material)
        {
            if (material == null)
            {
                return Identity;
            }
            var tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor")
                     : material.HasProperty("_Color") ? material.GetColor("_Color")
                     : Color.white;
            var metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            var smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness")
                           : material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness")
                           : 0.5f;
            return new ScalarFactors(tint, metallic, smoothness);
        }
    }

    public static class ScalarFold
    {
        /// <summary>Albedo: component-wise multiply by tint (folded in the albedo's stored space).</summary>
        public static Color FoldAlbedo(Color src, Color tint)
            => new Color(src.r * tint.r, src.g * tint.g, src.b * tint.b, src.a * tint.a);

        /// <summary>
        /// URP mask map packs metallic in R and smoothness in A. Fold the scalar factors
        /// into those channels (source defaults to white when a material has no mask map,
        /// so the folded value becomes the flat factor).
        /// </summary>
        public static Color FoldMask(Color srcMask, float metallic, float smoothness)
            => new Color(srcMask.r * metallic, srcMask.g, srcMask.b, srcMask.a * smoothness);
    }
}
