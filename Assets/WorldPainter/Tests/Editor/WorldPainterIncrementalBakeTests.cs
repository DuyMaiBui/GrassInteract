#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor;
using WorldPainter;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for <see cref="WorldPainterIncrementalBake"/> chunk-index computation
    /// (Phase 4 task 4).
    ///
    /// Only tests the pure-math <see cref="WorldPainterIncrementalBake.GetAffectedChunkIndices"/>
    /// path which requires no GPU and no <see cref="ChunkedInstanceBuffer"/> Bake invocation.
    ///
    /// No editor-only scene / GPU / async dependencies — EditMode safe.
    /// </summary>
    [TestFixture]
    public class WorldPainterIncrementalBakeTests
    {
        private WorldPainterIncrementalBake bake = null!;

        [SetUp]
        public void SetUp()
        {
            this.bake = new WorldPainterIncrementalBake();
        }

        // ── GetAffectedChunkIndices ───────────────────────────────────────────

        [Test]
        public void GetAffectedChunkIndices_EmptyBuffer_ReturnsEmpty()
        {
            // Buffer not yet baked → GridX / GridZ are 0.
            var buffer = new ChunkedInstanceBuffer();
            var result = this.bake.GetAffectedChunkIndices(
                buffer,
                fieldOrigin: Vector3.zero,
                stampPos: Vector3.zero,
                brushRadius: 10f);

            Assert.IsEmpty(result,
                "Unbaked buffer has GridX=0 / GridZ=0 → no chunks to return");

            buffer.Dispose();
        }

        [Test]
        public void GetAffectedChunkIndices_StampCentred_ReturnsChunks()
        {
            // Manually build a buffer stub by baking a trivial scatter.
            var buffer = BuildMinimalBuffer(gridX: 4, gridZ: 4, chunkSizeM: 8);

            // Stamp at field origin with radius 5m → hits some centre chunks.
            var origin = new Vector3(16f, 0f, 16f); // field centre for 4×4 × 8m grid
            var result = this.bake.GetAffectedChunkIndices(
                buffer,
                fieldOrigin: origin,
                stampPos: origin,
                brushRadius: 5f);

            Assert.Greater(result.Count, 0,
                "Stamp centred on field should overlap at least one chunk");

            buffer.Dispose();
        }

        [Test]
        public void GetAffectedChunkIndices_StampFarOutside_ReturnsEmpty()
        {
            var buffer = BuildMinimalBuffer(gridX: 4, gridZ: 4, chunkSizeM: 8);

            var origin     = new Vector3(16f, 0f, 16f);
            var farStamp   = new Vector3(1000f, 0f, 1000f); // far away

            var result = this.bake.GetAffectedChunkIndices(
                buffer,
                fieldOrigin: origin,
                stampPos: farStamp,
                brushRadius: 5f);

            Assert.IsEmpty(result,
                "Stamp far outside the grid should not overlap any chunk");

            buffer.Dispose();
        }

        [Test]
        public void GetAffectedChunkIndices_ResultIsValidFlatIndices()
        {
            var buffer = BuildMinimalBuffer(gridX: 4, gridZ: 4, chunkSizeM: 8);
            int gridX  = buffer.GridX;
            int gridZ  = buffer.GridZ;
            int maxIdx = gridX * gridZ - 1;

            var origin = new Vector3(16f, 0f, 16f);
            var result = this.bake.GetAffectedChunkIndices(
                buffer,
                fieldOrigin: origin,
                stampPos: origin,
                brushRadius: 20f); // large radius → hits many chunks

            foreach (int idx in result)
            {
                Assert.GreaterOrEqual(idx, 0,
                    $"Chunk index {idx} is negative");
                Assert.LessOrEqual(idx, maxIdx,
                    $"Chunk index {idx} exceeds grid bounds {maxIdx}");
            }

            buffer.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Bakes a minimal <see cref="ChunkedInstanceBuffer"/> with the requested grid
        /// dimensions. Uses a single instance placed at the field origin so Bake is valid.
        /// </summary>
        private static ChunkedInstanceBuffer BuildMinimalBuffer(
            int gridX, int gridZ, int chunkSizeM)
        {
            int n = 1;
            var matSlab = new Matrix4x4[] { Matrix4x4.identity };
            var posSlab = new Vector3[]   { Vector3.zero };
            var nrmSlab = new Vector3[]   { Vector3.up   };

            var baseSlabs     = new Matrix4x4[1][] { matSlab };
            var positionSlabs = new Vector3[1][]   { posSlab };
            var normalSlabs   = new Vector3[1][]   { nrmSlab };
            var slabCounts    = new int[1]          { n       };

            var worldBounds   = new Bounds(Vector3.zero, Vector3.one * 1000f);
            var scatter = new GrassScatterResult(
                baseSlabs, slabCounts, positionSlabs, normalSlabs, n, worldBounds);

            float fieldW    = gridX * chunkSizeM;
            float fieldH    = gridZ * chunkSizeM;
            var   fieldBoundsXZ = new Vector2(fieldW, fieldH);
            var   meshBounds    = new Bounds(Vector3.zero, Vector3.one);

            var buffer = new ChunkedInstanceBuffer();
            buffer.Bake(scatter, Vector3.zero, fieldBoundsXZ, 1f, meshBounds,
                oriented: false, chunkSize: chunkSizeM);

            return buffer;
        }
    }
}
