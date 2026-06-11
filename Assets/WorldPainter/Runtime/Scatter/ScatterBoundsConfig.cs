#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Bounds and GPU-driven configuration shared across all scatter layer types.
    /// Composed by concrete layer types rather than inherited.
    /// </summary>
    [System.Serializable]
    public struct ScatterBoundsConfig
    {
        [BoxGroup("Bounds")]
        [Tooltip("Unscaled height (metres) of the LOD0 blade mesh.")]
        [Min(0.01f)]
        [SerializeField] private float maxBladeHeight;

        [BoxGroup("Bounds")]
        [Tooltip("Extra headroom (metres) added to the field-wide AABB beyond the scaled blade height.")]
        [Min(0f)]
        [SerializeField] private float bendHeadroom;

        [BoxGroup("GPU-Driven")]
        [Min(1)]
        [Tooltip("World-space XZ cell size (metres) for the GPU-driven spatial grid.")]
        [SerializeField] private int chunkSize;

        public ScatterBoundsConfig(float maxBladeHeight, float bendHeadroom, int chunkSize)
        {
            this.maxBladeHeight = maxBladeHeight;
            this.bendHeadroom = bendHeadroom;
            this.chunkSize = chunkSize;
        }

        public float MaxBladeHeight => this.maxBladeHeight;
        public float BendHeadroom => this.bendHeadroom;
        public int ChunkSize => this.chunkSize;
    }
}
