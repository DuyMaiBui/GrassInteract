#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// A trail-style disturbance that records a FIFO of world-space samples as the GameObject moves, so
    /// grass stays flattened in a fading path BEHIND the mover (tire tracks / footprints) instead of only
    /// bending at the current position. The renderer reads <see cref="Samples"/> each frame.
    ///
    /// Stroke breaks: set <see cref="Emitting"/> = false to pause recording; on resume the next sample is
    /// flagged <see cref="TrailSample.StrokeStart"/> so the renderer can skip the gap segment.
    ///
    /// Standalone module note: bend params (radius / strength / max-bend) are folded inline, as in
    /// <see cref="GrassInteractor"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("GPUGrass/Grass Trail Interactor")]
    public sealed class GrassTrailInteractor : MonoBehaviour
    {
        [Tooltip("Footprint radius in world metres.")]
        [Min(0f)]
        [SerializeField] private float worldRadius = 2f;

        [Tooltip("Lean strength along the trail (0 = none).")]
        [Min(0f)]
        [SerializeField] private float strength = 1f;

        [Tooltip("Maximum bend angle in degrees at full strength.")]
        [Range(0f, 90f)]
        [SerializeField] private float maxBendDegrees = 80f;

        [Tooltip("Seconds a flattened trail sample persists before recovering.")]
        [Min(0f)]
        [SerializeField] private float trailDuration = 5f;

        [Tooltip("Minimum metres the mover must travel before a new sample is recorded.")]
        [Min(0f)]
        [SerializeField] private float minVertexDistance = 0.25f;

        [Tooltip("Fraction of the radius fully flattened (the matted core of the track).")]
        [Range(0f, 1f)]
        [SerializeField] private float centerZonePercent = 0.4f;

        // ── Public API ────────────────────────────────────────────────────────
        /// <summary>When false, new samples are not appended. Existing samples still age and evict.</summary>
        public bool Emitting { get; set; } = true;

        public float WorldRadius       => this.worldRadius;
        public float Strength          => this.strength;
        public float MaxBendDegrees    => this.maxBendDegrees;
        public float TrailDuration     => this.trailDuration;
        public float CenterZonePercent => this.centerZonePercent;

        // ── Per-sample state ──────────────────────────────────────────────────
        /// <summary>One recorded world-space position along the trail.</summary>
        public struct TrailSample
        {
            public Vector3 PosWS;
            public float   Age;

            /// <summary>True when this sample immediately follows a stroke break (pen-lift gap).</summary>
            public bool StrokeStart;
        }

        private const int MAX_SAMPLES_PER_TRAIL = 256;

        private readonly List<TrailSample> samples = new(MAX_SAMPLES_PER_TRAIL);
        private bool wasEmittingLastFrame = true;
        private bool pendingStrokeBreak;

        /// <summary>FIFO of trail samples (read-only view for the renderer upload).</summary>
        public IReadOnlyList<TrailSample> Samples => this.samples;

        // ── Static registry ───────────────────────────────────────────────────
        private static readonly List<GrassTrailInteractor> active = new();

        /// <summary>All currently-enabled trail interactors. Read by the renderer each frame.</summary>
        public static IReadOnlyList<GrassTrailInteractor> Active => active;

        private void OnEnable()
        {
            if (!active.Contains(this))
                active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
            this.samples.Clear();
            this.pendingStrokeBreak = false;
            this.wasEmittingLastFrame = true;
        }

        private void LateUpdate()
        {
            float dt = Application.isPlaying ? Time.deltaTime : Time.unscaledDeltaTime;

            // 1) Age all existing samples (regardless of Emitting).
            for (int i = 0; i < this.samples.Count; i++)
            {
                TrailSample s = this.samples[i];
                s.Age += dt;
                this.samples[i] = s;
            }

            // 2) Evict samples older than trailDuration (FIFO front).
            int firstAlive = 0;
            while (firstAlive < this.samples.Count && this.samples[firstAlive].Age > this.trailDuration)
                firstAlive++;
            if (firstAlive > 0)
                this.samples.RemoveRange(0, firstAlive);

            // 3) Edge-detect Emitting → queue a stroke break on the falling edge.
            bool emittingNow = this.Emitting;
            if (this.wasEmittingLastFrame && !emittingNow)
                this.pendingStrokeBreak = true;

            // 4-5) Emit a new sample if empty or moved far enough.
            if (emittingNow)
            {
                bool firstSample = this.samples.Count == 0;
                bool moved = !firstSample &&
                             Vector3.Distance(this.transform.position, this.samples[^1].PosWS) > this.minVertexDistance;
                if (firstSample || moved)
                {
                    if (this.samples.Count >= MAX_SAMPLES_PER_TRAIL)
                        this.samples.RemoveAt(0); // overflow → drop oldest

                    this.samples.Add(new TrailSample
                    {
                        PosWS       = this.transform.position,
                        Age         = 0f,
                        StrokeStart = this.pendingStrokeBreak || firstSample,
                    });
                    this.pendingStrokeBreak = false;
                }
            }

            // 6) Cache for next frame's edge detection.
            this.wasEmittingLastFrame = emittingNow;
        }
    }
}
