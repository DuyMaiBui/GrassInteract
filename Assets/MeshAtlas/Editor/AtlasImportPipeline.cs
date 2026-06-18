using System.Collections.Generic;
using MeshAtlas.Editor.Baking;
using MeshAtlas.Editor.Combine;
using MeshAtlas.Editor.Output;
using MeshAtlas.Editor.UI;
using UnityEngine;

namespace MeshAtlas.Editor
{
    /// <summary>
    /// Import path: instead of baking an atlas from source maps, the user supplies a
    /// pre-made atlas texture plus a manual sub-rect per material. Each submesh's UV0 is
    /// re-aligned into its material's rect and everything is combined into one mesh / one
    /// material that references the imported texture(s). No textures are written — the
    /// supplied assets are referenced directly.
    ///
    /// Errors surface as a typed failure (errors-over-silent-fallbacks); the only silent
    /// drops are UV-out-of-range meshes (reported as skipped) and submeshes whose material
    /// has no assigned rect (reported as warnings).
    /// </summary>
    public static class AtlasImportPipeline
    {
        public static PipelineResult Run(
            IReadOnlyList<GameObject> roots,
            IReadOnlyDictionary<Material, Rect> rectByMaterial,
            Texture2D[] importedByChannel,
            BakeOptions options)
        {
            var result = new PipelineResult();
            // Unlike bake mode (which requires 2+ materials to be worth atlasing), import mode
            // intentionally allows a SINGLE material: re-aligning one submesh's UVs into a region
            // of a larger pre-made texture is a valid "re-align UV" operation on its own.
            if (rectByMaterial == null || rectByMaterial.Count == 0)
            {
                result.Error = "Import mode needs at least one material with an assigned atlas rect.";
                return result;
            }
            if (importedByChannel == null
                || importedByChannel.Length < BakeChannelInfo.COUNT
                || importedByChannel[(int)BakeChannel.Albedo] == null)
            {
                result.Error = "Import mode needs an albedo atlas texture.";
                return result;
            }

            var renderers = RendererCollector.Collect(roots, result.SkippedMeshes);
            if (renderers.Count == 0)
            {
                result.Error = "No atlas-able meshes after the UV guard (UV0 must be within [0,1]).";
                return result;
            }

            var items = CombineItemBuilder.Build(renderers, rectByMaterial, result.Warnings);
            if (items.Count == 0)
            {
                result.Error = "No submeshes resolved to an assigned material rect.";
                return result;
            }

            if (importedByChannel[(int)BakeChannel.MaskMetallicSmoothness] == null)
            {
                result.Warnings.Add(
                    "No Metallic/Smoothness map supplied — the URP/Lit material defaults to "
                    + "metallic=1; assign a mask texture or adjust the material afterwards.");
            }

            var combined = MeshCombiner.Combine(items);
            result.Output = AtlasAssetWriter.WriteExisting(importedByChannel, combined, options.outputFolder, options.baseName);
            result.CombinedCount = renderers.Count;
            result.Success = true;
            return result;
        }
    }
}
