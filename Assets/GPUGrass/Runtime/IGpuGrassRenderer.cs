#nullable enable
using System;
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Seam between <see cref="GpuGrassController"/> (the facade component) and a concrete render tier.
    /// Two implementations land in Pass 2 (extracted + decoupled from WorldPainter):
    /// a GPU indirect tier (compute cull + RenderMeshIndirect) and a CPU instanced tier
    /// (RenderMeshInstanced, target 3.5 — the OpenGL ES 3.0 path). The controller picks one via
    /// <see cref="GpuGrassTierProbe"/> / <see cref="GpuGrassConfig.TierMode"/>.
    /// </summary>
    public interface IGpuGrassRenderer : IDisposable
    {
        /// <summary>(Re)build placement + per-tier GPU/CPU state from the config and baked placement data.</summary>
        void Build(GpuGrassConfig config, GpuGrassBakeData bake);

        /// <summary>Advance per-frame wind/bend state (reads the interactor + trail registries).</summary>
        void Step(float dt);

        /// <summary>
        /// Sets the adaptive density fraction (0..1; 1 = full). Drives the cull compute's per-blade skip,
        /// so thinning is GPU-side and ~free. Called each frame by the controller's load governor; not
        /// calling it leaves the renderer at full density.
        /// </summary>
        void SetDensity(float normalized01);

        /// <summary>
        /// Issue the draw(s). <paramref name="targetCamera"/> null = render in all cameras (play / player
        /// loop); a specific camera = that one only (edit-mode per-camera submit from beginCameraRendering).
        /// </summary>
        void Submit(Camera? targetCamera, Vector3 lodReferencePos);

        /// <summary>Field-wide AABB used for gizmos / bounds queries.</summary>
        Bounds WorldBounds { get; }
    }
}
