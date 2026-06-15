#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for CdlodQuadtree:
    ///  - Near camera → finer LOD (lower lod value).
    ///  - Far camera → coarser LOD (higher lod value or fewer nodes).
    ///  - All nodes together cover the tile XZ with no gaps/overlaps.
    ///  - Node count bounded (no infinite recursion).
    ///  - CdlodNode stride parity (C# == HLSL STRIDE constant).
    /// </summary>
    [TestFixture]
    public sealed class CdlodQuadtreeTests
    {
        private const float TILE_SIZE = TerrainWorldGrid.TILE_SIZE_M; // 256
        private const float ORIGIN_X  = 0f;
        private const float ORIGIN_Z  = 0f;
        private const float MIN_Y     = 0f;
        private const float MAX_Y     = 100f;

        // LOD ranges: 4 bands, each doubles
        private static readonly float[] LOD_RANGES = new float[] { 32f, 64f, 128f, 256f };

        private CdlodQuadtree MakeTree() =>
            new CdlodQuadtree(ORIGIN_X, ORIGIN_Z, MIN_Y, MAX_Y, TILE_SIZE, LOD_RANGES);

        // ── Stride parity test ────────────────────────────────────────────────

        [Test]
        public void CdlodNode_Stride_MatchesHlslLayout()
        {
            // float3(12) + float(4) + uint(4) + float(4) + float(4) + uint(4) = 32 B
            Assert.AreEqual(32, CdlodNode.STRIDE,
                "CdlodNode.STRIDE must equal sizeof(RenderNode) in TerrainNodeCull.compute (32 B).");
        }

        // ── LOD correctness ───────────────────────────────────────────────────

        [Test]
        public void NearCamera_ProducesFinestLod()
        {
            var tree = this.MakeTree();
            // Camera at tile centre, very close → finest LOD (lod == 0 nodes expected)
            Vector3 cam = new Vector3(ORIGIN_X + TILE_SIZE * 0.5f, 10f, ORIGIN_Z + TILE_SIZE * 0.5f);
            IReadOnlyList<CdlodNode> nodes = tree.Select(cam);

            bool hasFinestLod = false;
            foreach (CdlodNode n in nodes)
                if (n.lod == 0) { hasFinestLod = true; break; }

            Assert.IsTrue(hasFinestLod, "Near camera should produce at least one finest-LOD (lod=0) node.");
        }

        [Test]
        public void FarCamera_ProducesCoarsestLod()
        {
            var tree = this.MakeTree();
            // Camera very far away → all nodes should be at coarsest LOD
            Vector3 cam = new Vector3(-10000f, 100f, -10000f);
            IReadOnlyList<CdlodNode> nodes = tree.Select(cam);

            Assert.Greater(nodes.Count, 0, "Should produce at least 1 node even from far away.");
            foreach (CdlodNode n in nodes)
                Assert.AreEqual((uint)(LOD_RANGES.Length - 1), n.lod,
                    $"Far camera: expected all nodes at coarsest LOD {LOD_RANGES.Length - 1}, got {n.lod}.");
        }

        // ── Coverage: nodes cover tile XZ ─────────────────────────────────────

        [Test]
        public void SelectedNodes_CoverEntireTileXZ()
        {
            var tree = this.MakeTree();
            Vector3 cam = new Vector3(ORIGIN_X + TILE_SIZE * 0.5f, 200f, ORIGIN_Z + TILE_SIZE * 0.5f);
            IReadOnlyList<CdlodNode> nodes = tree.Select(cam);

            float area = 0f;
            foreach (CdlodNode n in nodes)
                area += n.scale * n.scale;

            float expectedArea = TILE_SIZE * TILE_SIZE;
            Assert.AreEqual(expectedArea, area, 0.1f,
                $"Node area sum {area} should equal tile area {expectedArea} (no gaps/overlaps).");
        }

        // ── Bounded node count ────────────────────────────────────────────────

        [Test]
        public void NodeCount_IsBounded()
        {
            var tree = this.MakeTree();
            Vector3 cam = new Vector3(ORIGIN_X + TILE_SIZE * 0.5f, 10f, ORIGIN_Z + TILE_SIZE * 0.5f);
            IReadOnlyList<CdlodNode> nodes = tree.Select(cam);

            // Max nodes: 4^depth — for 4 LOD bands that's 256. Use generous bound.
            Assert.LessOrEqual(nodes.Count, 512,
                $"Node count {nodes.Count} exceeds safe upper bound.");
        }

        // ── No duplicate nodes ────────────────────────────────────────────────

        [Test]
        public void SelectedNodes_NoOverlap()
        {
            var tree  = this.MakeTree();
            Vector3 cam = new Vector3(ORIGIN_X + TILE_SIZE * 0.5f, 50f, ORIGIN_Z + TILE_SIZE * 0.5f);
            IReadOnlyList<CdlodNode> nodes = tree.Select(cam);

            // Check that no two nodes have the same worldOffset + scale
            for (int i = 0; i < nodes.Count; ++i)
                for (int j = i + 1; j < nodes.Count; ++j)
                {
                    CdlodNode a = nodes[i];
                    CdlodNode b = nodes[j];
                    bool sameNode = Mathf.Approximately(a.worldOffset.x, b.worldOffset.x) &&
                                    Mathf.Approximately(a.worldOffset.z, b.worldOffset.z) &&
                                    Mathf.Approximately(a.scale, b.scale);
                    Assert.IsFalse(sameNode, $"Duplicate node at ({a.worldOffset.x},{a.worldOffset.z}) scale={a.scale}");
                }
        }
    }
}
