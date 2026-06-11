#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Placement strategy for a ScatterLayer. Two implementations:
    /// <list type="bullet">
    /// <item><see cref="DensityPlacement"/> — procedural RNG scatter over a density map.</item>
    /// <item><see cref="InstancePlacement"/> — feed authored instance records straight into the bake.</item>
    /// </list>
    /// One instance is created per Rebuild call via <c>layer.CreatePlacement()</c>; lifecycle scoped to that Rebuild.
    /// </summary>
    public interface IScatterPlacement
    {
        GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler);
    }
}
