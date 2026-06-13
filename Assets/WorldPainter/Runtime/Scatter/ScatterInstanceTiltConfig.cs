#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Whole-instance rigid tilt configuration. Composed ONLY by <see cref="PropLayer"/>
    /// (mesh props), not by <see cref="GrassLayer"/> (grass blades). Drives the
    /// C#-simulated, GPU-instanced tilt-away + spring-back response of a prop instance to a moving
    /// <see cref="GrassInteractor"/> — distinct from the per-vertex wind/bend deform in the shader.
    ///
    /// <para>
    /// The simulation (<see cref="InstanceTiltSimulator"/>) keeps per-instance tilt state in C# arrays
    /// so recovery is a true timed spring-back (<see cref="RecoveryRate"/>), not an instant snap.
    /// </para>
    /// </summary>
    [System.Serializable]
    public struct ScatterInstanceTiltConfig
    {
        [BoxGroup("Tilt")]
        [Tooltip("If true, this layer's instances rigidly tilt away from interactors.")]
        [SerializeField] private bool affectedByInteractors;

        [BoxGroup("Tilt")]
        [Range(0f, 4f)]
        [Tooltip("Away-lean amplitude scalar at full interactor magnitude (maps to tilt degrees, clamped by Max Tilt Angle).")]
        [SerializeField] private float tiltStrength;

        [BoxGroup("Tilt")]
        [Range(0f, 89f)]
        [Tooltip("Maximum rigid tilt angle in degrees — clamps a heavy push so a prop never over-rotates.")]
        [SerializeField] private float maxTiltAngle;

        [BoxGroup("Tilt")]
        [Min(0f)]
        [Tooltip("Spring-back speed in degrees/second toward upright after the interactor leaves.")]
        [SerializeField] private float recoveryRate;

        [BoxGroup("Tilt")]
        [Min(0f)]
        [Tooltip("Extra metres added to each interactor's footprint radius for the tilt response.")]
        [SerializeField] private float radiusPadding;

        [BoxGroup("Tilt")]
        [Tooltip("If true, per-instance colliders rotate with the visual tilt each frame (more correct, costs per-frame collider updates). Default false = colliders stay upright.")]
        [SerializeField] private bool colliderFollowsTilt;

        public ScatterInstanceTiltConfig(
            bool affectedByInteractors,
            float tiltStrength,
            float maxTiltAngle,
            float recoveryRate,
            float radiusPadding,
            bool colliderFollowsTilt)
        {
            this.affectedByInteractors = affectedByInteractors;
            this.tiltStrength = tiltStrength;
            this.maxTiltAngle = maxTiltAngle;
            this.recoveryRate = recoveryRate;
            this.radiusPadding = radiusPadding;
            this.colliderFollowsTilt = colliderFollowsTilt;
        }

        public bool AffectedByInteractors => this.affectedByInteractors;
        public float TiltStrength => this.tiltStrength;
        public float MaxTiltAngle => this.maxTiltAngle;
        public float RecoveryRate => this.recoveryRate;
        public float RadiusPadding => this.radiusPadding;
        public bool ColliderFollowsTilt => this.colliderFollowsTilt;
    }
}
