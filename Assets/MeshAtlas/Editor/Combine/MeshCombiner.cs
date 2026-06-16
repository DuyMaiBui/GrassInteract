using System.Collections.Generic;
using MeshAtlas.Editor.Packing;
using UnityEngine;

namespace MeshAtlas.Editor.Combine
{
    /// <summary>
    /// One (mesh, submesh) slice to combine: the world transform to bake and the atlas
    /// sub-rect its material occupies. One item per submesh so multi-material meshes
    /// remap each submesh's UVs into the correct region even when vertices are shared.
    /// </summary>
    public sealed class CombineItem
    {
        public Mesh Mesh { get; set; }
        public int SubMeshIndex { get; set; }
        public Matrix4x4 Transform { get; set; } = Matrix4x4.identity;
        public Rect SubRect { get; set; }
    }

    /// <summary>
    /// Remaps UV0 per submesh into its atlas sub-rect, then merges everything into one
    /// mesh / one submesh with a UInt32 index buffer (large-selection safe).
    /// </summary>
    public static class MeshCombiner
    {
        public static Mesh Combine(IReadOnlyList<CombineItem> items)
        {
            var temps = new List<Mesh>(items.Count);
            var instances = new CombineInstance[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var standalone = ExtractAndRemap(items[i]);
                temps.Add(standalone);
                instances[i] = new CombineInstance { mesh = standalone, transform = items[i].Transform, subMeshIndex = 0 };
            }

            var combined = new Mesh { name = "CombinedMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combined.CombineMeshes(instances, mergeSubMeshes: true, useMatrices: true);
            combined.RecalculateBounds();

            foreach (var t in temps)
            {
                Object.DestroyImmediate(t);
            }
            return combined;
        }

        /// <summary>Build a standalone single-submesh mesh from one source submesh, with UV0 remapped.</summary>
        private static Mesh ExtractAndRemap(CombineItem item)
        {
            var src = item.Mesh;
            var srcVerts = src.vertices;
            var srcNormals = src.normals;
            var srcTangents = src.tangents;
            var srcUv = src.uv;
            var tris = src.GetTriangles(item.SubMeshIndex);

            var hasNormals = srcNormals.Length == srcVerts.Length;
            var hasTangents = srcTangents.Length == srcVerts.Length;
            var hasUv = srcUv.Length == srcVerts.Length;

            var remap = new Dictionary<int, int>(tris.Length);
            var verts = new List<Vector3>();
            var normals = hasNormals ? new List<Vector3>() : null;
            var tangents = hasTangents ? new List<Vector4>() : null;
            var uvs = new List<Vector2>();
            var newTris = new int[tris.Length];

            for (var k = 0; k < tris.Length; k++)
            {
                var v = tris[k];
                if (!remap.TryGetValue(v, out var nv))
                {
                    nv = verts.Count;
                    remap[v] = nv;
                    verts.Add(srcVerts[v]);
                    if (hasNormals) { normals.Add(srcNormals[v]); }
                    if (hasTangents) { tangents.Add(srcTangents[v]); }
                    var uv = hasUv ? srcUv[v] : Vector2.zero;
                    uvs.Add(UVRemapper.Remap(uv, item.SubRect));
                }
                newTris[k] = nv;
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            if (hasNormals) { mesh.SetNormals(normals); }
            if (hasTangents) { mesh.SetTangents(tangents); }
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(newTris, 0);
            return mesh;
        }
    }
}
