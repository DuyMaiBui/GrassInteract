#nullable enable
using Unity.Collections;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Contract for authored-instance placement data. <see cref="InstancePlacement"/> consumes
    /// this interface — not the concrete <see cref="InstanceScatterLayer"/> type — so alternative
    /// layer types can reuse the authored placement strategy by implementing this interface.
    /// </summary>
    public interface IInstancePlacementSource
    {
        AuthoredInstancesData? AuthoredInstances { get; }
        float PlaceSpacing { get; }

        // ── Bounds (for AABB computation) ─────────────────────────────────────
        Vector2 FieldBounds { get; }
        Vector2 ScaleRange { get; }
        float MaxBladeHeight { get; }
        float BendHeadroom { get; }
    }
}
