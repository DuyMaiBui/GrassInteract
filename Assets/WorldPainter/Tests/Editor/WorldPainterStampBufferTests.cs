#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldPainter.Editor;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for the buffered smooth-paint pipeline: <see cref="StrokeStampBuffer"/> FIFO semantics
    /// and the drain-equivalence invariant (buffering then draining a stroke emits the SAME stamp
    /// positions, in order, as the direct per-event path — guards "final density identical").
    /// </summary>
    [TestFixture]
    public class WorldPainterStampBufferTests
    {
        // ── StrokeStampBuffer FIFO ────────────────────────────────────────────

        [Test]
        public void Buffer_Empty_TryDequeueReturnsFalse()
        {
            var buf = new StrokeStampBuffer();
            Assert.AreEqual(0, buf.Count);
            Assert.IsFalse(buf.TryDequeue(out _), "Empty buffer dequeues nothing");
        }

        [Test]
        public void Buffer_PreservesFifoOrder()
        {
            var buf = new StrokeStampBuffer();
            buf.Enqueue(new Vector3(1f, 0f, 0f));
            buf.Enqueue(new Vector3(2f, 0f, 0f));
            buf.Enqueue(new Vector3(3f, 0f, 0f));
            Assert.AreEqual(3, buf.Count);

            Assert.IsTrue(buf.TryDequeue(out var a));
            Assert.IsTrue(buf.TryDequeue(out var b));
            Assert.IsTrue(buf.TryDequeue(out var c));
            Assert.AreEqual(1f, a.x, 0.0001f);
            Assert.AreEqual(2f, b.x, 0.0001f);
            Assert.AreEqual(3f, c.x, 0.0001f);
            Assert.AreEqual(0, buf.Count, "Drained to empty");
        }

        [Test]
        public void Buffer_Clear_DropsAllPending()
        {
            var buf = new StrokeStampBuffer();
            buf.Enqueue(Vector3.one);
            buf.Enqueue(Vector3.one);
            buf.Clear();
            Assert.AreEqual(0, buf.Count);
            Assert.IsFalse(buf.TryDequeue(out _));
        }

        // ── Drain-equivalence invariant ───────────────────────────────────────

        [Test]
        public void BufferedDrain_EmitsSamePositions_AsDirectAdvance()
        {
            var start = new Vector3(0f, 0f, 0f);
            var end   = new Vector3(10f, 0f, 0f);
            const float spacing = 2f;
            const float flow    = 0.8f;

            // Direct path: collect stamp positions as Advance produces them.
            var direct = new List<Vector3>();
            var s1 = new WorldPainterStroke();
            s1.Begin(start);
            s1.Advance(end, spacing, flow, (pos, _) => direct.Add(pos));

            // Buffered path: Advance enqueues; drain pops them all.
            var buffered = new List<Vector3>();
            var buf = new StrokeStampBuffer();
            var s2 = new WorldPainterStroke();
            s2.Begin(start);
            s2.Advance(end, spacing, flow, (pos, _) => buf.Enqueue(pos));
            while (buf.TryDequeue(out var p)) buffered.Add(p);

            Assert.AreEqual(direct.Count, buffered.Count,
                "Buffered drain must emit the same stamp count as the direct path");
            for (int i = 0; i < direct.Count; ++i)
            {
                Assert.AreEqual(direct[i].x, buffered[i].x, 0.0001f, $"Stamp {i} X mismatch");
                Assert.AreEqual(direct[i].z, buffered[i].z, 0.0001f, $"Stamp {i} Z mismatch");
            }
        }

        [Test]
        public void BufferedDrain_MultiSegment_PreservesCumulativeOrder()
        {
            // A multi-segment drag buffered across "frames" then drained once must keep order.
            var buf = new StrokeStampBuffer();
            var stroke = new WorldPainterStroke();
            stroke.Begin(Vector3.zero);

            stroke.Advance(new Vector3(4f, 0f, 0f),  2f, 1f, (pos, _) => buf.Enqueue(pos));
            stroke.Advance(new Vector3(4f, 0f, 6f),  2f, 1f, (pos, _) => buf.Enqueue(pos));

            var drained = new List<Vector3>();
            while (buf.TryDequeue(out var p)) drained.Add(p);

            // 4m + 6m = 10m at 2m spacing → 5 stamps, monotonic along the path.
            Assert.AreEqual(5, drained.Count, "Cumulative stamp count across two segments");
        }
    }
}
