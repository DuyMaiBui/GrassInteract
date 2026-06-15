#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Builds the ONE shared terrain patch mesh: a (PATCH_RES+1)² grid in unit XZ [0,1], Y=0.
    /// All CDLOD nodes instance this same mesh; the VS scales/offsets it per-node.
    ///
    /// PATCH_RES = 16 (mobile-conservative; tunable 16-32).
    /// Vertex count = (PATCH_RES+1)² = 289.
    /// Index count  = PATCH_RES² × 6 = 1536 (two triangles per quad).
    ///
    /// Verify: mesh.GetIndexCount(0) > 0 (guards the InitArgsFromMesh 0-index pitfall).
    ///         mesh.vertexCount == (PATCH_RES+1)² == 289.
    /// </summary>
    public static class TerrainPatchMesh
    {
        // ── Constants ─────────────────────────────────────────────────────────
        /// <summary>Number of quads per edge. Tunable 16-32; 16 = mobile-conservative.</summary>
        public const int PATCH_RES = 16;

        private static Mesh? cachedMesh;

        /// <summary>
        /// Returns the shared patch mesh, building it on first call.
        /// The mesh is marked HideAndDontSave so it survives domain reloads without asset save.
        /// </summary>
        public static Mesh GetOrCreate()
        {
            if (cachedMesh != null) return cachedMesh;
            cachedMesh = Build();
            return cachedMesh;
        }

        /// <summary>
        /// Builds a fresh (PATCH_RES+1)² grid mesh in unit XZ [0,1], Y=0.
        /// UV.xy = vertex XZ position (equal to normalized XZ).
        /// </summary>
        public static Mesh Build()
        {
            int verts = PATCH_RES + 1;
            int totalVerts   = verts * verts;
            int totalIndices = PATCH_RES * PATCH_RES * 6;

            Vector3[] positions = new Vector3[totalVerts];
            Vector2[] uvs       = new Vector2[totalVerts];
            int[]     indices   = new int[totalIndices];

            float step = 1f / PATCH_RES;

            // Vertices: XZ in [0,1], Y=0
            for (int z = 0; z < verts; ++z)
            {
                for (int x = 0; x < verts; ++x)
                {
                    int i = z * verts + x;
                    float fx = x * step;
                    float fz = z * step;
                    positions[i] = new Vector3(fx, 0f, fz);
                    uvs[i]       = new Vector2(fx, fz);
                }
            }

            // Indices: two CW triangles per quad (Unity default winding)
            int idx = 0;
            for (int z = 0; z < PATCH_RES; ++z)
            {
                for (int x = 0; x < PATCH_RES; ++x)
                {
                    int v00 =  z      * verts + x;
                    int v10 =  z      * verts + x + 1;
                    int v01 = (z + 1) * verts + x;
                    int v11 = (z + 1) * verts + x + 1;

                    indices[idx++] = v00;
                    indices[idx++] = v01;
                    indices[idx++] = v10;
                    indices[idx++] = v10;
                    indices[idx++] = v01;
                    indices[idx++] = v11;
                }
            }

            var mesh = new Mesh
            {
                name        = "TerrainPatch",
                hideFlags   = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32,
            };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);

            UnityEngine.Debug.Assert(mesh.GetIndexCount(0) > 0,
                "[TerrainPatchMesh] Index count is 0 — indirect draw will render nothing.");
            UnityEngine.Debug.Assert(mesh.vertexCount == totalVerts,
                $"[TerrainPatchMesh] Expected {totalVerts} vertices, got {mesh.vertexCount}.");

            return mesh;
        }

        /// <summary>Releases the cached mesh. Called from GpuTerrainEngine.Dispose.</summary>
        public static void ReleaseCached()
        {
            if (cachedMesh == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(cachedMesh);
            else
                UnityEngine.Object.DestroyImmediate(cachedMesh);
            cachedMesh = null;
        }
    }
}
