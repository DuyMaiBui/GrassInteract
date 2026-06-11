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
    // LOD0 visible-index buffer via AsyncGPUReadback.  Replaces the removed
    // InstanceFrustumCuller: no Camera.main dependency; exact parity with the
    // visual render.
    //
    // Lifecycle: construct → Tick(visibleLod0Buf, lod0CountBuf) per frame
    //            → Dispose when engine disposes.
    //
    // Index-space: visibleLod0Buf contains chunk-sorted global indices.
    // sortedToAuthored[globalIdx] maps them to authored (pool-keyed) record
    // indices.  See ChunkedInstanceBuffer.SortedToAuthored.
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

        // ── Readback state ────────────────────────────────────────────────────
        // countBuf: a 1-uint Raw GraphicsBuffer written by CopyCounterValue from
        // visibleLod0Buf.  Read back alongside the index buffer so a single
        // AsyncGPUReadback.Request covers both in one round-trip.
        //
        // Actually two separate requests are simpler here: one for the count
        // (4 B) and one for the indices (lod0Count * 4 B, determined AFTER the
        // count arrives).  We use a two-phase design:
        //   Phase A: readback the 1-uint count buffer (always 4 B, cheap).
        //   Phase B: readback the index buffer for exactly lod0Count uints.
        // Both are throttled by TICK_INTERVAL_FRAMES and the in-flight guard.
        //
        // Alternatively, read the full index buffer capacity and trust the count
        // field inside argsLod0Buf.  We chose to read argsLod0Buf[1] (instance
        // count, byte offset ARGS_INSTANCE_COUNT_OFFSET=4) — that is the count
        // written by CopyCounterValue, so it is correct after the GPU barrier
        // inside ExecuteCommandBuffer.  One combined readback: request
        // argsLod0Buf 8 bytes from offset 0 → indices[0]=indexCountPerInstance
        // (not needed), indices[1]=instanceCount.  Simple and single-trip.
        //
        // FINAL DESIGN: a dedicated lod0CountBuf (1 uint, Raw) passed in from
        // the engine; the engine calls CopyCounterValue(visibleLod0Buf,
        // lod0CountBuf, 0) in RecordFrameCommands.  We readback ONLY the count
        // first; on completion we issue a second readback of the exact byte range
        // of visibleLod0Buf.  This keeps the index readback minimal and avoids
        // reading padding.  The count readback is 4 B; the index readback is up
        // to lod0Count * 4 B (bounded by poolCap).
        //
        // Stale-completion guard: an integer generation is incremented on each
        // Dispose.  Each readback callback captures the generation at the time it
        // was issued; if the captured value != current generation the callback
        // is discarded.

        private GraphicsBuffer? lod0CountBuf; // 1-uint Raw buffer; owned by this driver

        private int  generation;   // incremented on Dispose; guards stale callbacks
        private bool countReadbackInFlight;
        private bool indexReadbackInFlight;
        private int  pendingLod0Count; // count received from phase-A, used to size phase-B request
        private int  frameCounter;

        private bool disposed;
        private bool warnedNoAsyncReadback;

        // ── Scratch (no per-frame alloc) ──────────────────────────────────────
        private readonly HashSet<int> desiredActiveSet  = new(256);
        private readonly List<int>    toRelease         = new(256);

        // ── Construction ──────────────────────────────────────────────────────

        /// <param name="pool">The collider pool to drive (kept unchanged).</param>
        /// <param name="sortedToAuthored">
        ///   Permutation map from ChunkedInstanceBuffer: sortedToAuthored[sortedIdx] = authoredIdx.
        ///   Length must equal the total baked instance count.
        /// </param>
        /// <param name="positions">Authored-indexed world positions.</param>
        /// <param name="rotations">Authored-indexed rotations.</param>
        /// <param name="scales">Authored-indexed scales.</param>
        /// <param name="meshes">Authored-indexed collider meshes.</param>
        /// <param name="convex">Authored-indexed convex flags.</param>
        /// <param name="wantsCollider">Authored-indexed generateCollider flags.</param>
        /// <param name="materials">Authored-indexed physics materials.</param>
        /// <param name="poolCap">Pool capacity (passed to cap warning logic).</param>
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
            int                   poolCap)
        {
            this.pool              = pool              ?? throw new ArgumentNullException(nameof(pool));
            this.sortedToAuthored  = sortedToAuthored  ?? throw new ArgumentNullException(nameof(sortedToAuthored));
            this.positions         = positions         ?? throw new ArgumentNullException(nameof(positions));
            this.rotations         = rotations         ?? throw new ArgumentNullException(nameof(rotations));
            this.scales            = scales            ?? throw new ArgumentNullException(nameof(scales));
            this.meshes            = meshes            ?? throw new ArgumentNullException(nameof(meshes));
            this.convex            = convex            ?? throw new ArgumentNullException(nameof(convex));
            this.wantsCollider     = wantsCollider     ?? throw new ArgumentNullException(nameof(wantsCollider));
            this.materials         = materials         ?? throw new ArgumentNullException(nameof(materials));
            this.poolCap           = Mathf.Max(1, poolCap);

            // Allocate the 1-uint count buffer owned by this driver.
            this.lod0CountBuf = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
        }

        // ── Count-buffer accessor for RecordFrameCommands ─────────────────────

        /// <summary>
        /// The 1-uint Raw buffer into which the engine must write
        /// <c>CopyCounterValue(visibleLod0Buf, Lod0CountBuffer, 0)</c> each frame.
        /// </summary>
        public GraphicsBuffer? Lod0CountBuffer => this.lod0CountBuf;

        // ── Tick (called from InstancedPropEngine.Submit) ─────────────────────

        /// <summary>
        /// Issues a throttled AsyncGPUReadback of the LOD0 visible-index buffer.
        /// Must be called AFTER <c>Graphics.ExecuteCommandBuffer</c> so the GPU
        /// cull results are committed.
        /// </summary>
        public void Tick(GraphicsBuffer visibleLod0Buf)
        {
            if (this.disposed) return;

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                if (!this.warnedNoAsyncReadback)
                {
                    Debug.LogWarning(
                        "[InstanceVisibilityColliderDriver] AsyncGPUReadback is not supported on this " +
                        "platform. Per-instance collider culling will be skipped. " +
                        "Enable colliders without culling to use them on this hardware.");
                    this.warnedNoAsyncReadback = true;
                }
                return;
            }

            // Throttle: only kick a new readback every N frames, and only when no readback is in-flight.
            this.frameCounter++;
            if ((this.frameCounter % TICK_INTERVAL_FRAMES) != 0) return;
            if (this.countReadbackInFlight || this.indexReadbackInFlight) return;
            if (this.lod0CountBuf == null) return;

            // Capture generation snapshot for stale-completion guard.
            int capturedGeneration = this.generation;

            // Phase A: readback the 1-uint count buffer (4 bytes).
            this.countReadbackInFlight = true;
            AsyncGPUReadback.Request(this.lod0CountBuf, 4, 0, req =>
            {
                // Stale-completion guard: if Dispose was called since this request was issued, discard.
                if (capturedGeneration != this.generation) return;
                if (req.hasError)
                {
                    this.countReadbackInFlight = false;
                    return;
                }

                Unity.Collections.NativeArray<uint> countData =
                    req.GetData<uint>(0);
                uint rawCount = countData.Length > 0 ? countData[0] : 0u;
                int lod0Count = (int)Mathf.Min((float)rawCount, (float)this.poolCap);
                this.countReadbackInFlight = false;

                if (lod0Count <= 0)
                {
                    // No LOD0 instances visible — release all active colliders.
                    this.pool.ReleaseAll();
                    return;
                }

                // Phase B: readback exactly lod0Count uints from the index buffer.
                int byteCount = lod0Count * sizeof(uint);
                this.pendingLod0Count      = lod0Count;
                this.indexReadbackInFlight = true;
                int capturedGenB           = this.generation;

                AsyncGPUReadback.Request(visibleLod0Buf, byteCount, 0, idxReq =>
                {
                    if (capturedGenB != this.generation) return;
                    this.indexReadbackInFlight = false;
                    if (idxReq.hasError) return;

                    Unity.Collections.NativeArray<uint> idxData =
                        idxReq.GetData<uint>(0);
                    this.ApplyVisibleSet(idxData, this.pendingLod0Count);
                });
            });
        }

        // ── Map/diff core (also callable from unit tests without a GPU) ───────

        /// <summary>
        /// Applies a visible-index set to the pool: acquires newly-visible records,
        /// releases records that are no longer visible.
        ///
        /// Exposed as <c>internal</c> so EditMode unit tests can inject a fake
        /// visible-index list and verify the acquire/release logic without a GPU.
        /// </summary>
        internal void ApplyVisibleSet(
            Unity.Collections.NativeArray<uint> visibleIndices,
            int count)
        {
            this.desiredActiveSet.Clear();

            int totalSorted   = this.sortedToAuthored.Length;
            int wantsCount    = this.wantsCollider.Length;

            // Build desired set: map each GPU global idx → authored record idx.
            for (int i = 0; i < count && i < visibleIndices.Length; ++i)
            {
                uint globalIdx = visibleIndices[i];
                if (globalIdx >= (uint)totalSorted) continue;

                int authoredIdx = this.sortedToAuthored[(int)globalIdx];
                if (authoredIdx < 0 || authoredIdx >= wantsCount) continue;
                if (!this.wantsCollider[authoredIdx]) continue;

                this.desiredActiveSet.Add(authoredIdx);
            }

            // Acquire newly-visible.
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

            // Release records no longer visible.
            // Collect to avoid mutating pool.active while iterating (pool exposes Release by key).
            this.toRelease.Clear();
            foreach (int key in this.pool.ActiveKeys)
            {
                if (!this.desiredActiveSet.Contains(key))
                    this.toRelease.Add(key);
            }
            foreach (int key in this.toRelease)
                this.pool.Release(key);
        }

        /// <summary>
        /// Overload accepting a plain array of uint — convenience for unit tests.
        /// Allocates a temporary NativeArray (Allocator.Temp) and copies, then disposes.
        /// </summary>
        internal void ApplyVisibleSetFromArray(uint[] visibleIndices, int count)
        {
            int safeCount = Mathf.Clamp(count, 0, visibleIndices.Length);
            if (safeCount == 0)
            {
                // Empty visible set: release all and return.
                this.desiredActiveSet.Clear();
                this.toRelease.Clear();
                foreach (int key in this.pool.ActiveKeys)
                    this.toRelease.Add(key);
                foreach (int key in this.toRelease)
                    this.pool.Release(key);
                return;
            }

            var temp = new Unity.Collections.NativeArray<uint>(
                safeCount, Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < safeCount; ++i)
                temp[i] = visibleIndices[i];
            try
            {
                this.ApplyVisibleSet(temp, safeCount);
            }
            finally
            {
                temp.Dispose();
            }
        }

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            // Increment generation so any in-flight callbacks discard their results.
            this.generation++;

            this.lod0CountBuf?.Release();
            this.lod0CountBuf = null;

            // Release all active colliders.
            this.pool.ReleaseAll();
        }
    }
}
