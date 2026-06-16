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
            var renderers = CollectRenderers(roots, result.SkippedMeshes);
            if (renderers.Count < 2)
            {
                result.Error = $"Need at least 2 atlas-able meshes after the UV guard; found {renderers.Count}.";
                return result;
            }

            // Unique materials → packing sizes (albedo size, else default).
            var materials = new List<Material>();
            var matIndex = new Dictionary<Material, int>();
            var sizes = new List<Vector2Int>();
            foreach (var rc in renderers)
            {
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

            var packed = new AtlasPacker(options.padding, options.maxAtlasSize).Pack(sizes);
            if (!packed.Success)
            {
                result.Error = packed.Error;
                return result;
            }

            var atlasSize = packed.AtlasSize;
            var inputs = BuildInputs(materials, packed.Rects);
            var atlases = new MapBaker().Bake(inputs, atlasSize, options.EnabledChannels(), options.padding);

            var items = BuildCombineItems(renderers, matIndex, packed.Rects, result.Warnings);
            var combined = MeshCombiner.Combine(items);
            result.Output = AtlasAssetWriter.Write(atlases, combined, options.outputFolder, options.baseName);
            result.CombinedCount = renderers.Count;
            result.Success = true;
            return result;
        }

        private static List<RendererClip> CollectRenderers(IReadOnlyList<GameObject> roots, List<string> skipped)
        {
            var clips = new List<RendererClip>();
            if (roots == null)
            {
                return clips;
            }
            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null)
                    {
                        continue;
                    }
                    if (UvRangeInspector.HasOutOfRangeUv(mesh.uv, out _))
                    {
                        if (!skipped.Contains(mesh.name)) { skipped.Add(mesh.name); }
                        continue;
                    }
                    clips.Add(new RendererClip
                    {
                        Transform = mr.transform.localToWorldMatrix,
                        Mesh = mesh,
                        Materials = mr.sharedMaterials,
                    });
                }
            }
            return clips;
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

        private static List<CombineItem> BuildCombineItems(List<RendererClip> renderers, Dictionary<Material, int> matIndex, Rect[] rects, List<string> warnings)
        {
            var pivot = SelectionPivot(renderers);
            var toPivot = Matrix4x4.Translate(-pivot);
            var items = new List<CombineItem>();
            foreach (var rc in renderers)
            {
                var subCount = rc.Mesh.subMeshCount;
                for (var s = 0; s < subCount; s++)
                {
                    var mat = s < rc.Materials.Length ? rc.Materials[s] : null;
                    if (mat == null || !matIndex.TryGetValue(mat, out var mi))
                    {
                        // Surface the dropped geometry — never lose a submesh silently.
                        warnings.Add($"{rc.Mesh.name} submesh {s}: no resolvable material → dropped.");
                        continue;
                    }
                    items.Add(new CombineItem
                    {
                        Mesh = rc.Mesh,
                        SubMeshIndex = s,
                        Transform = toPivot * rc.Transform,
                        SubRect = rects[mi],
                    });
                }
            }
            return items;
        }

        private static Vector3 SelectionPivot(List<RendererClip> renderers)
        {
            var sum = Vector3.zero;
            foreach (var rc in renderers)
            {
                sum += (Vector3)rc.Transform.GetColumn(3);
            }
            return sum / renderers.Count;
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

        private sealed class RendererClip
        {
            public Matrix4x4 Transform;
            public Mesh Mesh;
            public Material[] Materials;
        }
    }
}
