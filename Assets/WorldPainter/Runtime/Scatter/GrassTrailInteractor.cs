#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// A trail-style disturbance that records a FIFO of world-space samples as the GameObject moves.
    /// Phase 2 upload reads <see cref="Samples"/> each frame to drive GPU bend along the trail path.
    ///
    /// Stroke breaks: set <see cref="Emitting"/> = false to pause recording. On resume, the next
    /// appended sample will have <see cref="TrailSample.StrokeStart"/> = true so Phase 2 can skip
    /// the gap segment in the GPU upload.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GrassInteractorData))]
    public sealed class GrassTrailInteractor : MonoBehaviour
    {
        [Min(0f)]
        [SerializeField] private float trailDuration = 5f;

        [Min(0f)]
        [SerializeField] private float minVertexDistance = 0.25f;

        [Range(0f, 1f)]
        [SerializeField] private float centerZonePercent = 0.4f;

        private GrassInteractorData? dataCache;
        private GrassInteractorData Data => this.dataCache ??= this.GetComponent<GrassInteractorData>();

        // ── Public API ──────────────────────────────────────────────────────────────

        /// <summary>When false, new samples are not appended. Existing samples still age and evict.</summary>
        public bool Emitting { get; set; } = true;

        public float TrailDuration     => this.trailDuration;
        public float WorldRadius       => this.Data.WorldRadius;
        public float MaxBendDegrees    => this.Data.MaxBendDegrees;
        public float CenterZonePercent => this.centerZonePercent;
        public float Strength          => this.Data.Strength;

        // ── Per-sample state ─────────────────────────────────────────────────────────

        /// <summary>One recorded world-space position along the trail.</summary>
        internal struct TrailSample
        {
            public Vector3 PosWS;
            public float   Age;

            /// <summary>True when this sample immediately follows a stroke break (pen-lift gap).</summary>
            public bool StrokeStart;
        }

        // ── Sampler internals ────────────────────────────────────────────────────────

        private const int MAX_SAMPLES_PER_TRAIL = 256;

        private readonly List<TrailSample> samples = new(MAX_SAMPLES_PER_TRAIL);
        private bool wasEmittingLastFrame = true;
        private bool pendingStrokeBreak;

        /// <summary>Pre-allocated FIFO of trail samples. Read-only view for Phase 2 upload.</summary>
        internal IReadOnlyList<TrailSample> Samples => this.samples;

        // ── Static registry ──────────────────────────────────────────────────────────

        private static readonly List<GrassTrailInteractor> active = new();

        /// <summary>All currently-enabled trail interactors. Read by Phase 2 upload each frame.</summary>
        public static IReadOnlyList<GrassTrailInteractor> Active => active;

        // ── Lifecycle ────────────────────────────────────────────────────────────────

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

            // 1) Tick ages on ALL existing samples (regardless of Emitting).
            for (int i = 0; i < this.samples.Count; i++)
            {
                TrailSample s = this.samples[i];
                s.Age += dt;
                this.samples[i] = s;
            }

            // 2) Evict samples whose age > trailDuration. Walk forward; first survivor index is N.
            int firstAlive = 0;
            while (firstAlive < this.samples.Count && this.samples[firstAlive].Age > this.trailDuration)
                firstAlive++;
            if (firstAlive > 0)
                this.samples.RemoveRange(0, firstAlive);

            // 3) Edge-detect Emitting (R5): if it just flipped false this frame, queue a stroke break.
            bool emittingNow = this.Emitting;
            if (this.wasEmittingLastFrame && !emittingNow)
                this.pendingStrokeBreak = true;

            // 4) If not emitting, skip sample emission (steps 5-6).
            if (emittingNow)
            {
                // 5) Emit a new sample if list empty OR moved > minVertexDistance.
                bool firstSample = this.samples.Count == 0;
                bool moved = !firstSample &&
                             Vector3.Distance(this.transform.position, this.samples[^1].PosWS) > this.minVertexDistance;
                if (firstSample || moved)
                {
                    if (this.samples.Count >= MAX_SAMPLES_PER_TRAIL)
                        this.samples.RemoveAt(0); // overflow = drop oldest

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

        // ── CPU-tier warn ────────────────────────────────────────────────────────────

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool hasValidated;

        private void Update()
        {
            if (this.hasValidated) return;
            this.hasValidated = true;

            ScatterField[] fields = FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            bool anyGpu = false;
            foreach (var f in fields)
                if (f.isActiveAndEnabled && f.ActiveTierName == "GPU") { anyGpu = true; break; }
            if (!anyGpu)
                Debug.LogWarning($"[{nameof(GrassTrailInteractor)}] '{this.name}' is active but no " +
                    "GPU-tier ScatterField exists. GrassTrailInteractor is GPU-only - " +
                    "this trail will have no visual effect.", this);
        }
#endif

    }
}
