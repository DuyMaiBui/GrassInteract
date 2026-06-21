#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Owns the per-frame structured <see cref="GraphicsBuffer"/> carrying up to
    /// <see cref="MAX_TRAIL_SEGMENTS"/> trail segments (cross-interactor total) to the GPU. One instance
    /// lives inside <see cref="GpuGrassRenderer"/>. Each <see cref="Upload"/> flattens every active
    /// <see cref="GrassTrailInteractor"/>'s FIFO samples into segments, skips stroke-break pairs
    /// (pen-lift gaps), and uploads. The buffer is bound globally once; SetData updates contents without
    /// rebinding. Allocated once — no GC after construction.
    ///
    /// Standalone note: sample positions are world-space (no painting↔world root transform), so the
    /// WorldPainter <c>WorldRootBinder</c> conversion is dropped.
    /// </summary>
    internal sealed class GpuGrassTrailBuffer : IDisposable
    {
        /// <summary>Maximum trail segments uploaded per frame (cross-interactor total).</summary>
        public const int MAX_TRAIL_SEGMENTS = 128;

        /// <summary>
        /// Stride of <see cref="TrailSegmentGpu"/>: 48 B (16-byte aligned).
        /// Must match the HLSL <c>GrassTrailSegmentGpu</c> struct.
        /// </summary>
        public const int STRIDE = 48;

        /// <summary>Blittable record mirroring HLSL <c>GrassTrailSegmentGpu</c> (48 B). Field order LOCKED.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct TrailSegmentGpu // 48 B
        {
            public Vector3 PosA;        // 12
            public float   Radius;      //  4  -> 16
            public Vector3 PosB;        // 12
            public float   Alpha;       //  4  -> 32
            public float   MaxBendRad;  //  4
            public float   CenterPct;   //  4
            public float   Strength;    //  4
            public float   Pad;         //  4  -> 48
        }

        private static readonly int GrassTrailSegmentsId     = Shader.PropertyToID("_GrassTrailSegments");
        private static readonly int GrassTrailSegmentCountId = Shader.PropertyToID("_GrassTrailSegmentCount");

        private readonly GraphicsBuffer buffer;
        private readonly TrailSegmentGpu[] staging = new TrailSegmentGpu[MAX_TRAIL_SEGMENTS];
        private bool warnedOverflow;

        public GpuGrassTrailBuffer()
        {
            this.buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MAX_TRAIL_SEGMENTS, STRIDE);
        }

        /// <summary>Binds the buffer as the global <c>_GrassTrailSegments</c> resource. Call once after construction.</summary>
        public void BindGlobal()
        {
            Shader.SetGlobalBuffer(GrassTrailSegmentsId, this.buffer);
        }

        public void Dispose()
        {
            if (this.buffer != null)
                this.buffer.Release();
        }

        /// <summary>
        /// Flattens every active trail's samples into world-space segments (skipping stroke-break gaps),
        /// uploads up to <see cref="MAX_TRAIL_SEGMENTS"/>, and sets the <c>_GrassTrailSegmentCount</c> uniform.
        /// Fake-null stale entries are skipped; overflow is dropped with a one-time warning.
        /// </summary>
        public void Upload(IReadOnlyList<GrassTrailInteractor> interactors)
        {
            int segCount = 0;

            for (int t = 0; t < interactors.Count && segCount < MAX_TRAIL_SEGMENTS; t++)
            {
                GrassTrailInteractor trail = interactors[t];
                if (trail == null) continue; // fake-null guard (edit-mode domain reload)

                IReadOnlyList<GrassTrailInteractor.TrailSample> s = trail.Samples;
                if (s.Count < 2) continue;

                float maxBendRad = Mathf.Deg2Rad * trail.MaxBendDegrees;
                float radius     = trail.WorldRadius;
                float centerPct  = trail.CenterZonePercent;
                float strength   = trail.Strength;
                float duration   = Mathf.Max(trail.TrailDuration, 1e-4f);

                for (int i = 1; i < s.Count && segCount < MAX_TRAIL_SEGMENTS; i++)
                {
                    if (s[i].StrokeStart) continue; // pen lift — do NOT bridge the gap

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
                    $"[GpuGrassTrailBuffer] >{MAX_TRAIL_SEGMENTS} trail segments active — dropping overflow.");
                this.warnedOverflow = true;
            }

            Shader.SetGlobalInteger(GrassTrailSegmentCountId, segCount);
        }
    }
}
