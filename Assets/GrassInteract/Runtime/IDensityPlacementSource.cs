#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Contract for density-based placement data. <see cref="DensityPlacement"/> consumes this
    /// interface — not the concrete <see cref="DensityScatterLayer"/> type — so alternative layer
    /// types can reuse the density placement strategy by implementing this interface.
    /// </summary>
    public interface IDensityPlacementSource
    {
        // ── Density ───────────────────────────────────────────────────────────
        Texture2D? DensityMap { get; }
        int TargetInstances { get; }

        // ── Procedural placement ──────────────────────────────────────────────
        Vector2 FieldBounds { get; }
        Vector2 ScaleRange { get; }
        int Seed { get; }
        Vector2 SlopeRange { get; }
        int SplatLayerIndex { get; }
        float SplatThreshold { get; }
        Vector3 RotationOffsetEuler { get; }
        Vector2 RandomPitchRange { get; }
        Vector2 RandomRollRange { get; }
        bool AlignToNormal { get; }

        // ── Bounds (for AABB computation) ─────────────────────────────────────
        float MaxBladeHeight { get; }
        float BendHeadroom { get; }
    }
}
