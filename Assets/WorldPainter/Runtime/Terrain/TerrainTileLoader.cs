#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Async tile loader: enqueues a load request, schedules the GPU-upload callback
    /// for drain on the main thread.
    ///
    /// Generation token guard (mirrors InstanceVisibilityColliderDriver):
    ///   Each load request captures the current generation token at enqueue time.
    ///   CancelTile() increments the token for that coord. DrainMainThreadQueue()
    ///   re-validates the token before invoking the callback, preventing
    ///   use-after-dispose if the tile is evicted before the callback fires.
    ///
    /// Thread model:
    ///   - Enqueue / CancelTile / DrainMainThreadQueue must be called from the main thread.
    ///   - The pending queue is protected by a lock for future Addressables integration
    ///     where callbacks may arrive from a background thread.
    ///   - GPU upload (TerrainTileGpuResources.Upload + GpuTerrainEngine.Build) is
    ///     main-thread only — always happens inside DrainMainThreadQueue.
    ///
    /// In this phase, tile data is already in memory (ScriptableObject), so the async
    /// simulation uses ThreadPool.QueueUserWorkItem to model the scheduled-IO pattern
    /// without blocking the main thread. A real Addressables integration would replace
    /// the ThreadPool call with an async handle callback.
    /// </summary>
    public sealed class TerrainTileLoader
    {
        // ── Pending callback ──────────────────────────────────────────────────

        private sealed class PendingCallback
        {
            public readonly Vector2Int       Coord;
            public readonly TerrainTileAsset Asset;
            public readonly int              Generation;
            public readonly Action<Vector2Int, TerrainTileAsset, int> Callback;

            public PendingCallback(
                Vector2Int coord,
                TerrainTileAsset asset,
                int generation,
                Action<Vector2Int, TerrainTileAsset, int> callback)
            {
                this.Coord      = coord;
                this.Asset      = asset;
                this.Generation = generation;
                this.Callback   = callback;
            }
        }

        // ── State (main-thread) ───────────────────────────────────────────────

        // Per-coord generation tokens — incremented on cancel.
        private readonly Dictionary<Vector2Int, int> generations =
            new Dictionary<Vector2Int, int>();

        // In-flight: coords currently being loaded (main thread only).
        private readonly HashSet<Vector2Int> inFlight = new HashSet<Vector2Int>();

        // Pending main-thread callbacks (written from thread pool, read on main thread).
        private readonly Queue<PendingCallback> pendingQueue = new Queue<PendingCallback>(32);
        private readonly object                 pendingLock  = new object();

        // ── Enqueue ───────────────────────────────────────────────────────────

        /// <summary>
        /// Enqueue an async load for <paramref name="asset"/>.
        /// If a load for <paramref name="coord"/> is already in flight, this is a no-op.
        /// <paramref name="onComplete"/> fires on the main thread (via DrainMainThreadQueue)
        /// with (coord, asset, generation) only if the generation token is still valid.
        /// </summary>
        public void Enqueue(
            Vector2Int coord,
            TerrainTileAsset asset,
            Action<Vector2Int, TerrainTileAsset, int> onComplete)
        {
            if (this.inFlight.Contains(coord))
                return; // double-enqueue guard

            if (!this.generations.TryGetValue(coord, out int gen))
                gen = 0;

            this.inFlight.Add(coord);

            // Capture values for the closure.
            Vector2Int       capturedCoord = coord;
            TerrainTileAsset capturedAsset = asset;
            int              capturedGen   = gen;
            Queue<PendingCallback> queue   = this.pendingQueue;
            object                lk       = this.pendingLock;

            // Simulate async I/O: post callback on thread pool so the main thread is not blocked.
            // In a real Addressables implementation this would be replaced by
            // Addressables.LoadAssetAsync<TerrainTileAsset>(...).Completed += ...
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // (This runs off the main thread — only touch queue + lock.)
                var pending = new PendingCallback(
                    capturedCoord, capturedAsset, capturedGen, onComplete);
                lock (lk)
                    queue.Enqueue(pending);
            });
        }

        /// <summary>
        /// Cancel any pending or in-flight load for this coord and increment its generation.
        /// Any callback that drains after this will be silently discarded.
        /// </summary>
        public void CancelTile(Vector2Int coord)
        {
            this.inFlight.Remove(coord);

            if (!this.generations.TryGetValue(coord, out int gen))
                gen = 0;
            this.generations[coord] = gen + 1;
        }

        /// <summary>
        /// Drain pending main-thread callbacks up to <paramref name="maxUploads"/> per call.
        /// Returns the number of callbacks that were accepted (generation matched).
        ///
        /// M1 fix: inFlight is cleared on BOTH the accepted and the stale (rejected) paths so
        /// a coord cancelled-then-re-enqueued between Enqueue and drain is not stuck waiting
        /// for a phantom inFlight entry.
        ///
        /// M2 fix: maxUploads caps how many accepted callbacks run this call so the
        /// per-frame GPU-upload budget is enforced at the drain site, not only at enqueue time.
        /// Stale callbacks (generation mismatch) are always discarded for free; they do not
        /// count against the budget.
        /// </summary>
        public int DrainMainThreadQueue(int maxUploads = int.MaxValue)
        {
            int accepted = 0;
            while (accepted < maxUploads)
            {
                PendingCallback? pending;
                lock (this.pendingLock)
                {
                    if (this.pendingQueue.Count == 0)
                        break;
                    pending = this.pendingQueue.Dequeue();
                }

                // Validate generation.
                if (!this.generations.TryGetValue(pending.Coord, out int currentGen))
                    currentGen = 0;
                if (pending.Generation != currentGen)
                {
                    // M1 fix: clear inFlight for stale callbacks so a re-enqueued coord
                    // is not blocked by a phantom in-flight entry.
                    this.inFlight.Remove(pending.Coord);
                    continue; // stale — discard without counting against budget
                }

                this.inFlight.Remove(pending.Coord);
                pending.Callback(pending.Coord, pending.Asset, pending.Generation);
                accepted++;
            }
            return accepted;
        }

        // ── Test entry point ──────────────────────────────────────────────────

        /// <summary>
        /// Synchronous fixture path for EditMode tests: directly enqueues a callback
        /// into the pending queue without going through the thread pool.
        /// Allows tests to verify the generation-token guard without async infrastructure.
        /// </summary>
        internal void EnqueueDirect(
            Vector2Int coord,
            TerrainTileAsset asset,
            Action<Vector2Int, TerrainTileAsset, int> onComplete)
        {
            if (!this.generations.TryGetValue(coord, out int gen))
                gen = 0;

            this.inFlight.Add(coord);
            lock (this.pendingLock)
                this.pendingQueue.Enqueue(new PendingCallback(coord, asset, gen, onComplete));
        }

        // ── P8: baked standalone tile loading ─────────────────────────────────

        /// <summary>
        /// P8 overload: enqueue loading of a standalone baked <see cref="TerrainTileAsset"/>
        /// that lives at <paramref name="bakedAsset"/> (already resolved by the caller via
        /// <c>AssetDatabase.LoadAssetAtPath</c> or Addressables).
        ///
        /// Functionally identical to <see cref="Enqueue"/>: the async simulation posts the
        /// callback on the thread pool. A real Addressables integration replaces the ThreadPool
        /// call with <c>Addressables.LoadAssetAsync&lt;TerrainTileAsset&gt;(key).Completed</c>.
        ///
        /// The key distinction from the container sub-asset path is that the caller must
        /// pre-resolve the asset reference before calling this method — the loader does not
        /// perform any AssetDatabase or Addressables lookup internally.
        /// </summary>
        public void EnqueueBaked(
            Vector2Int       coord,
            TerrainTileAsset bakedAsset,
            Action<Vector2Int, TerrainTileAsset, int> onComplete)
        {
            // Delegate to the same Enqueue path — the data contract is identical.
            this.Enqueue(coord, bakedAsset, onComplete);
        }

        /// <summary>
        /// Synchronous baked-tile fixture path for EditMode tests: directly enqueues a
        /// baked standalone tile callback without the thread pool.
        ///
        /// Mirrors <see cref="EnqueueDirect"/> but documents the baked-tile semantic so
        /// test readers can distinguish baked-standalone from container-sub-asset paths.
        /// </summary>
        internal void EnqueueBakedDirect(
            Vector2Int       coord,
            TerrainTileAsset bakedAsset,
            Action<Vector2Int, TerrainTileAsset, int> onComplete)
        {
            this.EnqueueDirect(coord, bakedAsset, onComplete);
        }
    }
}

