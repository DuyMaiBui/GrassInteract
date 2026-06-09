#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Deform (wind + interactor) configuration shared across all scatter layer types.
    /// Composed by concrete layer types rather than inherited.
    /// </summary>
    [System.Serializable]
    public struct ScatterDeformConfig
    {
        [BoxGroup("Deform")]
        [Tooltip("If true, this layer's instances sway with the global wind.")]
        [SerializeField] private bool affectedByWind;

        [BoxGroup("Deform")]
        [Tooltip("If true, this layer's instances lean away from interactors.")]
        [SerializeField] private bool affectedByInteractors;

        [BoxGroup("Trample")]
        [Range(0f, 4f)]
        [Tooltip("Lateral lean amplitude at the blade tip, in metres, at full interactor magnitude.")]
        [SerializeField] private float bendStrength;

        [BoxGroup("Trample")]
        [Range(0f, 1f)]
        [Tooltip("Height loss at full interactor magnitude (0..1).")]
        [SerializeField] private float flatten;

        [BoxGroup("Trample")]
        [Min(0f)]
        [Tooltip("Recovery speed (metres/second of upright restoration).")]
        [SerializeField] private float recoveryRate;

        public ScatterDeformConfig(
            bool affectedByWind,
            bool affectedByInteractors,
            float bendStrength,
            float flatten,
            float recoveryRate)
        {
            this.affectedByWind = affectedByWind;
            this.affectedByInteractors = affectedByInteractors;
            this.bendStrength = bendStrength;
            this.flatten = flatten;
            this.recoveryRate = recoveryRate;
        }

        public bool AffectedByWind => this.affectedByWind;
        public bool AffectedByInteractors => this.affectedByInteractors;
        public bool InteractsWithDeform => this.affectedByWind || this.affectedByInteractors;
        public float BendStrength => this.bendStrength;
        public float Flatten => this.flatten;
        public float RecoveryRate => this.recoveryRate;
    }
}
