#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Guards the inter-tile CDLOD crack fix: the shared patch mesh carries a vertical skirt
    /// ring so a finer tile's under-morphed edge cannot leave a visible gap against a coarser
    /// neighbour. CDLOD morph keeps a single tile crack-free; adjacent tiles are independent
    /// quadtrees, so a skirt is what makes the seam watertight.
    ///
    /// These are GEOMETRY invariants (the skirt must exist, sit on the edge, and be flagged for
    /// the VS to push down). The actual downward push + double-sided coverage is VS/raster work
    /// verified visually, not headless.
    /// </summary>
    [TestFixture]
    public sealed class TerrainPatchMeshSkirtTests
    {
        private const int RES   = TerrainPatchMesh.PATCH_RES;
        private const int VERTS = RES + 1;

        private static readonly int SurfaceVerts = VERTS * VERTS;
        private static readonly int SkirtVerts   = 4 * VERTS;

        [Test]
        public void Mesh_HasSurfacePlusSkirtVertices()
        {
            var mesh = TerrainPatchMesh.Build();
            try
            {
                Assert.AreEqual(SurfaceVerts + SkirtVerts, mesh.vertexCount,
                    "Patch mesh must be the surface grid plus one skirt-bottom vertex per edge vertex (4 edges).");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void Mesh_IndexCount_IncludesDoubleSidedSkirt()
        {
            var mesh = TerrainPatchMesh.Build();
            try
            {
                // Surface = RES²×6; skirt = 4 edges × RES quads × 4 triangles (double-sided) × 3.
                int expected = RES * RES * 6 + 4 * RES * 12;
                Assert.AreEqual(expected, (int)mesh.GetIndexCount(0),
                    "Skirt must add double-sided curtain triangles around all four edges.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SurfaceVertices_AreFlat()
        {
            var mesh = TerrainPatchMesh.Build();
            try
            {
                var pos = mesh.vertices;
                for (int i = 0; i < SurfaceVerts; ++i)
                    Assert.AreEqual(0f, pos[i].y, 1e-6f,
                        $"Surface vertex {i} must have y=0 (not flagged as skirt).");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SkirtVertices_AreFlaggedOnEdge_WithEdgeAlignedUV()
        {
            var mesh = TerrainPatchMesh.Build();
            try
            {
                var pos = mesh.vertices;
                var uv  = mesh.uv;
                for (int i = SurfaceVerts; i < SurfaceVerts + SkirtVerts; ++i)
                {
                    Assert.AreEqual(TerrainPatchMesh.SKIRT_FLAG, pos[i].y, 1e-6f,
                        $"Skirt vertex {i} must carry SKIRT_FLAG so the VS pushes it down.");

                    // uv == position.xz, and the vertex lies on a node edge (u or v at 0 or 1).
                    Assert.AreEqual(pos[i].x, uv[i].x, 1e-6f, $"Skirt vertex {i} uv.x must equal position.x.");
                    Assert.AreEqual(pos[i].z, uv[i].y, 1e-6f, $"Skirt vertex {i} uv.y must equal position.z.");

                    bool onEdge = Mathf.Approximately(uv[i].x, 0f) || Mathf.Approximately(uv[i].x, 1f) ||
                                  Mathf.Approximately(uv[i].y, 0f) || Mathf.Approximately(uv[i].y, 1f);
                    Assert.IsTrue(onEdge,
                        $"Skirt vertex {i} uv ({uv[i].x},{uv[i].y}) must lie on a node edge so it welds to the surface edge.");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
