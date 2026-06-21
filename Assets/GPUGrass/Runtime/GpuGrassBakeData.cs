#nullable enable
using System;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Editor-baked grass placement generated from a Terrain detail (grass) layer. Decision: editor-bake to
    /// a data asset — no runtime <c>GetDetailLayer</c>/scatter cost on mobile. Produced by the bake step
    /// (Pass 2) and consumed by the renderer at <see cref="GpuGrassController.Rebuild"/>. Re-bake after
    /// repainting the terrain detail layer.
    ///
    /// v1 stores plain parallel arrays (position / yaw / scale). For very large fields a packed binary blob
    /// is a Pass-2 optimization; the public accessors stay the same.
    /// </summary>
    [CreateAssetMenu(menuName = "GPUGrass/Grass Bake Data", fileName = "GpuGrassBakeData")]
    public sealed class GpuGrassBakeData : ScriptableObject
    {
        [SerializeField] private Vector3[] positions = Array.Empty<Vector3>();
        [SerializeField] private float[] yaws = Array.Empty<float>();
        [SerializeField] private float[] scales = Array.Empty<float>();
        [SerializeField] private Bounds worldBounds;
        [SerializeField] private int sourceDetailLayer = -1;
        [SerializeField] private int instanceCount;

        /// <summary>World-space blade root positions.</summary>
        public Vector3[] Positions => this.positions;

        /// <summary>Per-blade yaw in degrees, parallel to <see cref="Positions"/>.</summary>
        public float[] Yaws => this.yaws;

        /// <summary>Per-blade uniform scale, parallel to <see cref="Positions"/>.</summary>
        public float[] Scales => this.scales;

        /// <summary>Field-wide AABB (already includes blade height + bend headroom when baked).</summary>
        public Bounds WorldBounds => this.worldBounds;

        /// <summary>The terrain detail layer index this data was baked from (-1 = unset).</summary>
        public int SourceDetailLayer => this.sourceDetailLayer;

        /// <summary>Total baked blade count.</summary>
        public int InstanceCount => this.instanceCount;

        /// <summary>Replaces all baked data. Called by the bake step (Pass 2).</summary>
        public void SetData(Vector3[] positions, float[] yaws, float[] scales, Bounds bounds, int sourceDetailLayer)
        {
            this.positions = positions ?? Array.Empty<Vector3>();
            this.yaws = yaws ?? Array.Empty<float>();
            this.scales = scales ?? Array.Empty<float>();
            this.worldBounds = bounds;
            this.sourceDetailLayer = sourceDetailLayer;
            this.instanceCount = this.positions.Length;
        }
    }
}
