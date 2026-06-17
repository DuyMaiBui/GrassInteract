#nullable enable
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Phase 6: Owns the per-frame <see cref="GraphicsBuffer"/> that carries up to
    /// <see cref="MAX_INTERACTORS"/> interactor records to the GPU.
    ///
    /// <para>
    /// One instance lives inside <see cref="GrassGpuEngine"/>. Each call to
    /// <see cref="Upload"/> snapshots <see cref="GrassInteractor.Active"/>, skips fake-null
    /// stale entries (edit-mode domain-reload guard — same pattern as
    /// <see cref="GrassBendSimulator.Step"/>), fills the staging array, and calls
    /// <see cref="GraphicsBuffer.SetData"/>. The GPU buffer is allocated once in the
    /// constructor and reused every frame; no GC is produced after construction.
    /// </para>
    ///
    /// <para>
    /// Struct layout: <c>InteractorGpu</c> is 32 B (float3 + 5 floats) matching the HLSL
    /// <c>GrassInteractorGpu</c> struct declared in <c>GrassInteractIndirect.shader</c>.
    /// </para>
    /// </summary>
    internal sealed class GrassInteractorBuffer : System.IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Maximum number of interactors uploaded to the GPU per frame.</summary>
        public const int MAX_INTERACTORS = 16;

        /// <summary>
        /// Stride in bytes of <see cref="InteractorGpu"/>.
        /// float3(12) + float(4) + float(4) + float(4) + float(4) + float(4) = 32 B.
        /// Must match the HLSL <c>GrassInteractorGpu</c> struct stride.
        /// </summary>
        public const int STRIDE = 32;

        // ── Blittable GPU record — must match HLSL GrassInteractorGpu exactly ─

        /// <summary>
        /// Blittable record matching <c>GrassInteractorGpu</c> in GrassInteractIndirect.shader.
        /// Layout: float3 posWS (12 B) + float radius (4 B) + float strength (4 B)
        ///         + float pad0 (4 B) + float pad1 (4 B) + float pad2 (4 B) = 32 B.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct InteractorGpu
        {
            public Vector3 posWS;    // 12 B
            public float   radius;   //  4 B
            public float   strength; //  4 B
            public float   pad0;     //  4 B  — stride padding only
            public float   pad1;     //  4 B
            public float   pad2;     //  4 B
            // Total: 32 B
        }

        // ── Fields ────────────────────────────────────────────────────────────

        private readonly GraphicsBuffer buffer;
        private readonly InteractorGpu[] staging = new InteractorGpu[MAX_INTERACTORS];
        private bool warnedOverflow;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>The GPU buffer. Stable reference — bind once; update the count uniform each frame.</summary>
        public GraphicsBuffer Buffer => this.buffer;

        /// <summary>Number of valid interactor records written in the last <see cref="Upload"/> call.</summary>
        public int Count { get; private set; }

        // ── Construction / Disposal ───────────────────────────────────────────

        /// <summary>
        /// Allocates the <see cref="GraphicsBuffer"/> once. Lightweight — call from
        /// <see cref="GrassGpuEngine.Build"/>.
        /// </summary>
        public GrassInteractorBuffer()
        {
            this.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MAX_INTERACTORS, STRIDE);
        }

        /// <summary>Releases the GPU buffer. Unity-null-safe.</summary>
        public void Dispose()
        {
            if (this.buffer != null)
                this.buffer.Release();
        }

        // ── Per-frame upload ──────────────────────────────────────────────────

        /// <summary>
        /// Snapshots <paramref name="active"/>, converts each interactor world position to painting
        /// space via <paramref name="binder"/> (no-op when <paramref name="binder"/> is null or
        /// <see cref="WorldRootBinder.IsIdentity"/>), copies up to <see cref="MAX_INTERACTORS"/>
        /// records into the staging array, and calls <see cref="GraphicsBuffer.SetData"/>.
        /// Fake-null stale entries (edit-mode domain-reload artefacts) are skipped.
        /// If <c>active.Count &gt; MAX_INTERACTORS</c> a warning is logged ONCE and the
        /// overflow entries are dropped silently.
        /// </summary>
        public void Upload(IReadOnlyList<GrassInteractor> active, WorldRootBinder? binder)
        {
            int filled = 0;
            bool convertPositions = binder != null && !binder.IsIdentity;

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
                            $"[GrassInteractorBuffer] Active interactor count exceeds MAX_INTERACTORS ({MAX_INTERACTORS}). " +
                            "Overflow entries will be dropped. Reduce the number of active GrassInteractor components.");
                        this.warnedOverflow = true;
                    }
                    break;
                }

                Vector3 pos = interactor.WorldPosition;
                if (convertPositions)
                    pos = binder.WorldToPainting(pos);

                this.staging[filled] = new InteractorGpu
                {
                    posWS    = pos,
                    radius   = interactor.Radius,
                    strength = interactor.Strength,
                    pad0     = 0f,
                    pad1     = 0f,
                    pad2     = 0f,
                };
                filled++;
            }

            this.Count = filled;

            // Upload the full 16-element staging array — the VS loop is bounded by _InteractorCount,
            // so unwritten tail elements are never read. This avoids a variable-length SetData overload.
            this.buffer.SetData(this.staging);
        }
    }
}
