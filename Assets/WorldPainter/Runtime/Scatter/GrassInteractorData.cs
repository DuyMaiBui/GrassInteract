#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Shared interactor parameters — the single source of truth for WorldRadius, Strength, and
    /// MaxBendDegrees. Attach this alongside <see cref="GrassInteractor"/> or
    /// <see cref="GrassTrailInteractor"/>; both components read their bend parameters from here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrassInteractorData : MonoBehaviour
    {
        [Min(0f)]
        [Tooltip("Footprint radius in world metres — how wide a patch this interactor leans.")]
        [SerializeField] private float worldRadius = 2f;

        [Range(0f, 1f)]
        [Tooltip("Lean strength at the footprint center (1 = full lean).")]
        [SerializeField] private float strength = 1f;

        [Range(0f, 90f)]
        [Tooltip("Maximum bend angle in degrees.")]
        [SerializeField] private float maxBendDegrees = 90f;

        public float WorldRadius    => this.worldRadius;
        public float Strength       => this.strength;
        public float MaxBendDegrees => this.maxBendDegrees;
    }
}
