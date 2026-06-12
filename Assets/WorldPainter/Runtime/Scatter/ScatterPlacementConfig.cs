#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Shared placement configuration for any scatter layer.
    /// Composed by concrete layer types rather than inherited.
    /// </summary>
    [System.Serializable]
    public struct ScatterPlacementConfig
    {
        [Tooltip("Colliders the placement raycast snaps instances onto. Instances with no hit fall back to the field-plane Y.")]
        [SerializeField] private LayerMask groundSnapMask;

        public ScatterPlacementConfig(LayerMask groundSnapMask)
        {
            this.groundSnapMask = groundSnapMask;
        }

        public LayerMask GroundSnapMask => this.groundSnapMask;
    }
}
