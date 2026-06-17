using System.Collections.Generic;
using MeshAtlas.Editor.Baking;
using MeshAtlas.Editor.Combine;
using MeshAtlas.Editor.Output;
using MeshAtlas.Editor.Packing;
using MeshAtlas.Editor.UI;
using UnityEngine;

namespace MeshAtlas.Editor
{
    public sealed class PipelineResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public int CombinedCount { get; set; }
        public List<string> SkippedMeshes { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public AtlasWriteResult Output { get; set; }
    }

    /// <summary>
    /// Orchestrates the full bake: collect → UV guard → pack → bake → combine → write.
    /// Errors surface as a typed failure (errors-over-silent-fallbacks); nothing is
    /// silently dropped except UV-out-of-range meshes, which are reported as skipped.
    /// </summary>
    public static class AtlasBakePipeline
    {
        private const int DEFAULT_SOURCE_SIZE = 256;

        public static PipelineResult Run(IReadOnlyList<GameObject> roots, BakeOptions options)
        {
            var result = new PipelineResult();
            var renderers = RendererCollector.Collect(roots, result.SkippedMeshes);

            // Unique materials → packing sizes (albedo size, else default).
            var materials = new List<Material>();
            var matIndex = new Dictionary<Material, int>();
            var sizes = new List<Vector2Int>();
            foreach (var rc in renderers)
            {
                if (rc.Materials == null)
                {
                    continue;
                }
                foreach (var mat in rc.Materials)
                {
                    if (mat == null || matIndex.ContainsKey(mat))
                    {
                        continue;
                    }
                    matIndex[mat] = materials.Count;
                    materials.Add(mat);
                    sizes.Add(SourceSize(mat));
                }
            }

            // Atlasing is defined by combining 2+ MATERIALS into one — the mesh count is
            // irrelevant. A single mesh with two submesh materials qualifies.
            if (materials.Count < 2)
            {
                result.Error = $"Need at least 2 unique materials to atlas; found {materials.Count}. "
                    + "(A single mesh with 2+ submesh materials qualifies.)";
                return result;
            }

            var packed = new AtlasPacker(options.padding, options.maxAtlasSize).Pack(sizes);
            if (!packed.Success)
            {
                result.Error = packed.Error;
                return result;
            }

            var atlasSize = packed.AtlasSize;
            var inputs = BuildInputs(materials, packed.Rects);
            var atlases = new MapBaker().Bake(inputs, atlasSize, options.EnabledChannels(), options.padding);

            var rectByMaterial = new Dictionary<Material, Rect>(materials.Count);
            for (var i = 0; i < materials.Count; i++)
            {
                rectByMaterial[materials[i]] = packed.Rects[i];
            }
            var items = CombineItemBuilder.Build(renderers, rectByMaterial, result.Warnings);
            var combined = MeshCombiner.Combine(items);
            result.Output = AtlasAssetWriter.Write(atlases, combined, options.outputFolder, options.baseName);
            result.CombinedCount = renderers.Count;
            result.Success = true;
            return result;
        }

        private static List<BakeInput> BuildInputs(List<Material> materials, Rect[] rects)
        {
            var inputs = new List<BakeInput>(materials.Count);
            for (var i = 0; i < materials.Count; i++)
            {
                var m = materials[i];
                inputs.Add(new BakeInput
                {
                    Albedo = GetTex(m, "_BaseMap", "_MainTex"),
                    Normal = GetTex(m, "_BumpMap"),
                    Mask = GetTex(m, "_MetallicGlossMap", "_MaskMap"),
                    Emission = GetTex(m, "_EmissionMap"),
                    Factors = ScalarFactors.FromMaterial(m),
                    SubRect = rects[i],
                });
            }
            return inputs;
        }

        private static Vector2Int SourceSize(Material m)
        {
            var tex = GetTex(m, "_BaseMap", "_MainTex") ?? GetTex(m, "_BumpMap");
            return tex != null
                ? new Vector2Int(Mathf.Max(1, tex.width), Mathf.Max(1, tex.height))
                : new Vector2Int(DEFAULT_SOURCE_SIZE, DEFAULT_SOURCE_SIZE);
        }

        private static Texture GetTex(Material m, params string[] props)
        {
            if (m == null)
            {
                return null;
            }
            foreach (var p in props)
            {
                if (m.HasProperty(p))
                {
                    var t = m.GetTexture(p);
                    if (t != null) { return t; }
                }
            }
            return null;
        }
    }
}
