#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace WorldPainter.Tests
{
    /// <summary>
    /// Tests for TerrainTileLoader covering:
    ///   - M1 fix: inFlight cleared on BOTH accepted and stale drain paths,
    ///     preventing a phantom in-flight entry that blocks a rapid evict→re-request.
    ///   - M2 fix: DrainMainThreadQueue(maxUploads) enforces the per-frame budget
    ///     at the drain site so N callbacks queued in one frame do not all run.
    ///   - Baseline: generation guard still rejects stale callbacks.
    ///   - Baseline: accepted callback fires with correct args.
    /// </summary>
    [TestFixture]
    public sealed class TerrainTileLoaderTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TerrainTileAsset MakeTile(int tx, int tz)
        {
            var asset = ScriptableObject.CreateInstance<TerrainTileAsset>();
            asset.tileCoord = new Vector2Int(tx, tz);
            asset.heightRes = 3;
            asset.minHeight = 0f;
            asset.maxHeight = 100f;
            asset.heightData = new byte[3 * 3 * TerrainHeightFormat.BYTES_PER_SAMPLE];
            return asset;
        }

        // ── Baseline: generation guard ─────────────────────────────────────

        [Test]
        public void DrainMainThreadQueue_AcceptedCallback_FiresWithCorrectArgs()
        {
            var loader = new TerrainTileLoader();
            var coord  = new Vector2Int(0, 0);
            var asset  = MakeTile(0, 0);

            Vector2Int? firedCoord = null;
            loader.EnqueueDirect(coord, asset, (c, _, _2) => firedCoord = c);
            loader.DrainMainThreadQueue();

            Assert.AreEqual(coord, firedCoord, "Accepted callback must fire with the enqueued coord.");
        }

        [Test]
        public void DrainMainThreadQueue_StaleCallback_IsDiscarded()
        {
            var loader = new TerrainTileLoader();
            var coord  = new Vector2Int(1, 0);
            var asset  = MakeTile(1, 0);

            bool fired = false;
            loader.EnqueueDirect(coord, asset, (_, __, ___) => fired = true);
            loader.CancelTile(coord); // bumps generation → callback is stale
            loader.DrainMainThreadQueue();

            Assert.IsFalse(fired, "Stale callback (generation mismatch) must not fire.");
        }

        // ── M1 fix: inFlight cleared on the stale path ───────────────────────

        [Test]
        public void M1_StaleCallback_ClearsInFlight_AllowingReEnqueue()
        {
            // Scenario: tile A enqueued, evicted (cancel), then re-requested.
            // Before M1 fix: stale drain path did NOT clear inFlight, so the re-enqueue
            // guard (inFlight.Contains) blocked the re-request until the stale callback
            // was eventually drained — causing the tile to silently fail to reload.
            // After M1 fix: stale drain clears inFlight → re-enqueue succeeds immediately
            // once the stale callback drains.
            var loader = new TerrainTileLoader();
            var coord  = new Vector2Int(3, 0);
            var asset  = MakeTile(3, 0);

            // 1. Enqueue (gen 0 captured, added to inFlight).
            loader.EnqueueDirect(coord, asset, (_, __, ___) => { });

            // 2. Cancel (gen → 1, inFlight cleared by CancelTile).
            loader.CancelTile(coord);

            // 3. Re-enqueue at gen 1 — adds back to inFlight.
            bool secondFired = false;
            loader.EnqueueDirect(coord, asset, (_, __, ___) => secondFired = true);

            // 4. First drain: the gen-0 callback drains (stale, M1 fix removes from inFlight).
            //    The gen-1 callback is still in the queue.
            //    Then the gen-1 callback drains and fires.
            loader.DrainMainThreadQueue();

            Assert.IsTrue(secondFired,
                "M1 fix: re-enqueued gen-1 callback must fire after the stale gen-0 drains. " +
                "This FAILS against the pre-fix code where inFlight was not cleared on the stale path.");
        }

        [Test]
        public void M1_RapidEvictAndReRequest_DoesNotLeavePhantomInFlight()
        {
            // Confirm: after a stale callback drains, the coord is no longer in inFlight
            // so a fresh Enqueue for it is accepted (not treated as double-enqueue).
            var loader = new TerrainTileLoader();
            var coord  = new Vector2Int(5, 0);
            var asset  = MakeTile(5, 0);

            // Enqueue gen-0, cancel (gen→1), drain gen-0 as stale.
            loader.EnqueueDirect(coord, asset, (_, __, ___) => { });
            loader.CancelTile(coord);
            loader.DrainMainThreadQueue(); // drains stale gen-0

            // Now enqueue gen-1 — should be accepted (not blocked by phantom inFlight).
            bool firedAfterDrain = false;
            loader.EnqueueDirect(coord, asset, (_, __, ___) => firedAfterDrain = true);
            loader.DrainMainThreadQueue();

            Assert.IsTrue(firedAfterDrain,
                "M1 fix: after stale drain, the coord must be re-enqueueable. " +
                "Phantom inFlight would have blocked this.");
        }

        // ── M2 fix: per-frame budget enforced at drain ────────────────────────

        [Test]
        public void M2_DrainMainThreadQueue_WithBudget_ProcessesAtMostBudgetCallbacks()
        {
            // Queue 5 callbacks; drain with budget=2 — only 2 must fire per call.
            var loader   = new TerrainTileLoader();
            int firedCount = 0;

            for (int i = 0; i < 5; i++)
            {
                var coord = new Vector2Int(i, 10);
                var asset = MakeTile(i, 10);
                loader.EnqueueDirect(coord, asset, (_, __, ___) => firedCount++);
            }

            int accepted = loader.DrainMainThreadQueue(maxUploads: 2);

            Assert.AreEqual(2, accepted,    "DrainMainThreadQueue must return the accepted count.");
            Assert.AreEqual(2, firedCount,  "Exactly 2 callbacks must fire when budget=2 and 5 are queued.");
        }

        [Test]
        public void M2_DrainMainThreadQueue_RemainingCarriedToNextCall()
        {
            // Remaining callbacks (above the budget) must be processed in the next drain call.
            var loader     = new TerrainTileLoader();
            int firedCount = 0;

            for (int i = 0; i < 4; i++)
            {
                var coord = new Vector2Int(i, 20);
                var asset = MakeTile(i, 20);
                loader.EnqueueDirect(coord, asset, (_, __, ___) => firedCount++);
            }

            loader.DrainMainThreadQueue(maxUploads: 2); // first frame: 2 fire
            loader.DrainMainThreadQueue(maxUploads: 2); // second frame: remaining 2 fire

            Assert.AreEqual(4, firedCount,
                "M2 fix: all 4 callbacks must eventually fire across two budget-capped drain calls.");
        }

        [Test]
        public void M2_StaleCallbacks_DoNotCountAgainstBudget()
        {
            // Stale callbacks (generation mismatch) are discarded for free and must not
            // consume any of the per-frame upload budget.
            var loader     = new TerrainTileLoader();
            int accepted   = 0;
            int firedCount = 0;

            // Queue 3 valid gen-0 callbacks.
            for (int i = 0; i < 3; i++)
            {
                var coord = new Vector2Int(i, 30);
                var asset = MakeTile(i, 30);
                loader.EnqueueDirect(coord, asset, (_, __, ___) => firedCount++);
            }

            // Queue 2 stale callbacks (cancel before drain).
            for (int i = 3; i < 5; i++)
            {
                var coord = new Vector2Int(i, 30);
                var asset = MakeTile(i, 30);
                loader.EnqueueDirect(coord, asset, (_, __, ___) => firedCount++);
                loader.CancelTile(coord); // make stale
            }

            // Drain with budget=3. The 2 stale ones must not eat budget.
            // Expected: 3 valid fire, 2 stale discarded, accepted = 3.
            accepted = loader.DrainMainThreadQueue(maxUploads: 3);

            Assert.AreEqual(3, accepted,
                "M2 fix: stale callbacks must not count against the upload budget.");
            Assert.AreEqual(3, firedCount,
                "Only valid callbacks should fire; stale ones are discarded.");
        }
    }
}
