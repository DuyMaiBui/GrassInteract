#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Builds the ONE shared terrain patch mesh: a (PATCH_RES+1)² grid in unit XZ [0,1], Y=0,
    /// PLUS a vertical skirt ring around all four edges. All CDLOD nodes instance this same
    /// mesh; the VS scales/offsets it per-node.
    ///
    /// PATCH_RES = 16 (mobile-conservative; tunable 16-32).
    /// Surface vertex count = (PATCH_RES+1)² = 289.
    /// Surface index count  = PATCH_RES² × 6 = 1536 (two triangles per quad).
    ///
    /// ── Skirt (inter-tile CDLOD crack fix) ───────────────────────────────────
    /// CDLOD morph keeps a tile crack-free internally, but two ADJACENT tiles are
    /// independent quadtrees: at a shared edge they can pick LODs that differ, and the
    /// finer side's per-vertex morph is not guaranteed to reach blend=1 there → a Y
    /// T-junction (crack) opens, most visibly while the camera moves across a band edge.
    /// Each node edge therefore carries a downward skirt: extra "bottom" vertices at the
    /// SAME edge XZ (so they morph + sample height identically to the surface edge vertex)
    /// flagged with positionOS.y = SKIRT_FLAG. The VS pushes those flagged vertices down by
    /// _SkirtDepth, forming a vertical curtain that covers any gap. Skirt quads are emitted
    /// double-sided so the curtain hides the crack regardless of view direction. Interior
    /// (same-LOD) seams are watertight, so their coincident skirts stay hidden behind the surface.
    ///
    /// Verify: mesh.GetIndexCount(0) > 0 (guards the InitArgsFromMesh 0-index pitfall).
    ///         mesh.vertexCount == SURFACE_VERTS + SKIRT_VERTS.
    /// </summary>
    public static class TerrainPatchMesh
    {
        // ── Constants ─────────────────────────────────────────────────────────
        /// <summary>Number of quads per edge. Tunable 16-32; 16 = mobile-conservative.</summary>
        public const int PATCH_RES = 16;

        /// <summary>
        /// positionOS.y marker written into skirt-bottom vertices. The VS detects this
        /// (y &lt; -0.5) and pushes the vertex down by _SkirtDepth. Surface vertices have y = 0.
        /// </summary>
        public const float SKIRT_FLAG = -1f;

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
            int surfaceVerts   = verts * verts;
            int surfaceIndices = PATCH_RES * PATCH_RES * 6;

            // Skirt: one bottom vertex per surface edge vertex, 4 edges. Corner vertices
            // are duplicated per edge (harmless — coincident XZ). Skirt quads are double-sided
            // (4 triangles each) so the curtain covers the crack from any view angle.
            const int edges = 4;
            int skirtVerts   = edges * verts;
            int skirtIndices = edges * PATCH_RES * 12; // 16 quads/edge × 4 tris × 3

            int totalVerts   = surfaceVerts + skirtVerts;
            int totalIndices = surfaceIndices + skirtIndices;

            Vector3[] positions = new Vector3[totalVerts];
            Vector2[] uvs       = new Vector2[totalVerts];
            int[]     indices   = new int[totalIndices];

            float step = 1f / PATCH_RES;

            // ── Surface vertices: XZ in [0,1], Y=0 ───────────────────────────────
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

            // ── Surface indices: two CW triangles per quad (Unity default winding) ─
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

            // ── Skirt geometry ───────────────────────────────────────────────────
            // For each edge p in 0..PATCH_RES: a skirt-bottom vertex at the SAME edge XZ,
            // flagged via positionOS.y = SKIRT_FLAG. Quads connect the surface edge vertex
            // (top) to its skirt vertex (bottom). uv == edge XZ so the skirt morphs and
            // samples height identically to the surface edge, keeping the curtain top welded
            // to the (morphed) edge.
            int skirtBase = surfaceVerts;
            // Surface edge-vertex index for edge e (0=S,1=N,2=W,3=E) at position p (0..PATCH_RES).
            int SurfEdge(int e, int p) => e switch
            {
                0 => p,                              // South (z=0)
                1 => PATCH_RES * verts + p,          // North (z=PATCH_RES)
                2 => p * verts,                      // West  (x=0)
                _ => p * verts + PATCH_RES,          // East  (x=PATCH_RES)
            };
            // Skirt-bottom vertex world (node-local) UV for edge e at position p.
            Vector2 EdgeUV(int e, int p)
            {
                float t = p * step;
                return e switch
                {
                    0 => new Vector2(t, 0f),  // South
                    1 => new Vector2(t, 1f),  // North
                    2 => new Vector2(0f, t),  // West
                    _ => new Vector2(1f, t),  // East
                };
            }

            for (int e = 0; e < edges; ++e)
            {
                int edgeSkirtBase = skirtBase + e * verts;
                for (int p = 0; p <= PATCH_RES; ++p)
                {
                    Vector2 uv = EdgeUV(e, p);
                    int si = edgeSkirtBase + p;
                    positions[si] = new Vector3(uv.x, SKIRT_FLAG, uv.y);
                    uvs[si]       = uv;
                }

                for (int p = 0; p < PATCH_RES; ++p)
                {
                    int topA = SurfEdge(e, p);
                    int topB = SurfEdge(e, p + 1);
                    int botA = edgeSkirtBase + p;
                    int botB = edgeSkirtBase + p + 1;

                    // Double-sided curtain: emit both windings so the skirt is never
                    // backface-culled regardless of which side the crack is viewed from.
                    indices[idx++] = topA; indices[idx++] = topB; indices[idx++] = botA;
                    indices[idx++] = topB; indices[idx++] = botB; indices[idx++] = botA;
                    indices[idx++] = topA; indices[idx++] = botA; indices[idx++] = topB;
                    indices[idx++] = topB; indices[idx++] = botA; indices[idx++] = botB;
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
            // Skirt verts (Y=SKIRT_FLAG) would shrink RecalculateBounds Y to [-1,0]; the real
            // Y extent is driven per-node in the VS (height + skirt depth). Set generous bounds
            // so Unity does not frustum-cull the patch by its authored unit-cube bounds.
            mesh.bounds = new Bounds(new Vector3(0.5f, 0f, 0.5f), new Vector3(1f, 2f, 1f));
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
