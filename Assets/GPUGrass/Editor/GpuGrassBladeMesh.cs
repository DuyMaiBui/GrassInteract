#nullable enable
using UnityEngine;

namespace GPUGrass.Editor
{
    /// <summary>
    /// Builds a minimal procedural blade mesh for the GPUGrass GPU tier: a vertical, upward-tapering quad
    /// strip in local space (base centred at the origin, tip at +Y), with UVs where <c>v</c> runs 0 at the
    /// base → 1 at the tip. The indirect shader reads <c>uv.y</c> as the per-vertex bend factor, so the
    /// strip flexes correctly under wind/interactor lean.
    ///
    /// This is a placeholder so Pass 2 is renderable end-to-end; the authored multi-LOD blade-mesh builder
    /// lands in Pass 3. Vertices are unit-height (1 m); per-blade scale comes from the bake/config.
    /// </summary>
    public static class GpuGrassBladeMesh
    {
        /// <summary>Builds a tapered blade-strip mesh with <paramref name="segments"/> height divisions.</summary>
        public static Mesh Build(int segments = 3, float baseWidth = 0.12f)
        {
            segments = Mathf.Max(1, segments);
            int rows = segments + 1;

            var verts = new Vector3[rows * 2];
            var uvs   = new Vector2[rows * 2];
            var norms = new Vector3[rows * 2];
            var tris  = new int[segments * 6];

            for (int r = 0; r <= segments; r++)
            {
                float t = (float)r / segments;        // 0 base → 1 tip
                float halfW = baseWidth * 0.5f * (1f - t); // taper to a point at the tip
                float y = t;                           // unit height

                int i0 = r * 2;
                verts[i0]     = new Vector3(-halfW, y, 0f);
                verts[i0 + 1] = new Vector3( halfW, y, 0f);
                uvs[i0]       = new Vector2(0f, t);
                uvs[i0 + 1]   = new Vector2(1f, t);
                norms[i0]     = Vector3.forward; // local face normal +Z (matches shader TBN assumption)
                norms[i0 + 1] = Vector3.forward;
            }

            for (int s = 0; s < segments; s++)
            {
                int v0 = s * 2;
                int ti = s * 6;
                // Two triangles per quad row (CCW front-facing toward +Z).
                tris[ti]     = v0;
                tris[ti + 1] = v0 + 2;
                tris[ti + 2] = v0 + 1;
                tris[ti + 3] = v0 + 1;
                tris[ti + 4] = v0 + 2;
                tris[ti + 5] = v0 + 3;
            }

            var mesh = new Mesh { name = "GpuGrassBlade_LOD0" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
