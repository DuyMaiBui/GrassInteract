#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// The existing CPU grass path behind the <see cref="IGrassEngine"/> seam.
    /// Wraps <see cref="GrassBendSimulator"/> + <see cref="GrassRenderer"/> verbatim — zero behavior change
    /// vs the pre-seam <c>ScatterField</c>. This is the verified low tier; Phase 2+ adds the high tier.
    ///
    /// Ownership: the facade owns <see cref="InstanceBatchPool"/>; this engine receives it in
    /// <see cref="Build"/> and returns the scatter slabs to it in <see cref="Dispose"/>.
    /// </summary>
    internal sealed class GrassCpuEngine : IGrassEngine
    {
        private GrassScatterResult? scatter;
        private GrassRenderer? renderer;
        private GrassBendSimulator? simulator;

        // Pool reference held for Dispose — the facade owns the pool's lifetime.
        private InstanceBatchPool? pool;

        // ------------------------------------------------------------------ IGrassEngine

        /// <inheritdoc/>
        public void Build(ScatterLayer layer, Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)
        {
            this.Dispose(); // return any previously held slabs before re-building

            this.pool = pool;
            this.scatter = GrassScatter.Build(layer, origin, pool, sampler);
            // All render/wind/bounds params are read directly from the layer.
            this.renderer = new GrassRenderer(layer, origin);
            this.simulator = new GrassBendSimulator(this.scatter, layer);
        }

        /// <inheritdoc/>
        public void Step(float dt)
        {
            this.simulator?.Step(dt);
        }

        /// <inheritdoc/>
        public void Submit(Camera? targetCamera, Vector3 lodReferencePos)
        {
            if (this.scatter == null || this.renderer == null || this.simulator == null)
                return;

            this.renderer.Render(lodReferencePos, this.simulator.RenderSlabs, this.simulator.SlabCounts,
                this.scatter.WorldBounds, targetCamera);
        }

        /// <inheritdoc/>
        public Bounds WorldBounds => this.scatter?.WorldBounds ?? default;

        // ── IGrassEngine : SetScaleFactor ─────────────────────────────────────

        private bool warnedScaleFactor;

        /// <inheritdoc/>
        /// CPU engine does not support render-time scale factor. Logs a one-time warning.
        public void SetScaleFactor(float factor)
        {
            if (this.warnedScaleFactor) return;
            this.warnedScaleFactor = true;
            Debug.LogWarning("[GrassCpuEngine] scaleFactor is a GPU render feature; the CPU fallback ignores it.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (this.scatter != null && this.pool != null)
                GrassScatter.ReturnSlabs(this.scatter, this.pool);

            this.scatter = null;
            this.renderer = null;
            this.simulator = null;
            // Do NOT null out this.pool — it is facade-owned; we just stop using it.
        }
    }
}
