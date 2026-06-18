#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — scatter infrastructure (engine selection + GPU resources).
    ///
    /// Provides the shared GPU resource fields, sampler construction, and engine-selection
    /// helpers consumed by <see cref="WorldPainter.SurfaceLayers"/> (the unified SSOT partial).
    ///
    /// Engine files (GrassGpuEngine, GrassCpuEngine, InstancedPropEngine, InstanceBatchPool,
    /// IGrassEngine, IScatterPlacement) are UNTOUCHED — the ISurfaceSampler seam is identical.
    /// </summary>
    public sealed partial class WorldPainter
    {
        // ── Grass tier enum (local copy — ScatterField.GrassTierMode is its deprecated mirror) ─

        /// <summary>
        /// Controls which rendering engine tier is used for Grass-kind scatter layers.
        /// Mirrors <c>ScatterField.GrassTierMode</c> so WorldPainter has no runtime dependency
        /// on ScatterField (which is deletable in P3).
        /// </summary>
        public enum ScatterTierMode
        {
            /// <summary>
            /// Let <see cref="GrassTierProbe.TryGpu"/> decide: GPU when device supports it
            /// and scatter GPU resources are assigned; CPU otherwise.
            /// </summary>
            Auto,

            /// <summary>Always use the CPU tier (safe fallback; works on all devices).</summary>
            ForceCpu,

            /// <summary>
            /// Always use the GPU indirect tier. Only valid when scatterCullCompute +
            /// scatterIndirectMat are both assigned; logs an error and falls back to CPU otherwise.
            /// Development / QA override only — do not ship with this value.
            /// </summary>
            ForceGpu,
        }

        // ── GPU resource refs (required for GPU/instanced-prop engines) ────────

        [HideInInspector] [SerializeField] private ComputeShader? scatterCullCompute  = null;
        [HideInInspector] [SerializeField] private Material?      scatterIndirectMat  = null;

        [SerializeField]
        [Tooltip("Tier override for scatter (Auto = probe device, ForceCpu, ForceGpu).")]
        private ScatterTierMode scatterForceTier = ScatterTierMode.Auto;

        [SerializeField]
        [Min(0)]
        [Tooltip("Instancing slabs to pre-allocate for scatter engines. 0 = lazy.")]
        private int scatterPrewarmSlabs = 0;

        [SerializeField]
        [Tooltip("Extra metres of per-blade cull headroom beyond blade reach (GPU tier only).")]
        private float scatterExtraCullMargin = 0f;

        // ── Runtime scatter state ─────────────────────────────────────────────

        // Pool is field-owned; lifetime spans multiple build calls (slab reuse).
        private InstanceBatchPool? scatterPool;

        // Heightmap sampler built from first valid resident tile.
        private HeightmapSurfaceSampler? scatterSampler;

        /// <summary>
        /// Exposes the map-wide heightmap sampler for editor prop-placement ground-snap.
        /// Null until the first scatter build (or SurfaceLayers rebuild) has run.
        /// </summary>
        internal HeightmapSurfaceSampler? ScatterSampler => this.scatterSampler;

        /// <summary>Name of the last selected scatter tier (diagnostics only).</summary>
        public string ScatterActiveTierName { get; private set; } = string.Empty;

        // ── Sampler construction ──────────────────────────────────────────────

        private HeightmapSurfaceSampler? BuildScatterSampler()
        {
            if (this.map == null) return null;

            foreach (TerrainTileAsset tile in this.map.EnumerateTiles())
            {
                if (tile != null && tile.IsHeightValid)
                    return new HeightmapSurfaceSampler(tile);
            }

            return null;
        }

        // ── GPU infra resolve ─────────────────────────────────────────────────

        private void ResolveScatterInfra()
        {
#if UNITY_EDITOR
            if (this.scatterCullCompute == null)
                this.scatterCullCompute = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Assets/WorldPainter/Shaders/GrassCull.compute");

            if (this.scatterIndirectMat == null)
            {
                // Prefer the authored asset if present (lets users tweak material settings).
                this.scatterIndirectMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/WorldPainter/Materials/IndirectGrass.mat");
            }
#endif
            // Final fallback (editor OR runtime): synthesize a transient material from the shader
            // when no .mat exists. Marked HideAndDontSave so it isn't accidentally serialized
            // into the scene from the [SerializeField] reference (which would dangle on reload).
            if (this.scatterIndirectMat == null)
            {
                var shader = Shader.Find("WorldPainter/IndirectGrass");
                if (shader != null)
                {
                    this.scatterIndirectMat = new Material(shader)
                    {
                        name      = "IndirectGrass (runtime)",
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                }
            }
        }

        // ── Engine selection ──────────────────────────────────────────────────

        private IGrassEngine SelectAndBuildScatterEngine(
            int layerIndex,
            ScatterLayer layer,
            Vector3 origin,
            ISurfaceSampler sampler,
            bool gpuCapable,
            string probeReason)
        {
            switch (this.scatterForceTier)
            {
                case ScatterTierMode.ForceCpu:
                    this.ScatterActiveTierName = "CPU";
                    Debug.Log(
                        $"[WorldPainter.Scatter] Layer [{layerIndex}] ForceCpu override → CPU tier.",
                        this);
                    return new GrassCpuEngine();

                case ScatterTierMode.ForceGpu:
                    if (this.scatterCullCompute == null || this.scatterIndirectMat == null)
                    {
                        Debug.LogError(
                            $"[WorldPainter.Scatter] Layer [{layerIndex}] ForceGpu requested but " +
                            "scatterCullCompute or scatterIndirectMat is not assigned — falling back to CPU.",
                            this);
                        this.ScatterActiveTierName = "CPU";
                        return new GrassCpuEngine();
                    }
                    return this.TryBuildScatterGpuEngine(
                        layerIndex, $"ForceGpu layer[{layerIndex}]",
                        layer, origin, sampler,
                        this.scatterCullCompute, this.ResolveScatterMaterial(layer));

                case ScatterTierMode.Auto:
                default:
                    Debug.Log(
                        $"[WorldPainter.Scatter] Layer [{layerIndex}] Probe: {probeReason}", this);
                    if (!gpuCapable ||
                        this.scatterCullCompute == null ||
                        this.scatterIndirectMat == null)
                    {
                        if (gpuCapable &&
                            (this.scatterCullCompute == null || this.scatterIndirectMat == null))
                        {
                            Debug.LogWarning(
                                $"[WorldPainter.Scatter] Layer [{layerIndex}] Auto: device supports GPU " +
                                "tier but scatterCullCompute or scatterIndirectMat not assigned — CPU tier.",
                                this);
                        }
                        this.ScatterActiveTierName = "CPU";
                        return new GrassCpuEngine();
                    }
                    return this.TryBuildScatterGpuEngine(
                        layerIndex, $"Auto layer[{layerIndex}]",
                        layer, origin, sampler,
                        this.scatterCullCompute, this.ResolveScatterMaterial(layer));
            }
        }

        // Per-layer material when set AND it uses the WorldPainter/IndirectGrass shader (so its
        // properties — _BaseMap/_BaseColor/_TipColor/etc — bind to the same uniforms the GPU engine
        // and shader expect). Falls back to the project-wide scatterIndirectMat otherwise, so layers
        // that leave Material null (or pick an incompatible shader) still render with the default.
        // Without this, the GPU tier always cloned scatterIndirectMat and per-layer styling — base
        // map, gradient colors — was silently ignored (CPU tier already honors layer.Render.Material).
        private Material ResolveScatterMaterial(ScatterLayer layer)
        {
            Material? layerMat = layer.Render.Material;
            if (layerMat != null && layerMat.shader != null &&
                layerMat.shader.name == "WorldPainter/IndirectGrass")
                return layerMat;
            return this.scatterIndirectMat!;
        }

        private IGrassEngine TryBuildScatterGpuEngine(
            int layerIndex,
            string source,
            ScatterLayer layer,
            Vector3 origin,
            ISurfaceSampler sampler,
            ComputeShader cullCompute,
            Material indirectMaterial)
        {
            try
            {
                var gpuEngine = new GrassGpuEngine(
                    cullCompute, indirectMaterial, this.scatterExtraCullMargin);
                gpuEngine.Build(layer, origin, this.scatterPool!, sampler);
                gpuEngine.BindRootSpace(this.RootBinder);

                if (!gpuEngine.SelfTest(out string testReason))
                {
                    Debug.LogWarning(
                        $"[WorldPainter.Scatter] {source}: {testReason} → GPU self-test failed on " +
                        $"{SystemInfo.graphicsDeviceName} → CPU tier.", this);
                    gpuEngine.Dispose();
                    this.ScatterActiveTierName = "CPU";
                    return new GrassCpuEngine();
                }

                Debug.Log($"[WorldPainter.Scatter] {source}: {testReason}", this);
                this.ScatterActiveTierName = "GPU";
                return new ScatterPreBuiltEngineWrapper(gpuEngine);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WorldPainter.Scatter] {source}: GPU engine threw {ex.GetType().Name} on " +
                    $"{SystemInfo.graphicsDeviceName} → CPU tier.\n{ex.Message}", this);
                this.ScatterActiveTierName = "CPU";
                return new GrassCpuEngine();
            }
        }

        // ── PreBuiltEngineWrapper (mirrors ScatterField.PreBuiltEngineWrapper) ─

        /// <summary>
        /// Thin wrapper that forwards IGrassEngine calls to an already-built GrassGpuEngine
        /// and makes Build a no-op (engine was already built + self-tested inside
        /// TryBuildScatterGpuEngine).
        /// </summary>
        private sealed class ScatterPreBuiltEngineWrapper : IGrassEngine
        {
            private readonly GrassGpuEngine inner;

            internal ScatterPreBuiltEngineWrapper(GrassGpuEngine inner)
            {
                this.inner = inner;
            }

            public void Build(ScatterLayer layer, Vector3 origin,
                InstanceBatchPool pool, ISurfaceSampler sampler) { }

            public void Step(float dt) => this.inner.Step(dt);
            public void Submit(Camera? cam, Vector3 lodRef) => this.inner.Submit(cam, lodRef);
            public Bounds WorldBounds => this.inner.WorldBounds;
            public void SetScaleFactor(float factor) => this.inner.SetScaleFactor(factor);
            public void Dispose() => this.inner.Dispose();
        }
    }
}
