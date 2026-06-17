#nullable enable
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Guards for the density heatmap overlay (<see cref="WorldPainterSculptTool"/> partial).
    ///
    /// The overlay replaces the in-drag grass-blade rebuild as the live painted-area indicator
    /// (blades are deferred to mouse-up for perf). These tests assert the two non-GPU invariants
    /// the overlay depends on: the heatmap shader resolves+compiles (else the overlay silently
    /// no-ops → the artist paints blind), and the per-tile quad geometry is correct. The behavioral
    /// "no scatter rebuild during drag" guarantee is verified by runtime Profiler smoke, not here —
    /// driving a full GPU stroke headless is out of scope for the unit suite.
    /// </summary>
    [TestFixture]
    public sealed class DensityHeatmapOverlayTests
    {
        // ── Shader resolves (the silent-no-op guard) ──────────────────────────

        [Test]
        public void HeatmapShader_IsImportedAndFound()
        {
            var shader = Shader.Find(WorldPainterSculptTool.HEATMAP_SHADER);
            Assert.IsNotNull(shader,
                $"Shader '{WorldPainterSculptTool.HEATMAP_SHADER}' not found — the density overlay " +
                "would silently no-op, leaving the artist no painted-area feedback during a drag.");
        }

        [Test]
        public void HeatmapShader_BuildsMaterialWithExpectedProperties()
        {
            var shader = Shader.Find(WorldPainterSculptTool.HEATMAP_SHADER);
            Assert.IsNotNull(shader, "Heatmap shader missing — see HeatmapShader_IsImportedAndFound.");

            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Assert.IsTrue(mat.HasProperty("_MainTex"), "Overlay binds the density RT to _MainTex.");
                Assert.IsTrue(mat.HasProperty("_Scale"),   "Density scale knob _Scale must exist.");
                Assert.IsTrue(mat.HasProperty("_MaxAlpha"), "Alpha ceiling _MaxAlpha must exist.");
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }

        // ── Quad geometry ─────────────────────────────────────────────────────

        [Test]
        public void HeatmapQuad_HasFourVertsTwoTrisAndCornerUVs()
        {
            var mesh = WorldPainterSculptTool.BuildHeatmapQuad();
            try
            {
                Assert.AreEqual(4, mesh.vertexCount, "Ground quad must have 4 vertices.");
                Assert.AreEqual(6, mesh.triangles.Length, "Ground quad must have 2 triangles (6 indices).");

                // Unit quad centred on origin, flat on XZ: extents ±0.5 in X and Z, Y == 0.
                var verts = mesh.vertices;
                float minX = float.MaxValue, maxX = float.MinValue;
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (var v in verts)
                {
                    Assert.AreEqual(0f, v.y, 1e-6f, "Quad must be flat on the XZ plane (Y == 0).");
                    minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                    minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z);
                }
                Assert.AreEqual(-0.5f, minX, 1e-6f);
                Assert.AreEqual( 0.5f, maxX, 1e-6f);
                Assert.AreEqual(-0.5f, minZ, 1e-6f);
                Assert.AreEqual( 0.5f, maxZ, 1e-6f);

                // UVs must span the full 0..1 density texture (so the whole tile RT is shown).
                var uv = mesh.uv;
                Assert.AreEqual(4, uv.Length, "Each vertex needs a UV.");
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (var t in uv)
                {
                    minU = Mathf.Min(minU, t.x); maxU = Mathf.Max(maxU, t.x);
                    minV = Mathf.Min(minV, t.y); maxV = Mathf.Max(maxV, t.y);
                }
                Assert.AreEqual(0f, minU, 1e-6f); Assert.AreEqual(1f, maxU, 1e-6f);
                Assert.AreEqual(0f, minV, 1e-6f); Assert.AreEqual(1f, maxV, 1e-6f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
