#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for TerrainNodeBuffer:
    ///  - AABB union covers the tile XZ region.
    ///  - Empty-node sentinel (empty list → no coverage assert, no crash).
    ///  - Upload populates NodeBuffer and AabbBuffer correctly.
    ///  - ValidatePartition passes for a full-tile selection.
    /// </summary>
    [TestFixture]
    public sealed class TerrainNodeBufferTests
    {
        private const float TILE_SIZE  = TerrainWorldGrid.TILE_SIZE_M;
        private const float ORIGIN_X   = 0f;
        private const float ORIGIN_Z   = 0f;
        private const float MIN_Y      = 0f;
        private const float MAX_Y      = 100f;

        private static List<CdlodNode> MakeFullTileNodes()
        {
            // Single full-tile node covers the whole tile
            var nodes = new List<CdlodNode>
            {
                new CdlodNode
                {
                    worldOffset = new Vector3(ORIGIN_X, MIN_Y, ORIGIN_Z),
                    scale       = TILE_SIZE,
                    lod         = 0,
                    morphStart  = 0f,
                    morphEnd    = float.MaxValue,
                    tileIdx     = 0,
                }
            };
            return nodes;
        }

        // ── Upload ────────────────────────────────────────────────────────────

        [Test]
        public void Upload_SingleNode_PopulatesBuffers()
        {
            var buf = new TerrainNodeBuffer();
            try
            {
                var nodes = MakeFullTileNodes();
                buf.Upload(nodes, MIN_Y, MAX_Y);
                Assert.AreEqual(1, buf.NodeCount, "NodeCount should be 1 after uploading 1 node.");
                // Buffers should be non-null after upload
                Assert.IsNotNull(buf.NodeBuffer, "NodeBuffer should not be null after upload.");
                Assert.IsNotNull(buf.AabbBuffer, "AabbBuffer should not be null after upload.");
            }
            finally
            {
                buf.Dispose();
            }
        }

        [Test]
        public void Upload_EmptyList_NoException()
        {
            var buf = new TerrainNodeBuffer();
            try
            {
                Assert.DoesNotThrow(() => buf.Upload(new List<CdlodNode>(), MIN_Y, MAX_Y));
                Assert.AreEqual(0, buf.NodeCount, "Empty upload should result in NodeCount = 0.");
            }
            finally
            {
                buf.Dispose();
            }
        }

        // ── ValidatePartition ─────────────────────────────────────────────────

        [Test]
        public void ValidatePartition_FullTile_Passes()
        {
            var buf = new TerrainNodeBuffer();
            try
            {
                buf.Upload(MakeFullTileNodes(), MIN_Y, MAX_Y);
                bool pass = buf.ValidatePartition(ORIGIN_X, ORIGIN_Z, TILE_SIZE, out string report);
                Assert.IsTrue(pass, $"ValidatePartition should pass for full-tile node. Report: {report}");
            }
            finally
            {
                buf.Dispose();
            }
        }

        [Test]
        public void ValidatePartition_EmptyNodes_Skips()
        {
            var buf = new TerrainNodeBuffer();
            try
            {
                buf.Upload(new List<CdlodNode>(), MIN_Y, MAX_Y);
                bool pass = buf.ValidatePartition(ORIGIN_X, ORIGIN_Z, TILE_SIZE, out string report);
                // Empty = skip (returns true with "SKIP" message)
                Assert.IsTrue(pass, $"Empty partition should return true (skip). Report: {report}");
                StringAssert.Contains("SKIP", report, "Empty partition report should contain 'SKIP'.");
            }
            finally
            {
                buf.Dispose();
            }
        }

        [Test]
        public void ValidatePartition_FourQuadrantNodes_Passes()
        {
            var buf = new TerrainNodeBuffer();
            try
            {
                float half = TILE_SIZE * 0.5f;
                var nodes = new List<CdlodNode>
                {
                    new CdlodNode { worldOffset = new Vector3(ORIGIN_X,        MIN_Y, ORIGIN_Z),        scale = half, lod = 0 },
                    new CdlodNode { worldOffset = new Vector3(ORIGIN_X + half, MIN_Y, ORIGIN_Z),        scale = half, lod = 0 },
                    new CdlodNode { worldOffset = new Vector3(ORIGIN_X,        MIN_Y, ORIGIN_Z + half), scale = half, lod = 0 },
                    new CdlodNode { worldOffset = new Vector3(ORIGIN_X + half, MIN_Y, ORIGIN_Z + half), scale = half, lod = 0 },
                };
                buf.Upload(nodes, MIN_Y, MAX_Y);
                bool pass = buf.ValidatePartition(ORIGIN_X, ORIGIN_Z, TILE_SIZE, out string report);
                Assert.IsTrue(pass, $"Four-quadrant nodes should pass coverage. Report: {report}");
            }
            finally
            {
                buf.Dispose();
            }
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        [Test]
        public void Dispose_IsIdempotent()
        {
            var buf = new TerrainNodeBuffer();
            buf.Upload(MakeFullTileNodes(), MIN_Y, MAX_Y);
            Assert.DoesNotThrow(() => { buf.Dispose(); buf.Dispose(); },
                "Dispose should be idempotent (safe to call twice).");
        }
    }
}
