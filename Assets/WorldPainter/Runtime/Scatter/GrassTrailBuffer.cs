#nullable enable
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter
{
    /// <summary>
    /// Phase 2: Owns the per-frame <see cref="GraphicsBuffer"/> that carries up to
    /// <see cref="MAX_TRAIL_SEGMENTS"/> trail segment records to the GPU.
    ///
    /// <para>
    /// One instance lives inside <see cref="GrassGpuEngine"/>. Each call to
    /// <see cref="Upload"/> snapshots <see cref="GrassTrailInteractor.Active"/>, flattens
    /// all per-trail samples into segments, skips stroke-break pairs (pen-lift gaps), fills
    /// the pre-allocated staging array, and calls <see cref="GraphicsBuffer.SetData"/>.
    /// The GPU buffer is allocated once in the constructor and reused every frame; no GC
    /// is produced after construction.
    /// </para>
    ///
    /// <para>
    /// Struct layout: <c>TrailSegmentGpu</c> is 48 B (float3 + float × 9 = 48 B, 16-byte aligned)
    /// matching the HLSL <c>TrailSegmentGpu</c> struct declared in Phase 3 shader.
    /// </para>
    /// </summary>
    internal sealed class GrassTrailBuffer : System.IDisposable
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Maximum number of trail segments uploaded to the GPU per frame (cross-interactor total).</summary>
        public const int MAX_TRAIL_SEGMENTS = 128;

        /// <summary>
        /// Stride in bytes of <see cref="TrailSegmentGpu"/>.
        /// float3(12) + float(4) + float3(12) + float(4) + float(4) + float(4) + float(4) + float(4) = 48 B.
        /// Must match the HLSL <c>TrailSegmentGpu</c> struct stride.
        /// </summary>
        public const int STRIDE = 48;

        // ── Blittable GPU record — must match HLSL TrailSegmentGpu exactly ────

        /// <summary>
        /// Blittable record matching <c>TrailSegmentGpu</c> in GrassInteractIndirect.shader (Phase 3).
        /// Field order is LOCKED — Phase 3 HLSL must mirror this byte-for-byte.
        /// Layout: float3 PosA (12 B) + float Radius (4 B) = 16 B
        ///         float3 PosB (12 B) + float Alpha (4 B) = 32 B
        ///         float MaxBendRad (4 B) + float CenterPct (4 B) + float Strength (4 B) + float Pad (4 B) = 48 B.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct TrailSegmentGpu              // 48 B total
        {
            public Vector3 PosA;        // 12
            public float   Radius;      //  4  -> 16
            public Vector3 PosB;        // 12
            public float   Alpha;       //  4  -> 32
            public float   MaxBendRad;  //  4
            public float   CenterPct;   //  4
            public float   Strength;    //  4
            public float   Pad;         //  4  -> 48 (16-byte aligned)
        }

        // ── Shader property IDs (cached once, zero per-frame string allocs) ──

        private static readonly int GrassTrailSegmentsId      = Shader.PropertyToID("_GrassTrailSegments");
        private static readonly int GrassTrailSegmentCountId  = Shader.PropertyToID("_GrassTrailSegmentCount");

        // ── Fields ────────────────────────────────────────────────────────────

        private readonly GraphicsBuffer buffer;
        private readonly TrailSegmentGpu[] staging = new TrailSegmentGpu[MAX_TRAIL_SEGMENTS];
        private bool warnedOverflow;

        // ── Construction / Disposal ───────────────────────────────────────────

        /// <summary>
        /// Allocates the <see cref="GraphicsBuffer"/> once and asserts the struct stride.
        /// Lightweight — call from <see cref="GrassGpuEngine.Build"/>.
        /// </summary>
        public GrassTrailBuffer()
        {
            System.Diagnostics.Debug.Assert(
                Marshal.SizeOf<TrailSegmentGpu>() == 48,
                "[GrassTrailBuffer] TrailSegmentGpu stride mismatch: expected 48 B.");

            this.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MAX_TRAIL_SEGMENTS, STRIDE);
        }

        /// <summary>
        /// Binds the buffer as a global shader resource (<c>_GrassTrailSegments</c>).
        /// Call ONCE from <see cref="GrassGpuEngine.Build"/> after construction.
        /// The same <see cref="GraphicsBuffer"/> reference is reused every frame — SetData updates
        /// contents without changing the binding.
        /// </summary>
        public void BindGlobal()
        {
            Shader.SetGlobalBuffer(GrassTrailSegmentsId, this.buffer);
        }

        /// <summary>Releases the GPU buffer. Unity-null-safe.</summary>
        public void Dispose()
        {
            if (this.buffer != null)
                this.buffer.Release();
        }

        // ── Per-frame upload ──────────────────────────────────────────────────

        /// <summary>
        /// Snapshots <paramref name="interactors"/>, flattens all per-trail samples into
        /// <see cref="TrailSegmentGpu"/> records, skips segment pairs bridging a stroke break,
        /// and uploads up to <see cref="MAX_TRAIL_SEGMENTS"/> records via
        /// <see cref="GraphicsBuffer.SetData"/>.
        ///
        /// <para>
        /// Fake-null stale entries (edit-mode domain-reload artefacts) are skipped.
        /// Overflow entries are dropped with a one-time warning.
        /// </para>
        /// </summary>
        internal void Upload(IReadOnlyList<GrassTrailInteractor> interactors)
        {
            int segCount = 0;

            for (int t = 0; t < interactors.Count && segCount < MAX_TRAIL_SEGMENTS; t++)
            {
                GrassTrailInteractor trail = interactors[t];
                if (trail == null) continue;  // fake-null guard (edit-mode domain reload)

                IReadOnlyList<GrassTrailInteractor.TrailSample> s = trail.Samples;
                if (s.Count < 2) continue;

                float maxBendRad = Mathf.Deg2Rad * trail.MaxBendDegrees;
                float radius     = trail.WorldRadius;
                float centerPct  = trail.CenterZonePercent;
                float strength   = trail.Strength;
                float duration   = Mathf.Max(trail.TrailDuration, 1e-4f);

                for (int i = 1; i < s.Count && segCount < MAX_TRAIL_SEGMENTS; i++)
                {
                    if (s[i].StrokeStart) continue;  // pen lift — do NOT bridge

                    float alphaA = 1f - s[i - 1].Age / duration;
                    float alphaB = 1f - s[i    ].Age / duration;
                    this.staging[segCount++] = new TrailSegmentGpu
                    {
                        PosA       = s[i - 1].PosWS,
                        Radius     = radius,
                        PosB       = s[i    ].PosWS,
                        Alpha      = 0.5f * (alphaA + alphaB),
                        MaxBendRad = maxBendRad,
                        CenterPct  = centerPct,
                        Strength   = strength,
                        Pad        = 0f,
                    };
                }
            }

            if (segCount > 0)
                this.buffer.SetData(this.staging, 0, 0, segCount);

            if (interactors.Count > 0 && segCount == MAX_TRAIL_SEGMENTS && !this.warnedOverflow)
            {
                Debug.LogWarning(
                    $"[GrassTrailBuffer] >{MAX_TRAIL_SEGMENTS} trail segments active - dropping overflow.");
                this.warnedOverflow = true;
            }

            Shader.SetGlobalInteger(GrassTrailSegmentCountId, segCount);
        }
    }
}
