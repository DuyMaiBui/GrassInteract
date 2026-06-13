#nullable enable
using Unity.Collections;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Contract for authored-instance placement data. <see cref="InstancePlacement"/> consumes
    /// this interface so any layer type can reuse the authored placement strategy by implementing it.
    /// </summary>
    public interface IInstancePlacementSource
    {
        AuthoredInstancesData? AuthoredInstances { get; }

        // ── Bounds (for AABB computation) ─────────────────────────────────────
        Vector2 FieldBounds { get; }
        Vector2 ScaleRange { get; }
        float MaxBladeHeight { get; }
        float BendHeadroom { get; }
    }
}
