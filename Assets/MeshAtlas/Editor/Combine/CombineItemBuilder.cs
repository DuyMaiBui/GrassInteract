using System.Collections.Generic;
using UnityEngine;

namespace MeshAtlas.Editor.Combine
{
    /// <summary>
    /// Turns collected renderers into per-submesh <see cref="CombineItem"/>s, assigning
    /// each submesh the atlas sub-rect of its material. Geometry is recentred on the
    /// selection pivot so the combined mesh sits at a sensible local origin. A submesh
    /// whose material has no assigned rect is dropped with a warning — never lost silently.
    ///
    /// The bake pipeline feeds rects computed by the packer; the import pipeline feeds rects
    /// the user assigned by hand. Both share this builder.
    /// </summary>
    public static class CombineItemBuilder
    {
        public static List<CombineItem> Build(
            IReadOnlyList<RendererClip> renderers,
            IReadOnlyDictionary<Material, Rect> rectByMaterial,
            List<string> warnings)
        {
            var items = new List<CombineItem>();
            if (renderers == null || renderers.Count == 0 || rectByMaterial == null)
            {
                return items;
            }

            var pivot = SelectionPivot(renderers);
            var toPivot = Matrix4x4.Translate(-pivot);
            foreach (var rc in renderers)
            {
                if (rc?.Mesh == null)
                {
                    continue;
                }
                var subCount = rc.Mesh.subMeshCount;
                for (var s = 0; s < subCount; s++)
                {
                    var mat = rc.Materials != null && s < rc.Materials.Length ? rc.Materials[s] : null;
                    if (mat == null || !rectByMaterial.TryGetValue(mat, out var rect))
                    {
                        // Surface the dropped geometry — never lose a submesh silently.
                        warnings?.Add($"{rc.Mesh.name} submesh {s}: no atlas rect for its material → dropped.");
                        continue;
                    }
                    items.Add(new CombineItem
                    {
                        Mesh = rc.Mesh,
                        SubMeshIndex = s,
                        Transform = toPivot * rc.Transform,
                        SubRect = rect,
                    });
                }
            }
            return items;
        }

        private static Vector3 SelectionPivot(IReadOnlyList<RendererClip> renderers)
        {
            var sum = Vector3.zero;
            var count = 0;
            foreach (var rc in renderers)
            {
                if (rc == null)
                {
                    continue;
                }
                sum += (Vector3)rc.Transform.GetColumn(3);
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }
    }
}
