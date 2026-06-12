#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — unified SurfaceLayers (grass variants) scatter orchestration.
    ///
    /// Parallels the FROZEN <see cref="RebuildScatter"/> path but iterates the new
    /// <see cref="WorldMapAsset.SurfaceLayers"/>. For each <see cref="GrassLayer"/> it builds N
    /// frozen engines — one per palette variant — via a transient <see cref="GrassVariantScatterLayer"/>
    /// adapter, REUSING the frozen private helpers (<c>SelectAndBuildScatterEngine</c>,
    /// <c>ResolveScatterInfra</c>, <c>BuildScatterSampler</c>) which are accessible because this is the
    /// same partial class.
    ///
    /// Zero edits to <c>WorldPainter.Scatter.cs</c> or any engine file (those stay FROZEN).
    /// </summary>
    public sealed partial class WorldPainter
    {
        // One engine + its adapter per (grass layer, variant). Adapters are kept alive for the
        // lifetime of their engine (the engine retains the layer reference for Submit-time config).
        private readonly List<IGrassEngine>             surfaceEngines  = new();
        private readonly List<GrassVariantScatterLayer> surfaceAdapters = new();

        /// <summary>(Re)builds one frozen engine per grass-variant from <see cref="WorldMapAsset.SurfaceLayers"/>.</summary>
        internal void RebuildSurfaceLayers()
        {
            this.DisposeSurfaceLayers();

            if (this.map == null) return;
            IReadOnlyList<WorldPainterLayer> layers = this.map.SurfaceLayers;
            if (layers.Count == 0) return;

            // Reuse the frozen grounding sampler + GPU infra resolution.
            this.scatterSampler ??= this.BuildScatterSampler();
            this.ResolveScatterInfra();
            this.scatterPool ??= new InstanceBatchPool(this.scatterPrewarmSlabs);

#if UNITY_EDITOR || !UNITY_WEBGL
            bool gpuCapable = GrassTierProbe.TryGpu(out string probeReason);
#else
            bool gpuCapable  = false;
            string probeReason = "WebGL";
#endif

            Vector3 origin = this.transform.position;

            for (int i = 0; i < layers.Count; ++i)
            {
                if (layers[i] is not GrassLayer grass) continue; // splat layers don't scatter

                IReadOnlyList<GrassVariant> palette = grass.Palette;
                for (int v = 0; v < palette.Count; ++v)
                {
                    var adapter = GrassVariantScatterLayer.Create(grass, v, palette[v].densityMap);

                    if (!adapter.Validate(out string err))
                    {
                        Debug.Log($"[WorldPainter.SurfaceLayers] {adapter.name}: {err} — skipped.", this);
                        DestroyAdapter(adapter);
                        continue;
                    }

                    ISurfaceSampler sampler = (ISurfaceSampler?)this.scatterSampler
                        ?? new RaycastSurfaceSampler(adapter.Placement.GroundSnapMask, origin.y);

                    IGrassEngine engine = this.SelectAndBuildScatterEngine(
                        i, adapter, origin, sampler, gpuCapable, probeReason);
                    engine.Build(adapter, origin, this.scatterPool!, sampler);

                    this.surfaceEngines.Add(engine);
                    this.surfaceAdapters.Add(adapter);

                    Debug.Log($"[WorldPainter.SurfaceLayers] {adapter.name} → " +
                              $"tier={this.ScatterActiveTierName}.", this);
                }
            }
        }

        /// <summary>Advances surface-layer scatter simulation.</summary>
        internal void StepSurfaceLayers(float dt)
        {
            for (int i = 0; i < this.surfaceEngines.Count; ++i)
                this.surfaceEngines[i].Step(dt);
        }

        /// <summary>Submits surface-layer scatter draws for this frame.</summary>
        internal void SubmitSurfaceLayers(Camera? targetCamera)
        {
            if (this.surfaceEngines.Count == 0) return;

            Vector3 lodRef = targetCamera != null
                ? targetCamera.transform.position
                : (Camera.main != null ? Camera.main.transform.position : this.transform.position);

            for (int i = 0; i < this.surfaceEngines.Count; ++i)
                this.surfaceEngines[i].Submit(targetCamera, lodRef);
        }

        /// <summary>Disposes all surface-layer engines + their transient adapters.</summary>
        internal void DisposeSurfaceLayers()
        {
            for (int i = 0; i < this.surfaceEngines.Count; ++i)
                this.surfaceEngines[i].Dispose();
            this.surfaceEngines.Clear();

            for (int i = 0; i < this.surfaceAdapters.Count; ++i)
                DestroyAdapter(this.surfaceAdapters[i]);
            this.surfaceAdapters.Clear();
        }

        private static void DestroyAdapter(GrassVariantScatterLayer? adapter)
        {
            if (adapter == null) return;
            if (Application.isPlaying) Destroy(adapter);
            else DestroyImmediate(adapter);
        }
    }
}
