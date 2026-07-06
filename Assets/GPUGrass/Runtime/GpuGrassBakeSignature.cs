#nullable enable
using System;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// A value snapshot of exactly the <see cref="GpuGrassConfig"/> fields that the editor bake
    /// (<c>GpuGrassBaker</c>) reads to place blades. Stored inside <see cref="GpuGrassBakeData"/> at bake
    /// time so the editor can tell whether the config's PLACEMENT tunables have diverged from the baked
    /// data. Render-only tunables (LOD, wind, bend, shadows, material, tier, occlusion) are deliberately
    /// EXCLUDED — changing them must never mark a bake stale, since they need no re-bake.
    ///
    /// Compared by exact value equality: revert a field to its baked value and the signature matches
    /// again. Keep the field list in lock-step with what <c>GpuGrassBaker.Bake</c> actually reads.
    /// </summary>
    [Serializable]
    public struct GpuGrassBakeSignature : IEquatable<GpuGrassBakeSignature>
    {
        [SerializeField] private int     placementSource;
        [SerializeField] private float   targetDensityPerSqM;
        [SerializeField] private Vector2 scaleRange;
        [SerializeField] private Vector2 bladeHeightRange;
        [SerializeField] private Vector2 slopeRange;
        [SerializeField] private Vector2 heightRange01;
        [SerializeField] private int     seed;

        /// <summary>Snapshots the bake-affecting fields of <paramref name="config"/> (null → default).</summary>
        public static GpuGrassBakeSignature From(GpuGrassConfig config)
        {
            if (config == null) return default;
            return new GpuGrassBakeSignature
            {
                placementSource     = (int)config.PlacementSource,
                targetDensityPerSqM = config.TargetDensityPerSqM,
                scaleRange          = config.ScaleRange,
                bladeHeightRange    = config.BladeHeightRange,
                slopeRange          = config.SlopeRange,
                heightRange01       = config.HeightRange01,
                seed                = config.Seed,
            };
        }

        public bool Equals(GpuGrassBakeSignature o) =>
            this.placementSource     == o.placementSource &&
            this.targetDensityPerSqM == o.targetDensityPerSqM &&
            this.scaleRange          == o.scaleRange &&
            this.bladeHeightRange    == o.bladeHeightRange &&
            this.slopeRange          == o.slopeRange &&
            this.heightRange01       == o.heightRange01 &&
            this.seed                == o.seed;

        public override bool Equals(object? obj) => obj is GpuGrassBakeSignature o && this.Equals(o);

        public override int GetHashCode() => HashCode.Combine(
            this.placementSource, this.targetDensityPerSqM, this.scaleRange,
            this.bladeHeightRange, this.slopeRange, this.heightRange01, this.seed);

        public static bool operator ==(GpuGrassBakeSignature a, GpuGrassBakeSignature b) => a.Equals(b);
        public static bool operator !=(GpuGrassBakeSignature a, GpuGrassBakeSignature b) => !a.Equals(b);
    }
}
