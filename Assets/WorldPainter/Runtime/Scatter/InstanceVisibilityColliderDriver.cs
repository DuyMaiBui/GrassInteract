#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
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
    // three band results are UNIONed into pendingSet.  Once all three bands
    // settle, pendingSet is atomically committed to desiredActiveSet via
    // OnBandSettled().  A generation token guards stale completions after Dispose.
    //
    // Double-buffer invariant: desiredActiveSet is ONLY mutated by the atomic
    // commit in OnBandSettled().  Tick's section-A (release/acquire) reads
    // desiredActiveSet every frame without risk of observing a partially-filled
    // set — it always sees the last fully-settled round.
    //
    // Acquire is amortised: every frame Tick() releases all dropped keys
    // immediately, then acquires up to maxCollidersPerFrame NEW entries from
    // desiredActiveSet.  This spreads MeshCollider cooking across frames.
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

        // Counts how many band callbacks are still outstanding for the current round.
        // A new round starts only when this reaches 0 (all three bands settled).
        private int inFlightRounds;

        // ── Double-buffered desired state ─────────────────────────────────────
        //
        // pendingSet    : written ONLY by MergeVisibleBand during an in-flight round.
        //                 Cleared at round-start. Never read by Tick section-A.
        //
        // desiredActiveSet : ONLY mutated by the atomic commit in OnBandSettled()
        //                    when inFlightRounds reaches 0. Read every frame by
        //                    Tick section-A (release/acquire loop).  Always holds
        //                    the last complete round — never partially-filled.

        private readonly HashSet<int> pendingSet      = new(256);
        private readonly HashSet<int> desiredActiveSet = new(256);

        // ── Scratch (no per-frame alloc) ──────────────────────────────────────
        private readonly List<int> toRelease = new(256);
        private readonly List<int> toAcquire = new(256);

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
        ///   A. Release active colliders no longer in <c>desiredActiveSet</c> (all at once).
        ///      Acquire up to <c>maxCollidersPerFrame</c> new entries from <c>desiredActiveSet</c>.
        ///      desiredActiveSet is the last FULLY SETTLED round — never partially-filled.
        ///   B. Throttled: kick a new 3-band AsyncGPUReadback round every TICK_INTERVAL_FRAMES
        ///      frames (only when the previous round has fully settled).  Results land in
        ///      pendingSet and are atomically committed to desiredActiveSet by OnBandSettled().
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
            // desiredActiveSet is always the last complete round — reading it here is safe
            // even when a new round is in flight, because the in-flight round writes only
            // pendingSet and commits atomically to desiredActiveSet in OnBandSettled().

            // Release dropped: keys active but not in desiredActiveSet.
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
                    WpLog.Warning(
                        "[InstanceVisibilityColliderDriver] AsyncGPUReadback is not supported on this " +
                        "platform. Per-instance collider culling will be skipped.");
                    this.warnedNoAsyncReadback = true;
                }
                return;
            }

            this.frameCounter++;
            if ((this.frameCounter % TICK_INTERVAL_FRAMES) != 0) return;
            if (this.inFlightRounds > 0) return; // previous round still settling
            if (this.lod0CountBuf == null || this.lod1CountBuf == null || this.lod2CountBuf == null) return;

            // Start a new round: clear pendingSet (NOT desiredActiveSet).
            // desiredActiveSet keeps the previous settled state until all bands land.
            this.pendingSet.Clear();
            this.inFlightRounds = 3; // three bands; each decrements via OnBandSettled()

            int capturedGen = this.generation;

            this.KickBandReadback(capturedGen, this.lod0CountBuf, visibleLod0Buf);
            this.KickBandReadback(capturedGen, this.lod1CountBuf, visibleLod1Buf);
            this.KickBandReadback(capturedGen, this.lod2CountBuf, visibleLod2Buf);
        }

        // ── Single-band two-phase readback ────────────────────────────────────

        private void KickBandReadback(
            int            capturedGen,
            GraphicsBuffer countBuf,
            GraphicsBuffer indexBuf)
        {
            GraphicsBuffer capturedIndexBuf = indexBuf;

            AsyncGPUReadback.Request(countBuf, 4, 0, countReq =>
            {
                if (capturedGen != this.generation) { this.OnBandSettled(); return; }
                if (countReq.hasError)              { this.OnBandSettled(); return; }

                Unity.Collections.NativeArray<uint> countData = countReq.GetData<uint>(0);
                uint rawCount  = countData.Length > 0 ? countData[0] : 0u;
                int  bandCount = (int)Mathf.Min((float)rawCount, (float)this.poolCap);

                if (bandCount <= 0) { this.OnBandSettled(); return; }

                int capturedCount = bandCount;
                int capturedGen2  = this.generation;

                AsyncGPUReadback.Request(capturedIndexBuf, bandCount * sizeof(uint), 0, idxReq =>
                {
                    if (capturedGen2 != this.generation) { this.OnBandSettled(); return; }
                    if (idxReq.hasError)                 { this.OnBandSettled(); return; }

                    Unity.Collections.NativeArray<uint> idxData = idxReq.GetData<uint>(0);
                    this.MergeVisibleBand(idxData, capturedCount);
                    this.OnBandSettled();
                });
            });
        }

        // ── Settle hook — called at every band-callback exit path ─────────────

        /// <summary>
        /// Decrements <c>inFlightRounds</c>. When it reaches 0 (all three bands for
        /// this round have settled), atomically commits <c>pendingSet</c> into
        /// <c>desiredActiveSet</c>.  This is the ONLY place desiredActiveSet is mutated.
        /// </summary>
        private void OnBandSettled()
        {
            this.inFlightRounds--;
            if (this.inFlightRounds > 0) return;

            // All three bands settled — commit the new desired state atomically.
            this.desiredActiveSet.Clear();
            this.desiredActiveSet.UnionWith(this.pendingSet); // alloc-free
        }

        // ── Merge a band's visible indices into pendingSet ────────────────────

        /// <summary>
        /// Maps each GPU global index from a single LOD band into an authored record
        /// index and adds it to <c>pendingSet</c> (union-in-progress for this round).
        /// Called only from Phase-B readback callbacks; Unity guarantees callbacks fire
        /// on the main thread.
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

                if (this.pendingSet.Count < this.poolCap)
                {
                    this.pendingSet.Add(authoredIdx);
                }
                else if (!this.pendingSet.Contains(authoredIdx))
                {
                    if (!this.warnedOverCap)
                    {
                        WpLog.Warning(
                            $"[InstanceVisibilityColliderDriver] Desired collider count exceeds pool cap " +
                            $"({this.poolCap}). Excess instances will not get colliders. " +
                            "Raise PoolCap to remove this limit.");
                        this.warnedOverCap = true;
                    }
                }
            }
        }

        // ── Test entry points (GPU-free, internal) ────────────────────────────

        /// <summary>
        /// Replaces desiredActiveSet with a single-band visible set and immediately
        /// applies acquire/release.  Simulates a fully-settled round from one band.
        /// Used by EditMode unit tests to bypass AsyncGPUReadback.
        /// </summary>
        internal void ApplyVisibleSetFromArray(uint[] visibleIndices, int count)
        {
            int safeCount = Mathf.Clamp(count, 0, visibleIndices.Length);
            this.pendingSet.Clear();
            this.MergeFromArray(visibleIndices, safeCount);
            this.CommitPendingForTest();
            this.ApplyDesiredSetNow();
        }

        /// <summary>
        /// Merges three bands into pendingSet, commits to desiredActiveSet, then applies.
        /// Used by EditMode unit tests for the 3-band union scenario.
        /// </summary>
        internal void ApplyThreeBandsFromArrays(
            uint[] band0, int count0,
            uint[] band1, int count1,
            uint[] band2, int count2)
        {
            this.pendingSet.Clear();
            this.MergeFromArray(band0, count0);
            this.MergeFromArray(band1, count1);
            this.MergeFromArray(band2, count2);
            this.CommitPendingForTest();
            this.ApplyDesiredSetNow();
        }

        /// <summary>
        /// Primes desiredActiveSet from a uint array without applying acquire/release.
        /// Used by unit tests to set the desired state before calling TickAcquireRelease.
        /// </summary>
        internal void SetDesiredSetForTest(uint[] visibleIndices, int count)
        {
            this.pendingSet.Clear();
            this.MergeFromArray(visibleIndices, Mathf.Clamp(count, 0, visibleIndices.Length));
            this.CommitPendingForTest();
        }

        /// <summary>
        /// Simulates starting a new async round: clears pendingSet and sets inFlightRounds=3.
        /// desiredActiveSet is left unchanged (holds the previous settled state).
        /// Used by regression tests to verify section-A reads the prior settled set
        /// during the async gap.
        /// </summary>
        internal void BeginRoundForTest()
        {
            this.pendingSet.Clear();
            this.inFlightRounds = 3;
        }

        /// <summary>
        /// Simulates one band settling with the given visible indices.
        /// Merges into pendingSet and calls OnBandSettled(); when all 3 bands have
        /// settled the commit fires automatically.
        /// </summary>
        internal void SettleBandForTest(uint[] band, int count)
        {
            this.MergeFromArray(band, Mathf.Clamp(count, 0, band.Length));
            this.OnBandSettled();
        }

        /// <summary>
        /// Runs one acquire/release pass against desiredActiveSet with the configured budget.
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

        // ── Private helpers ───────────────────────────────────────────────────

        private void MergeFromArray(uint[] arr, int count)
        {
            if (count <= 0) return;
            var temp = new Unity.Collections.NativeArray<uint>(
                count, Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < count; ++i)
                temp[i] = arr[i];
            try   { this.MergeVisibleBand(temp, count); }
            finally { temp.Dispose(); }
        }

        /// <summary>Atomically commits pendingSet → desiredActiveSet (test path).</summary>
        private void CommitPendingForTest()
        {
            this.desiredActiveSet.Clear();
            this.desiredActiveSet.UnionWith(this.pendingSet);
        }

        /// <summary>
        /// Releases dropped keys and acquires all desired entries without budget cap.
        /// Used by test paths that want the full settled state in one synchronous call.
        /// </summary>
        private void ApplyDesiredSetNow()
        {
            this.toRelease.Clear();
            foreach (int key in this.pool.ActiveKeys)
            {
                if (!this.desiredActiveSet.Contains(key))
                    this.toRelease.Add(key);
            }
            foreach (int key in this.toRelease)
                this.pool.Release(key);

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
