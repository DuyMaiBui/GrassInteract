#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// A moving disturbance that leans grass AWAY from it — attach to a car, wheel, player, or any mover.
    /// On enable it joins a static registry (<see cref="Active"/>) that the grass renderer reads each frame;
    /// every blade inside this interactor's circular footprint leans away from the footprint center and
    /// recovers toward upright after the interactor leaves.
    ///
    /// Standalone module note: the former WorldPainter <c>GrassInteractorData</c> sibling has been folded
    /// into this component (radius / strength / max-bend are inline serialized fields), so GPUGrass needs
    /// no companion component.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("GPUGrass/Grass Interactor")]
    public sealed class GrassInteractor : MonoBehaviour
    {
        [Tooltip("Footprint radius in world metres.")]
        [Min(0f)]
        [SerializeField] private float worldRadius = 2f;

        [Tooltip("Lean strength at the footprint center (0 = none).")]
        [Min(0f)]
        [SerializeField] private float strength = 1f;

        [Tooltip("Maximum bend angle in degrees at full strength.")]
        [Range(0f, 90f)]
        [SerializeField] private float maxBendDegrees = 70f;

        /// <summary>World-space position of the footprint center (the transform position).</summary>
        public Vector3 WorldPosition => this.transform.position;

        /// <summary>Footprint radius in world metres.</summary>
        public float Radius => this.worldRadius;

        /// <summary>Lean strength at the footprint center.</summary>
        public float Strength => this.strength;

        /// <summary>Maximum bend angle in degrees.</summary>
        public float MaxBendDegrees => this.maxBendDegrees;

        // Static registry of enabled interactors — the renderer/sim is the single consumer and iterates it.
        private static readonly List<GrassInteractor> active = new();

        /// <summary>All currently-enabled interactors. Read by the grass sim each frame.</summary>
        public static IReadOnlyList<GrassInteractor> Active => active;

        private void OnEnable()
        {
            if (!active.Contains(this)) // idempotent — guard double-add across edit-mode domain reloads
                active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }
    }
}
