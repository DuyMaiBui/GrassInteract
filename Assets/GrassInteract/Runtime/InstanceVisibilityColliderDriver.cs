#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    // ──────────────────────────────────────────────────────────────────────────
    // InstanceVisibilityColliderDriver
    //
    // Drives InstanceColliderPool acquire/release from the GPU cull pipeline's
    // LOD visible-index buffers via AsyncGPUReadback.  Covers all three LOD
    // bands so colliders track every rendered instance at any distance.
    //
    // Two-phase readback per band per tick: Phase A reads a 1-uint count buffer
    // (written each frame by CopyCounterValue in RecordFrameCommands); Phase B
    // reads exactly count*4 bytes from the matching visible-index buffer.  All
    // three band results are UNIONed into desiredActiveSet.  A generation token
    // guards stale completions that arrive after Dispose.
    //
    // Acquire is amortised: every frame Tick() releases all dropped keys
    // immediately, then acquires up to maxCollidersPerFrame NEW entries from
    // desiredActiveSet.  This spreads MeshCollider cooking across frames to
    // avoid a spawn hitch.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class InstanceVisibilityColliderDriver : IDisposable
    {
        // ── Throttle ──────────────────────────────────────────────────────────
        private const int TICK_INTERVAL_FRAMES = 4;

        // ── Injected record arrays (authored-indexed, parallel) ───────────────
        private readonly InstanceColliderPool  pool;
        private readonly int[]                 sortedToAuthored;
        private readonly Vector3[]             positions;
        private readonly Quaternion[]          rotations;
        private readonly float[]               scales;
        private readonly Mesh?[]               meshes;
        private readonly bool[]                convex;
        private readonly bool[]                wantsCollider;
        private readonly PhysicsMaterial?[]    materials;
        private readonly int                   poolCap;
        private readonly int                   maxCollidersPerFrame;

        // ── Per-band count buffers (1-uint Raw; owned by this driver) ─────────
        private GraphicsBuffer? lod0CountBuf;
        private GraphicsBuffer? lod1CountBuf;
        private GraphicsBuffer? lod2CountBuf;

        // ── Readback state ────────────────────────────────────────────────────
        private int  generation;        // incremented on Dispose; guards stale callbacks
        private int  frameCounter;
        private bool disposed;
        private bool warnedNoAsyncReadback;
        private bool warnedOverCap;

        // Counts how many band-readback rounds are still in-flight this tick.
        // A new tick starts only when this reaches 0 (all three bands settled).
        private int inFlightRounds;

        // Per-band: pending count received from Phase A, used to size Phase B.
        private int pendingCount0;
        private int pendingCount1;
        private int pendingCount2;

        // ── Desired state (UNION of all LOD bands, updated by readbacks) ──────
        // Updated only by readback callbacks; read every frame by Tick's acquire loop.
        private readonly HashSet<int> desiredActiveSet = new(256);

        // ── Scratch (no per-frame alloc) ──────────────────────────────────────
        private readonly List<int>    toRelease    = new(256);
        private readonly List<int>    toAcquire    = new(256);

        // ── Construction ──────────────────────────────────────────────────────

        /// <param name="pool">The collider pool to drive.</param>
        /// <param name="sortedToAuthored">
        ///   Permutation from ChunkedInstanceBuffer: sortedToAuthored[sortedIdx] = authoredIdx.
        /// </param>
        /// <param name="positions">Authored-indexed world positions.</param>
        /// <param name="rotations">Authored-indexed rotations.</param>
        /// <param name="scales">Authored-indexed scales.</param>
        /// <param name="meshes">Authored-indexed collider meshes.</param>
        /// <param name="convex">Authored-indexed convex flags.</param>
        /// <param name="wantsCollider">Authored-indexed generateCollider flags.</param>
        /// <param name="materials">Authored-indexed physics materials.</param>
        /// <param name="poolCap">Pool capacity (used for cap-warning logic).</param>
        /// <param name="maxCollidersPerFrame">
        ///   Max NEW colliders acquired per frame — amortises MeshCollider cooking.
        /// </param>
        [UnityEngine.Scripting.Preserve]
        public InstanceVisibilityColliderDriver(
            InstanceColliderPool  pool,
            int[]                 sortedToAuthored,
            Vector3[]             positions,
            Quaternion[]          rotations,
            float[]               scales,
            Mesh?[]               meshes,
            bool[]                convex,
            bool[]                wantsCollider,
            PhysicsMaterial?[]    materials,
            int                   poolCap,
            int                   maxCollidersPerFrame = 8)
        {
            this.pool                 = pool             ?? throw new ArgumentNullException(nameof(pool));
            this.sortedToAuthored     = sortedToAuthored ?? throw new ArgumentNullException(nameof(sortedToAuthored));
            this.positions            = positions        ?? throw new ArgumentNullException(nameof(positions));
            this.rotations            = rotations        ?? throw new ArgumentNullException(nameof(rotations));
            this.scales               = scales           ?? throw new ArgumentNullException(nameof(scales));
            this.meshes               = meshes           ?? throw new ArgumentNullException(nameof(meshes));
            this.convex               = convex           ?? throw new ArgumentNullException(nameof(convex));
            this.wantsCollider        = wantsCollider    ?? throw new ArgumentNullException(nameof(wantsCollider));
            this.materials            = materials        ?? throw new ArgumentNullException(nameof(materials));
            this.poolCap              = Mathf.Max(1, poolCap);
            this.maxCollidersPerFrame = Mathf.Max(1, maxCollidersPerFrame);

            // Allocate the 1-uint count buffers owned by this driver (one per LOD band).
            this.lod0CountBuf = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            this.lod1CountBuf = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
            this.lod2CountBuf = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
        }

        // ── Count-buffer accessors for RecordFrameCommands ────────────────────

        /// <summary>1-uint Raw buffer; engine writes CopyCounterValue(visibleLod0Buf, …) each frame.</summary>
        public GraphicsBuffer? Lod0CountBuffer => this.lod0CountBuf;

        /// <summary>1-uint Raw buffer; engine writes CopyCounterValue(visibleLod1Buf, …) each frame.</summary>
        public GraphicsBuffer? Lod1CountBuffer => this.lod1CountBuf;

        /// <summary>1-uint Raw buffer; engine writes CopyCounterValue(visibleLod2Buf, …) each frame.</summary>
        public GraphicsBuffer? Lod2CountBuffer => this.lod2CountBuf;

        // ── Tick (called from InstancedPropEngine.Submit every frame) ─────────

        /// <summary>
        /// Every-frame method:
        ///   1. Release active colliders no longer in <c>desiredActiveSet</c> (all at once).
        ///   2. Acquire up to <c>maxCollidersPerFrame</c> new entries from <c>desiredActiveSet</c>.
        ///   3. Throttled: kick a new 3-band AsyncGPUReadback round every TICK_INTERVAL_FRAMES frames
        ///      (only when the previous round has fully settled) to refresh <c>desiredActiveSet</c>.
        ///
        /// Must be called AFTER <c>Graphics.ExecuteCommandBuffer</c> so GPU cull results are committed.
        /// </summary>
        public void Tick(
            GraphicsBuffer visibleLod0Buf,
            GraphicsBuffer visibleLod1Buf,
            GraphicsBuffer visibleLod2Buf)
        {
            if (this.disposed) return;

            // ── A. Every-frame acquire/release from current desiredActiveSet ──

            // Release dropped: keys active but not in desiredSet.
            this.toRelease.Clear();
            foreach (int key in this.pool.ActiveKeys)
            {
                if (!this.desiredActiveSet.Contains(key))
                    this.toRelease.Add(key);
            }
            foreach (int key in this.toRelease)
                this.pool.Release(key);

            // Acquire new: desired but not yet active, up to maxCollidersPerFrame.
            this.toAcquire.Clear();
            foreach (int authoredIdx in this.desiredActiveSet)
            {
                if (!this.pool.IsActive(authoredIdx))
                    this.toAcquire.Add(authoredIdx);
            }

            int acquired = 0;
            foreach (int authoredIdx in this.toAcquire)
            {
                if (acquired >= this.maxCollidersPerFrame) break;
                this.pool.Acquire(
                    authoredIdx,
                    this.positions[authoredIdx],
                    this.rotations[authoredIdx],
                    this.scales[authoredIdx],
                    this.meshes[authoredIdx],
                    this.convex[authoredIdx],
                    this.materials[authoredIdx]);
                acquired++;
            }

            // ── B. Throttled: kick new readback round to refresh desiredActiveSet ──

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!this.warnedNoAsyncReadback)
                {
                    Debug.LogWarning(
                        "[InstanceVisibilityColliderDriver] AsyncGPUReadback is not supported on this " +
                        "platform. Per-instance collider culling will be skipped.");
                    this.warnedNoAsyncReadback = true;
                }
                return;
            }

            this.frameCounter++;
            if ((this.frameCounter % TICK_INTERVAL_FRAMES) != 0) return;
            if (this.inFlightRounds > 0) return; // previous round still in flight
            if (this.lod0CountBuf == null || this.lod1CountBuf == null || this.lod2CountBuf == null) return;

            // Start a new round: desiredActiveSet is cleared here; each band ORs its results in.
            this.desiredActiveSet.Clear();
            this.inFlightRounds = 3; // three bands to settle

            int capturedGen = this.generation;

            this.KickBandReadback(capturedGen, this.lod0CountBuf, visibleLod0Buf, ref this.pendingCount0);
            this.KickBandReadback(capturedGen, this.lod1CountBuf, visibleLod1Buf, ref this.pendingCount1);
            this.KickBandReadback(capturedGen, this.lod2CountBuf, visibleLod2Buf, ref this.pendingCount2);
        }

        // ── Single-band two-phase readback ────────────────────────────────────

        private void KickBandReadback(
            int            capturedGen,
            GraphicsBuffer countBuf,
            GraphicsBuffer indexBuf,
            ref int        pendingCountRef)
        {
            // Capture the ref value into a local for use inside the closure.
            // The ref itself cannot be captured by a lambda, so we route via a
            // band-specific field (pendingCount0/1/2) assigned before the Phase-B
            // closure fires.  We use a local index alias here for the closure.
            GraphicsBuffer capturedIndexBuf = indexBuf;

            AsyncGPUReadback.Request(countBuf, 4, 0, countReq =>
            {
                if (capturedGen != this.generation)
                {
                    // Stale: still counts as settled.
                    this.inFlightRounds--;
                    return;
                }
                if (countReq.hasError)
                {
                    this.inFlightRounds--;
                    return;
                }

                Unity.Collections.NativeArray<uint> countData = countReq.GetData<uint>(0);
                uint rawCount = countData.Length > 0 ? countData[0] : 0u;
                int bandCount = (int)Mathf.Min((float)rawCount, (float)this.poolCap);

                if (bandCount <= 0)
                {
                    // No instances in this band — band settles with no additions to desiredSet.
                    this.inFlightRounds--;
                    return;
                }

                // Phase B: read exactly bandCount uints from the index buffer.
                int    byteCount   = bandCount * sizeof(uint);
                int    capturedCount = bandCount;
                int    capturedGen2  = this.generation;

                AsyncGPUReadback.Request(capturedIndexBuf, byteCount, 0, idxReq =>
                {
                    if (capturedGen2 != this.generation)
                    {
                        this.inFlightRounds--;
                        return;
                    }
                    if (idxReq.hasError)
                    {
                        this.inFlightRounds--;
                        return;
                    }

                    Unity.Collections.NativeArray<uint> idxData = idxReq.GetData<uint>(0);
                    this.MergeVisibleBand(idxData, capturedCount);
                    this.inFlightRounds--;
                });
            });
        }

        // ── Merge a band's visible indices into desiredActiveSet ──────────────

        /// <summary>
        /// Maps each GPU global index from a single LOD band into an authored record index
        /// and adds it to <c>desiredActiveSet</c> (union).  Called from Phase-B readback
        /// callbacks; thread-safe by Unity's guarantee that readback callbacks fire on the
        /// main thread.
        /// </summary>
        private void MergeVisibleBand(
            Unity.Collections.NativeArray<uint> visibleIndices,
            int count)
        {
            int totalSorted = this.sortedToAuthored.Length;
            int wantsCount  = this.wantsCollider.Length;

            for (int i = 0; i < count && i < visibleIndices.Length; ++i)
            {
                uint globalIdx = visibleIndices[i];
                if (globalIdx >= (uint)totalSorted) continue;

                int authoredIdx = this.sortedToAuthored[(int)globalIdx];
                if (authoredIdx < 0 || authoredIdx >= wantsCount) continue;
                if (!this.wantsCollider[authoredIdx]) continue;

                if (this.desiredActiveSet.Count < this.poolCap)
                {
                    this.desiredActiveSet.Add(authoredIdx);
                }
                else if (!this.desiredActiveSet.Contains(authoredIdx))
                {
                    // Desired set already at cap: warn once and stop adding.
                    if (!this.warnedOverCap)
                    {
                        Debug.LogWarning(
                            $"[InstanceVisibilityColliderDriver] Desired collider count exceeds pool cap " +
                            $"({this.poolCap}). Excess instances will not get colliders. " +
                            "Raise PoolCap to remove this limit.");
                        this.warnedOverCap = true;
                    }
                }
            }
        }

        // ── Test entry points (GPU-free) ──────────────────────────────────────

        /// <summary>
        /// Directly refreshes <c>desiredActiveSet</c> from a plain uint array
        /// (one band) and immediately runs one acquire/release pass.
        /// Used by EditMode unit tests to bypass AsyncGPUReadback.
        /// </summary>
        internal void ApplyVisibleSetFromArray(uint[] visibleIndices, int count)
        {
            int safeCount = Mathf.Clamp(count, 0, visibleIndices.Length);

            // Rebuild desired set from this single band.
            this.desiredActiveSet.Clear();

            if (safeCount > 0)
            {
                var temp = new Unity.Collections.NativeArray<uint>(
                    safeCount, Unity.Collections.Allocator.Temp,
                    Unity.Collections.NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < safeCount; ++i)
                    temp[i] = visibleIndices[i];
                try
                {
                    this.MergeVisibleBand(temp, safeCount);
                }
                finally
                {
                    temp.Dispose();
                }
            }

            // Apply immediately (no per-frame budget in test path — acquire all at once).
            this.ApplyDesiredSetNow();
        }

        /// <summary>
        /// Merges multiple bands into <c>desiredActiveSet</c> then applies immediately.
        /// Used by EditMode unit tests for the 3-band union scenario.
        /// </summary>
        internal void ApplyThreeBandsFromArrays(
            uint[] band0, int count0,
            uint[] band1, int count1,
            uint[] band2, int count2)
        {
            this.desiredActiveSet.Clear();
            this.MergeFromArray(band0, count0);
            this.MergeFromArray(band1, count1);
            this.MergeFromArray(band2, count2);
            this.ApplyDesiredSetNow();
        }

        private void MergeFromArray(uint[] arr, int count)
        {
            int safeCount = Mathf.Clamp(count, 0, arr.Length);
            if (safeCount == 0) return;
            var temp = new Unity.Collections.NativeArray<uint>(
                safeCount, Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < safeCount; ++i)
                temp[i] = arr[i];
            try   { this.MergeVisibleBand(temp, safeCount); }
            finally { temp.Dispose(); }
        }

        /// <summary>
        /// Applies desiredActiveSet to the pool immediately (no per-frame budget).
        /// Used by test paths where we want to see the full result in one call.
        /// </summary>
        private void ApplyDesiredSetNow()
        {
            // Release dropped.
            this.toRelease.Clear();
            foreach (int key in this.pool.ActiveKeys)
            {
                if (!this.desiredActiveSet.Contains(key))
                    this.toRelease.Add(key);
            }
            foreach (int key in this.toRelease)
                this.pool.Release(key);

            // Acquire all desired (no per-frame cap in test path).
            foreach (int authoredIdx in this.desiredActiveSet)
            {
                this.pool.Acquire(
                    authoredIdx,
                    this.positions[authoredIdx],
                    this.rotations[authoredIdx],
                    this.scales[authoredIdx],
                    this.meshes[authoredIdx],
                    this.convex[authoredIdx],
                    this.materials[authoredIdx]);
            }
        }

        // ── Test helpers (internal, not called from runtime paths) ───────────

        /// <summary>
        /// Sets desiredActiveSet directly from a uint array without going through GPU.
        /// Used by unit tests to prime the desired state before calling TickAcquireRelease.
        /// </summary>
        internal void SetDesiredSetForTest(uint[] visibleIndices, int count)
        {
            this.desiredActiveSet.Clear();
            this.MergeFromArray(visibleIndices, count);
        }

        /// <summary>
        /// Runs exactly one acquire/release pass using the current desiredActiveSet
        /// and the configured maxCollidersPerFrame budget.
        /// Used by unit tests to step the budgeted acquire logic frame-by-frame.
        /// </summary>
        internal void TickAcquireRelease()
        {
            // Release dropped.
            this.toRelease.Clear();
            foreach (int key in this.pool.ActiveKeys)
            {
                if (!this.desiredActiveSet.Contains(key))
                    this.toRelease.Add(key);
            }
            foreach (int key in this.toRelease)
                this.pool.Release(key);

            // Acquire new up to budget.
            this.toAcquire.Clear();
            foreach (int authoredIdx in this.desiredActiveSet)
            {
                if (!this.pool.IsActive(authoredIdx))
                    this.toAcquire.Add(authoredIdx);
            }

            int acquired = 0;
            foreach (int authoredIdx in this.toAcquire)
            {
                if (acquired >= this.maxCollidersPerFrame) break;
                this.pool.Acquire(
                    authoredIdx,
                    this.positions[authoredIdx],
                    this.rotations[authoredIdx],
                    this.scales[authoredIdx],
                    this.meshes[authoredIdx],
                    this.convex[authoredIdx],
                    this.materials[authoredIdx]);
                acquired++;
            }
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            // Increment generation so any in-flight callbacks discard their results
            // when they fire during or after the drain below.
            this.generation++;

            // Drain ALL in-flight native GPU transfers before releasing any buffer.
            // This covers lod0/1/2 CountBufs (owned here) and the three visibleLodNBufs
            // (owned by the engine, released after this Dispose returns).
            AsyncGPUReadback.WaitAllRequests();

            this.lod0CountBuf?.Release(); this.lod0CountBuf = null;
            this.lod1CountBuf?.Release(); this.lod1CountBuf = null;
            this.lod2CountBuf?.Release(); this.lod2CountBuf = null;

            // Release all active colliders.
            this.pool.ReleaseAll();
        }
    }
}
