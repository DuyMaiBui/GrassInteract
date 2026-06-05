#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Seam between <see cref="ScatterField"/> (the facade) and a concrete grass rendering engine.
    /// Phase 1 ships one implementation: <see cref="GrassCpuEngine"/> (the verbatim CPU path).
    /// Phase 2 accepts <see cref="ScatterLayer"/> so the
    /// generalized <see cref="ScatterField"/> can pass any Grass-kind layer here without a cast.
    ///
    /// Ownership contract:
    /// - The FACADE owns <see cref="InstanceBatchPool"/> and pre-warm; it passes the pool into
    ///   <see cref="Build"/> so the pool lifetime is never split across the seam.
    /// - The ENGINE owns scatter + per-engine render state (simulator, renderer, GraphicsBuffers, etc.).
    ///   <see cref="Dispose"/> must release all engine-owned resources and return pooled slabs via
    ///   <c>GrassScatter.ReturnSlabs</c>.
    /// - Driver wiring (LateUpdate, EditorApplication.update, beginCameraRendering) stays in the facade
    ///   and calls <see cref="Step"/> / <see cref="Submit"/> at the correct moments.
    /// </summary>
    internal interface IGrassEngine
    {
        /// <summary>
        /// (Re)builds placement and all per-engine state. CPU engine: builds scatter + simulator + renderer
        /// exactly as <c>ScatterField.Rebuild</c> did before the seam was introduced.
        /// All render/wind/bounds parameters are read directly from <paramref name="layer"/>.
        /// </summary>
        /// <param name="layer">Scatter layer (density map, placement rules, render/wind/bounds params).</param>
        /// <param name="origin">World-space field origin (field component's transform.position).</param>
        /// <param name="pool">Facade-owned slab pool. Engine rents slabs from it; Dispose must return them.</param>
        /// <param name="sampler">
        /// Surface sampler for Y-snapping and slope/hole filtering. Either
        /// <see cref="RaycastSurfaceSampler"/> (the legacy fallback) or
        /// <see cref="TerrainSurfaceSampler"/> (when a terrain is bound to the field).
        /// </param>
        void Build(ScatterLayer layer, Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler);

        /// <summary>
        /// Advances per-frame deform state by <paramref name="dt"/> seconds.
        /// CPU engine: <c>GrassBendSimulator.Step(dt)</c>.
        /// GPU engine (future): uploads interactor data / no-op if deform is fully GPU-side.
        /// </summary>
        void Step(float dt);

        /// <summary>
        /// Issues the draw(s) for this frame.
        /// CPU engine: <c>GrassRenderer.Render(...)</c> with the simulator output slabs.
        /// </summary>
        /// <param name="targetCamera">
        /// <c>null</c> = render in all cameras (play-mode player-loop path).
        /// A specific camera = render only in that one (edit-mode per-camera beginCameraRendering path).
        /// </param>
        /// <param name="lodReferencePos">World position used to select the LOD (camera or field origin).</param>
        void Submit(Camera? targetCamera, Vector3 lodReferencePos);

        /// <summary>Field-wide AABB used for gizmos and bounds queries.</summary>
        Bounds WorldBounds { get; }

        /// <summary>
        /// Releases all engine-owned resources. CPU engine: returns pooled base slabs to the facade's pool
        /// via <c>GrassScatter.ReturnSlabs</c>. GPU engine (future): releases GraphicsBuffers.
        /// Safe to call on a never-built or already-disposed engine.
        /// </summary>
        void Dispose();
    }
}
