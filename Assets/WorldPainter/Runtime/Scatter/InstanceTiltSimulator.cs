#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// C# per-instance rigid-tilt simulator for prop layers.
    /// Mirrors <see cref="GrassBendSimulator"/> for whole-instance props: each frame it rebuilds a
    /// per-instance tilt quaternion that leans the WHOLE instance AWAY from nearby
    /// <see cref="GrassInteractor"/>s and recovers toward upright at <c>recoveryRate</c>. Recovery state
    /// (the lean vector) persists across frames, so the spring-back is a true timed decay — not a snap.
    ///
    /// <para>
    /// Index contract: base positions are read from <see cref="ChunkedInstanceBuffer.Instances"/> (the
    /// chunk-sorted BAKED order), so element <c>i</c> of <see cref="TiltBuffer"/> corresponds to
    /// <c>_Instances[i]</c> in the shader — the same index the cull pass resolves via <c>_VisibleIndices</c>.
    /// </para>
    ///
    /// <para>
    /// NOTE: a plain (non-Burst) loop. This project does not auto-reference Unity.Mathematics/Burst into
    /// the predefined assembly; a Burst <c>IJobParallelFor</c> version is a perf follow-up that requires
    /// giving the runtime its own <c>.asmdef</c>. At tens of thousands of instances with a few interactors
    /// the per-frame cost here is sub-millisecond.
    /// </para>
    /// </summary>
    public sealed class InstanceTiltSimulator : System.IDisposable
    {
        // Lean-vector magnitude (tilt units) → degrees, mirroring GrassBendSimulator.DEG_PER_METRE.
        private const float DEG_PER_UNIT = 55f;

        private readonly int count;
        private readonly Vector3[] anchorPos;   // baked-order deform sampling anchors (pivot + yawRot*(offset*scale))
        private readonly Vector2[] currentLean; // persistent recovery state (XZ), recovers toward 0
        private readonly Vector4[] outTilt;     // quaternion (xyzw) per instance, uploaded each frame
        private GraphicsBuffer? tiltBuffer;

        private readonly float tiltStrength;
        private readonly float maxTiltDeg;
        private readonly float recoveryRate;
        private readonly float radiusPadding;

        /// <summary>
        /// Builds the simulator from the BAKED instance buffer (chunk-sorted order) + the layer's tilt config.
        /// </summary>
        public InstanceTiltSimulator(ChunkedInstanceBuffer baked, PropLayerScatterLayer layer)
        {
            ScatterInstanceTiltConfig tilt = layer.Tilt;
            this.tiltStrength  = tilt.TiltStrength;
            this.maxTiltDeg    = tilt.MaxTiltAngle;
            this.recoveryRate  = tilt.RecoveryRate;
            this.radiusPadding = tilt.RadiusPadding;

            var bakedInstances = baked.Instances; // baked (chunk-sorted) order; element i ↔ _Instances[i]
            this.count = bakedInstances?.Length ?? 0;

            int alloc = Mathf.Max(1, this.count);
            this.anchorPos   = new Vector3[alloc];
            this.currentLean = new Vector2[alloc];
            this.outTilt     = new Vector4[alloc];

            for (int i = 0; i < alloc; ++i)
                this.outTilt[i] = new Vector4(0f, 0f, 0f, 1f); // identity

            // Deform sampling anchor = pivot + yawRot*(localOffset * scale). Mirrors the shader anchor for the
            // common (non-oriented) prop case, where baseRot == Yaw(yaw) (the layer's
            // RotationOffsetEuler is always zero). For oriented props the shader additionally applies
            // surface-normal alignment + pitch/roll; this sim uses yaw-only, so the anchor's XZ proximity for
            // tilt is a close approximation there (documented; refine to full baseRot reconstruction if needed).
            Vector3 anchorLocal = layer.AnchorOffsetLocal;
            float scaleMax = baked.ScaleMax > 0f ? baked.ScaleMax : 1f;
            bool hasAnchor = anchorLocal != Vector3.zero;
            for (int i = 0; i < this.count; ++i)
            {
                Vector3 p = bakedInstances![i].posWS;
                if (hasAnchor)
                {
                    uint packed = bakedInstances[i].packedYawScale;
                    float yawDeg = (packed >> 16) / 65535f * 360f;
                    float scale  = (packed & 0xFFFFu) / 65535f * scaleMax;
                    this.anchorPos[i] = p + Quaternion.Euler(0f, yawDeg, 0f) * (anchorLocal * scale);
                }
                else
                {
                    this.anchorPos[i] = p;
                }
            }

            this.tiltBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, alloc, sizeof(float) * 4);
            this.tiltBuffer.SetData(this.outTilt);
        }

        /// <summary>Per-instance tilt quaternion buffer (xyzw), baked-order indexed. Bind as _InstanceTilt.</summary>
        public GraphicsBuffer? TiltBuffer => this.tiltBuffer;

        /// <summary>Baked instance count.</summary>
        public int Count => this.count;

        /// <summary>Read-only current tilt quaternions (for optional collider-follow-tilt).</summary>
        public Vector4[] TiltQuats => this.outTilt;

        /// <summary>Advances the tilt sim one frame and uploads the result to <see cref="TiltBuffer"/>.</summary>
        public void Step(float dt)
        {
            if (this.count == 0 || this.tiltBuffer == null) return;

            IReadOnlyList<GrassInteractor> active = GrassInteractor.Active;
            int n = active.Count;
            float recoveryStep = this.recoveryRate * dt;

            for (int i = 0; i < this.count; ++i)
            {
                // Sample interactor proximity from the deform anchor, not the pivot.
                Vector3 p = this.anchorPos[i];

                // Accumulate lean AWAY from every interactor whose footprint covers this instance.
                Vector2 target = Vector2.zero;
                for (int it = 0; it < n; ++it)
                {
                    GrassInteractor ia = active[it];
                    if (ia == null) continue;
                    float r = ia.Radius + this.radiusPadding;
                    if (r <= 0f) continue;
                    Vector3 ip = ia.WorldPosition;
                    float dx = p.x - ip.x;
                    float dz = p.z - ip.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr >= r * r) continue;
                    float d = Mathf.Sqrt(sqr);
                    float fall = 1f - d / r;
                    Vector2 away = d > 1e-4f ? new Vector2(dx / d, dz / d) : Vector2.zero;
                    target += away * (fall * ia.Strength * this.tiltStrength);
                }

                // Recover/advance the persistent lean toward target (MoveTowards).
                Vector2 cur = Vector2.MoveTowards(this.currentLean[i], target, recoveryStep);
                this.currentLean[i] = cur;

                // Map the lean vector to a rigid tilt quaternion about the instance pivot.
                float mag = cur.magnitude;
                if (mag < 1e-5f)
                {
                    this.outTilt[i] = new Vector4(0f, 0f, 0f, 1f);
                    continue;
                }
                Vector2 dir = cur / mag;
                float angleDeg = Mathf.Min(mag * DEG_PER_UNIT, this.maxTiltDeg);
                // Tilt the up-axis toward (dir.x, 0, dir.y): axis = cross(up, dir3) = (dir.y, 0, -dir.x).
                Vector3 axis = new Vector3(dir.y, 0f, -dir.x).normalized;
                Quaternion q = Quaternion.AngleAxis(angleDeg, axis);
                this.outTilt[i] = new Vector4(q.x, q.y, q.z, q.w);
            }

            this.tiltBuffer.SetData(this.outTilt);
        }

        public void Dispose()
        {
            this.tiltBuffer?.Release();
            this.tiltBuffer = null;
        }
    }
}
