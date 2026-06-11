#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// The C# heart of the bend system: a single per-frame pass that rebuilds every blade's per-instance
    /// <see cref="Matrix4x4"/> so grass sways with wind and leans AWAY from nearby
    /// <see cref="GrassInteractor"/>s, recovering toward upright once no interactor is in range. ALL state
    /// (lean direction per blade, output matrices) lives in plain C# arrays — there is NO GPU readback; the
    /// renderer just draws the matrices this produces.
    ///
    /// Ownership: this holds references to the scatter's base TRS slabs + parallel base-position slabs, and
    /// allocates its own per-blade state (<c>bendState</c>, <c>phase</c>, decomposed <c>baseYaw</c>/<c>baseScale</c>)
    /// plus the OUTPUT render slabs ONCE at construction — <see cref="Step"/> reuses them, so the per-frame
    /// path allocates nothing (zero GC).
    ///
    /// Perf ceiling: the wind term runs for EVERY blade each frame (the bend term early-outs for out-of-range
    /// upright blades). At the demo blade count this is comfortably within budget; ~50k blades is the
    /// documented soft ceiling. Escape hatch (NOT implemented here): move the wind sway back into a one-line
    /// <c>_Time</c> term in the dumb shader if a mobile frame budget needs it — the bend stays in C# regardless.
    /// </summary>
    public sealed class GrassBendSimulator
    {
        // Wind phase spatial-frequency vector — mirrors the old shader's spatialPhase so the look is preserved.
        private static readonly Vector2 PHASE_FREQ = new(0.37f, 0.21f);

        // Lean tuning. DEG_PER_METRE maps a lean vector's magnitude (metres of tip offset) to degrees of
        // rigid rotation about the base; MAX_LEAN_DEGREES clamps the total so a heavy bend can't distort the
        // blade past a believable lean. Named constants (no inline magic numbers per The1Studio conventions).
        private const float DEG_PER_METRE = 55f;
        private const float MAX_LEAN_DEGREES = 80f;

        private const float TWO_PI = Mathf.PI * 2f;

        private readonly Matrix4x4[][] baseSlabs;
        private readonly Vector3[][] basePositionSlabs;
        private readonly int[] slabCounts;

        // Per-blade state, slab-parallel (same [b][k] layout as the base slabs).
        private readonly Quaternion[][] baseYaw;   // yaw extracted once from each base matrix
        private readonly float[][] baseScale;      // uniform scale extracted once from each base matrix
        private readonly float[][] phase;          // precomputed wind phase per blade (out-of-lockstep sway)
        private readonly Vector2[][] bendState;    // current lean (XZ), recovers toward zero

        // OUTPUT — reused every frame, allocated once. Same slab layout; fed straight to GrassRenderer.
        private readonly Matrix4x4[][] renderSlabs;

        // Config snapshot (the simulator is rebuilt on a config change, so a snapshot is safe + avoids a
        // ScriptableObject property fetch per blade per frame).
        private readonly Vector2 windDir;
        private readonly float windStrength;
        private readonly float windFrequency;
        private readonly float bendStrength;
        private readonly float flatten;
        private readonly float recoveryRate;

        // Time source that advances in BOTH edit and play mode (Time.timeSinceLevelLoad is play-only). Driven
        // by the accumulated dt passed into Step, so wind animates live in the editor too.
        private float time;

        public GrassBendSimulator(GrassScatterResult scatter, ScatterLayer layer)
        {
            this.baseSlabs = scatter.BaseSlabs;
            this.basePositionSlabs = scatter.BasePositionSlabs;
            this.slabCounts = scatter.SlabCounts;

            var wind = layer.Wind;
            var deform = layer.Deform;

            Vector2 dir = wind.WindDirection;
            this.windDir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.right;
            this.windStrength = wind.WindStrength;
            this.windFrequency = wind.WindFrequency;
            this.bendStrength = deform.BendStrength;
            this.flatten = deform.Flatten;
            this.recoveryRate = deform.RecoveryRate;

            float windNoiseScale = wind.WindNoiseScale;

            int slabs = this.baseSlabs.Length;
            this.baseYaw = new Quaternion[slabs][];
            this.baseScale = new float[slabs][];
            this.phase = new float[slabs][];
            this.bendState = new Vector2[slabs][];
            this.renderSlabs = new Matrix4x4[slabs][];

            for (int b = 0; b < slabs; ++b)
            {
                int count = this.slabCounts[b];
                Matrix4x4[] baseSlab = this.baseSlabs[b];
                Vector3[] posSlab = this.basePositionSlabs[b];

                int len = baseSlab.Length; // 1023 — allocate full-width state so renderSlabs matches the slab
                var yawSlab = new Quaternion[len];
                var scaleSlab = new float[len];
                var phaseSlab = new float[len];
                var bendSlab = new Vector2[len];
                var outSlab = new Matrix4x4[len];

                for (int k = 0; k < count; ++k)
                {
                    Matrix4x4 m = baseSlab[k];
                    // Decompose the base matrix ONCE: yaw rotation + uniform scale (placement is uniform-scale,
                    // see GrassScatter.Build). lossyScale.x is the uniform factor.
                    yawSlab[k] = m.rotation;
                    scaleSlab[k] = m.lossyScale.x;

                    Vector3 p = posSlab[k];
                    phaseSlab[k] = (p.x * PHASE_FREQ.x + p.z * PHASE_FREQ.y) * windNoiseScale * TWO_PI;
                    // bendSlab[k] starts at Vector2.zero (upright); outSlab[k] is filled on the first Step.
                }

                this.baseYaw[b] = yawSlab;
                this.baseScale[b] = scaleSlab;
                this.phase[b] = phaseSlab;
                this.bendState[b] = bendSlab;
                this.renderSlabs[b] = outSlab;
            }
        }

        /// <summary>Output render matrices — reused every frame (zero per-frame GC). Feed straight to the renderer.</summary>
        public Matrix4x4[][] RenderSlabs => this.renderSlabs;

        /// <summary>Valid instance count per slab (same array the scatter produced).</summary>
        public int[] SlabCounts => this.slabCounts;

        /// <summary>
        /// Current lean (XZ) of the blade at <paramref name="flatIndex"/> (flat index across all slabs, in
        /// slab order). Non-zero while an interactor is leaning it; decays toward zero after the interactor
        /// leaves. Exposed for C# verification of the bend state — no GPU readback needed.
        /// </summary>
        public Vector2 GetBendState(int flatIndex)
        {
            if (flatIndex < 0)
                return Vector2.zero;

            for (int b = 0; b < this.bendState.Length; ++b)
            {
                int count = this.slabCounts[b];
                if (flatIndex < count)
                    return this.bendState[b][flatIndex];
                flatIndex -= count;
            }
            return Vector2.zero;
        }

        /// <summary>
        /// The single per-frame pass: advances wind, applies interactor lean + recovery, and rebuilds every
        /// blade's render matrix. <paramref name="dt"/> is the frame delta (Time.deltaTime in play, a fixed
        /// step in edit). Allocation-free.
        /// </summary>
        public void Step(float dt)
        {
            this.time += dt;

            // Snapshot the live interactor registry. Edit-mode domain reloads can leave fake-null entries that
            // OnDisable never cleaned; skip them so a stale entry can't NRE the per-blade loop.
            System.Collections.Generic.IReadOnlyList<GrassInteractor> interactors = GrassInteractor.Active;
            float recoveryStep = this.recoveryRate * dt;

            for (int b = 0; b < this.renderSlabs.Length; ++b)
            {
                int count = this.slabCounts[b];
                Vector3[] posSlab = this.basePositionSlabs[b];
                Quaternion[] yawSlab = this.baseYaw[b];
                float[] scaleSlab = this.baseScale[b];
                float[] phaseSlab = this.phase[b];
                Vector2[] bendSlab = this.bendState[b];
                Matrix4x4[] outSlab = this.renderSlabs[b];

                for (int k = 0; k < count; ++k)
                {
                    Vector3 basePos = posSlab[k];

                    // Wind: a small XZ lean vector that sways over time, per-blade phase keeps it out of lockstep.
                    float wave = Mathf.Sin(this.time * this.windFrequency + phaseSlab[k]) * this.windStrength;
                    Vector2 windTilt = this.windDir * wave;

                    // Bend: accumulate lean AWAY from every interactor whose footprint covers this blade.
                    Vector2 bendTarget = Vector2.zero;
                    bool anyInRange = false;
                    for (int it = 0; it < interactors.Count; ++it)
                    {
                        GrassInteractor interactor = interactors[it];
                        if (interactor == null) // skip fake-null stale entries (edit-mode reload)
                            continue;

                        Vector3 ip = interactor.WorldPosition;
                        float dx = basePos.x - ip.x;
                        float dz = basePos.z - ip.z;
                        float radius = interactor.Radius;
                        float sqrD = dx * dx + dz * dz;
                        if (radius <= 0f || sqrD >= radius * radius)
                            continue; // out of this interactor's footprint

                        float d = Mathf.Sqrt(sqrD);
                        float falloff = 1f - d / radius;
                        // Away direction (XZ). At d≈0 the direction is degenerate; fall back to no push there.
                        Vector2 awayDir = d > 1e-4f ? new Vector2(dx / d, dz / d) : Vector2.zero;
                        bendTarget += awayDir * (falloff * interactor.Strength * this.bendStrength);
                        anyInRange = true;
                    }

                    Vector2 bend = bendSlab[k];

                    // Early-out: no interactor in range AND already upright → skip recovery + leave bend at zero.
                    // This is the "only pay near interactors" win for the dominant out-of-range blade majority.
                    if (anyInRange || bend.sqrMagnitude > 1e-8f)
                    {
                        bend = Vector2.MoveTowards(bend, bendTarget, recoveryStep);
                        bendSlab[k] = bend;
                    }

                    // Compose the rigid lean about the base (pivot y=0): total lean = wind + bend, mapped to a
                    // small-angle rotation tilting the up-axis toward (lean.x, lean.y), clamped to a max angle.
                    Vector2 totalLean = windTilt + bend;
                    Quaternion lean = LeanRotation(totalLean);

                    // Flatten: a trampled blade mats DOWN by losing height (local-Y scale only), proportional to
                    // how hard it is bent (the interactor lean, NOT wind). bendStrength is the full-trample lean,
                    // so bend.magnitude / bendStrength is the 0..1 trample fraction; flatten is the height loss at
                    // full trample. X/Z keep base scale, so the blade compresses straight down about its base.
                    float scaleXZ = scaleSlab[k];
                    float scaleY = scaleXZ;
                    if (this.flatten > 0f && this.bendStrength > 1e-5f)
                    {
                        float trampleFrac = Mathf.Clamp01(bend.magnitude / this.bendStrength);
                        scaleY = scaleXZ * (1f - this.flatten * trampleFrac);
                    }

                    outSlab[k] = Matrix4x4.TRS(basePos, lean * yawSlab[k],
                        new Vector3(scaleXZ, scaleY, scaleXZ));
                }
            }
        }

        /// <summary>
        /// Maps an XZ lean vector (metres of tip offset) to a rigid rotation about the blade base. Small-angle
        /// Euler form: a +X lean rolls the up-axis toward +X (negative Z-Euler), a +Z lean pitches it toward
        /// +Z (positive X-Euler). The total lean magnitude is clamped to <see cref="MAX_LEAN_DEGREES"/> so a
        /// heavy bend never over-rotates into distortion.
        /// </summary>
        private static Quaternion LeanRotation(Vector2 lean)
        {
            float pitchDeg = lean.y * DEG_PER_METRE;
            float rollDeg = -lean.x * DEG_PER_METRE;

            // Clamp the combined tilt magnitude to the max lean angle (scale both components uniformly).
            float magDeg = Mathf.Sqrt(pitchDeg * pitchDeg + rollDeg * rollDeg);
            if (magDeg > MAX_LEAN_DEGREES)
            {
                float s = MAX_LEAN_DEGREES / magDeg;
                pitchDeg *= s;
                rollDeg *= s;
            }

            return Quaternion.Euler(pitchDeg, 0f, rollDeg);
        }
    }
}
