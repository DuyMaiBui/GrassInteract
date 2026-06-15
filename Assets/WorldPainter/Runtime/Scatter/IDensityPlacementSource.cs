#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Contract for density-based placement data. <see cref="DensityPlacement"/> consumes this
    /// interface so any layer type can reuse the density placement strategy by implementing it.
    /// </summary>
    public interface IDensityPlacementSource
    {
        // ── Density ───────────────────────────────────────────────────────────
        Texture2D? DensityMap { get; }
        int TargetInstances { get; }

        // ── Bounds override ───────────────────────────────────────────────────
        /// <summary>
        /// When true, <see cref="DensityPlacement"/> uses <see cref="FieldBounds"/> directly
        /// regardless of the active <see cref="ISurfaceSampler"/> type (per-tile adapters set this).
        /// When false (default), terrain-driven bounds from <see cref="TerrainSurfaceSampler"/>
        /// override <see cref="FieldBounds"/> for full-terrain scatter.
        /// </summary>
        bool UseExplicitFieldBounds { get; }

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
