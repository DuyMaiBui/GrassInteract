#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Owns the per-frame structured <see cref="GraphicsBuffer"/> carrying up to
    /// <see cref="MAX_INTERACTORS"/> point-interactor records to the GPU. One instance lives inside
    /// <see cref="GpuGrassRenderer"/>. Each <see cref="Upload"/> snapshots
    /// <see cref="GrassInteractor.Active"/>, skips fake-null stale entries (edit-mode domain-reload
    /// artefacts), fills the staging array, and uploads it. The GPU buffer is allocated once and reused —
    /// no GC after construction.
    ///
    /// Standalone note: positions are world-space (terrain grass has no painting↔world root transform),
    /// so the WorldPainter <c>WorldRootBinder</c> conversion is dropped.
    /// </summary>
    internal sealed class GpuGrassInteractorBuffer : IDisposable
    {
        /// <summary>Maximum interactors uploaded per frame. Must match the VS loop bound gate.</summary>
        public const int MAX_INTERACTORS = 16;

        /// <summary>
        /// Stride of <see cref="InteractorGpu"/>: float3(12) + 5×float(20) = 32 B.
        /// Must match the HLSL <c>GrassInteractorGpu</c> struct.
        /// </summary>
        public const int STRIDE = 32;

        /// <summary>Blittable record mirroring HLSL <c>GrassInteractorGpu</c> (32 B).</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct InteractorGpu
        {
            public Vector3 posWS;    // 12
            public float   radius;   //  4
            public float   strength; //  4
            public float   pad0;     //  4
            public float   pad1;     //  4
            public float   pad2;     //  4  -> 32
        }

        private readonly GraphicsBuffer buffer;
        private readonly InteractorGpu[] staging = new InteractorGpu[MAX_INTERACTORS];
        private bool warnedOverflow;
        private int  previousFilled = -1;

        /// <summary>Stable GPU buffer reference — bind once; update the count uniform each frame.</summary>
        public GraphicsBuffer Buffer => this.buffer;

        /// <summary>Valid records written in the last <see cref="Upload"/>.</summary>
        public int Count { get; private set; }

        public GpuGrassInteractorBuffer()
        {
            this.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MAX_INTERACTORS, STRIDE);
        }

        public void Dispose()
        {
            if (this.buffer != null)
                this.buffer.Release();
        }

        /// <summary>
        /// Snapshots <paramref name="active"/> into the GPU buffer (up to <see cref="MAX_INTERACTORS"/>).
        /// Fake-null stale entries are skipped; overflow is dropped with a one-time warning. Idle frames
        /// (zero now and zero last frame) skip the redundant upload.
        /// </summary>
        public void Upload(IReadOnlyList<GrassInteractor> active)
        {
            int filled = 0;
            for (int i = 0; i < active.Count; ++i)
            {
                GrassInteractor interactor = active[i];
                if (interactor == null) // fake-null stale entry from edit-mode domain reload
                    continue;

                if (filled >= MAX_INTERACTORS)
                {
                    if (!this.warnedOverflow)
                    {
                        Debug.LogWarning(
                            $"[GpuGrassInteractorBuffer] Active interactor count exceeds MAX_INTERACTORS " +
                            $"({MAX_INTERACTORS}). Overflow entries are dropped.");
                        this.warnedOverflow = true;
                    }
                    break;
                }

                this.staging[filled] = new InteractorGpu
                {
                    posWS    = interactor.WorldPosition,
                    radius   = interactor.Radius,
                    strength = interactor.Strength,
                    pad0     = 0f, pad1 = 0f, pad2 = 0f,
                };
                filled++;
            }

            this.Count = filled;

            // Idle-skip: no interactors now AND none last frame → buffer already in its (count==0-gated)
            // state; skip the redundant full upload. Any non-zero frame + the transition back to zero
            // still upload so a future read never sees stale data.
            if (filled == 0 && this.previousFilled == 0) return;
            this.previousFilled = filled;

            // Upload the full staging array — the VS loop is bounded by _InteractorCount, so the
            // unwritten tail is never read (avoids a variable-length SetData overload).
            this.buffer.SetData(this.staging);
        }
    }
}
