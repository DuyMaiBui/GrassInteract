using System.Collections.Generic;
using MeshAtlas.Editor.Packing;
using UnityEngine;

namespace MeshAtlas.Editor.Combine
{
    /// <summary>
    /// Walks selected roots and captures every MeshRenderer with an atlas-able mesh.
    /// Meshes whose UV0 falls outside [0,1] cannot be remapped into a sub-rect, so they
    /// are skipped (their names appended to <paramref name="skipped"/>) rather than
    /// silently producing a broken atlas. Extracted verbatim from the bake pipeline so the
    /// bake and import paths collect identically.
    /// </summary>
    public static class RendererCollector
    {
        public static List<RendererClip> Collect(IReadOnlyList<GameObject> roots, List<string> skipped)
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
                        if (skipped != null && !skipped.Contains(mesh.name)) { skipped.Add(mesh.name); }
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
    }
}
