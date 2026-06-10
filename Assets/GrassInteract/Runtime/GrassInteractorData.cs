#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Shared interactor parameters — the single source of truth for WorldRadius, Strength, and
    /// MaxBendDegrees. Embedded in both <see cref="GrassInteractor"/> and
    /// <see cref="GrassTrailInteractor"/> so the data schema lives in one place.
    /// </summary>
    [System.Serializable]
    public sealed class GrassInteractorData
    {
        [Min(0f)]
        [Tooltip("Footprint radius in world metres — how wide a patch this interactor leans.")]
        public float worldRadius = 2f;

        [Range(0f, 1f)]
        [Tooltip("Lean strength at the footprint center (1 = full lean).")]
        public float strength = 1f;

        [Range(0f, 90f)]
        [Tooltip("Maximum bend angle in degrees.")]
        public float maxBendDegrees = 90f;
    }
}
