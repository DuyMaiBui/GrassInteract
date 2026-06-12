#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — scatter orchestration (P2).
    ///
    /// Absorbs ScatterField's Rebuild/StepAll/SubmitAll/engine-selection orchestration
    /// into <see cref="RebuildScatter"/>, <see cref="StepScatter"/>, <see cref="SubmitScatter"/>.
    ///
    /// Reads scatter layers from the referenced <see cref="WorldMapAsset"/> (<c>this.map</c>).
    /// Builds a <see cref="HeightmapSurfaceSampler"/> internally from the first valid tile so
    /// no external bridge component (<c>GpuTerrainScatterGround</c>) is needed.
    ///
    /// Engine files (GrassGpuEngine, GrassCpuEngine, InstancedPropEngine, InstanceBatchPool,
    /// IGrassEngine, IScatterPlacement) are UNTOUCHED — the ISurfaceSampler seam is identical.
    ///
    /// FROZEN after P2 — later phases add new partials, never touch this file.
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

        // Pool is field-owned; lifetime spans multiple RebuildScatter calls (slab reuse).
        private InstanceBatchPool? scatterPool;

        // One engine per layer slot (null = InstanceScatterLayer or failed build).
        private readonly List<IGrassEngine?> scatterEngines = new();

        // Heightmap sampler built from first valid resident tile.
        private HeightmapSurfaceSampler? scatterSampler;

        /// <summary>Name of the last selected scatter tier (diagnostics only).</summary>
        public string ScatterActiveTierName { get; private set; } = string.Empty;

        // ── RebuildScatter ────────────────────────────────────────────────────

        /// <summary>
        /// (Re)builds all scatter layer engines from <see cref="WorldMapAsset"/> layers.
        /// Internalises the HeightmapSurfaceSampler grounding from the first resident tile.
        /// Safe to call repeatedly.
        /// </summary>
        internal void RebuildScatter()
        {
            this.DisposeScatterEngines();

            if (this.map == null) return;

            IReadOnlyList<ScatterLayer> layers = this.map.Layers;
            if (layers.Count == 0) return;

            // Build the heightmap sampler from the first valid tile in the map.
            this.scatterSampler = this.BuildScatterSampler();

            // Resolve GPU resources (may be null → CPU fallback).
            this.ResolveScatterInfra();

            this.scatterPool ??= new InstanceBatchPool(this.scatterPrewarmSlabs);

            // Tier probe — once per rebuild, shared across all Grass-kind layers.
#if UNITY_EDITOR || !UNITY_WEBGL
            bool gpuCapable = GrassTierProbe.TryGpu(out string probeReason);
#else
            bool gpuCapable  = false;
            string probeReason = "WebGL";
#endif

            string lastTierName = string.Empty;

            for (int i = 0; i < layers.Count; ++i)
            {
                ScatterLayer? layer = layers[i];
                if (layer == null)
                {
                    Debug.LogWarning($"[WorldPainter.Scatter] Layer [{i}] is null — skipping.", this);
                    this.scatterEngines.Add(null);
                    continue;
                }

                Vector3 origin = this.transform.position;
                ISurfaceSampler sampler = (ISurfaceSampler?)this.scatterSampler
                    ?? new RaycastSurfaceSampler(
                        layer.Placement.GroundSnapMask, this.transform.position.y);

                // InstanceScatterLayer → InstancedPropEngine route.
                if (layer is InstanceScatterLayer instanceLayer)
                {
                    IGrassEngine? propEngine = this.TryBuildScatterInstancedPropEngine(
                        i, instanceLayer, origin, sampler);
                    this.scatterEngines.Add(propEngine);
                    if (propEngine != null)
                        Debug.Log(
                            $"[WorldPainter.Scatter] Layer [{i}] '{instanceLayer.name}' → InstancedPropEngine.",
                            this);
                    continue;
                }

                // Density / grass pipeline.
                if (!layer.Validate(out string validationError))
                {
                    Debug.LogError(
                        $"[WorldPainter.Scatter] Layer [{i}] '{layer.name}' invalid: {validationError}",
                        this);
                    this.scatterEngines.Add(null);
                    continue;
                }

                IGrassEngine engine = this.SelectAndBuildScatterEngine(
                    i, layer, origin, sampler, gpuCapable, probeReason);
                engine.Build(layer, origin, this.scatterPool, sampler);
                this.scatterEngines.Add(engine);
                lastTierName = this.ScatterActiveTierName;

                Debug.Log(
                    $"[WorldPainter.Scatter] Layer [{i}] '{layer.name}' tier={this.ScatterActiveTierName} " +
                    $"(forceTier={this.scatterForceTier}) on {SystemInfo.graphicsDeviceName}",
                    this);
            }

            if (lastTierName.Length > 0)
                this.ScatterActiveTierName = lastTierName;
        }

        // ── StepScatter ───────────────────────────────────────────────────────

        /// <summary>Advances scatter simulation by <paramref name="dt"/> seconds.</summary>
        internal void StepScatter(float dt)
        {
            for (int i = 0; i < this.scatterEngines.Count; ++i)
                this.scatterEngines[i]?.Step(dt);
        }

        // ── SubmitScatter ─────────────────────────────────────────────────────

        /// <summary>Submits scatter draw calls for this frame.</summary>
        internal void SubmitScatter(Camera? targetCamera)
        {
            if (this.scatterEngines.Count == 0) return;

            Vector3 lodRef;
            if (targetCamera != null)
            {
                lodRef = targetCamera.transform.position;
            }
            else
            {
                Camera? main = Camera.main;
                lodRef = main != null ? main.transform.position : this.transform.position;
            }

            for (int i = 0; i < this.scatterEngines.Count; ++i)
                this.scatterEngines[i]?.Submit(targetCamera, lodRef);
        }

        // ── DisposeScatterEngines ─────────────────────────────────────────────

        private void DisposeScatterEngines()
        {
            for (int i = 0; i < this.scatterEngines.Count; ++i)
                this.scatterEngines[i]?.Dispose();
            this.scatterEngines.Clear();
            this.scatterSampler = null;
        }

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
                this.scatterIndirectMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/WorldPainter/Materials/IndirectGrass.mat");
#endif
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
                        this.scatterCullCompute, this.scatterIndirectMat);

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
                        this.scatterCullCompute, this.scatterIndirectMat);
            }
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

        private IGrassEngine? TryBuildScatterInstancedPropEngine(
            int layerIndex,
            InstanceScatterLayer layer,
            Vector3 origin,
            ISurfaceSampler sampler)
        {
            if (this.scatterCullCompute == null)
            {
                Debug.LogError(
                    $"[WorldPainter.Scatter] Layer [{layerIndex}] '{layer.name}' (instanced-prop): " +
                    "scatterCullCompute is not assigned. Skipping.", this);
                return null;
            }

            if (!layer.Validate(out string error))
            {
                Debug.LogError(
                    $"[WorldPainter.Scatter] Layer [{layerIndex}] '{layer.name}' (instanced-prop) invalid: {error}",
                    this);
                return null;
            }

            if (layer.Render.LodMeshes.Length == 0)
            {
                Debug.LogError(
                    $"[WorldPainter.Scatter] Layer [{layerIndex}] '{layer.name}' (instanced-prop): " +
                    "LodMeshes is empty.", this);
                return null;
            }

            Material? mat = layer.Render.Material;
            if (mat == null)
            {
                Debug.LogError(
                    $"[WorldPainter.Scatter] Layer [{layerIndex}] '{layer.name}' (instanced-prop): " +
                    "Material is not assigned.", this);
                return null;
            }

            try
            {
                var engine = new InstancedPropEngine(this.scatterCullCompute, mat);
                engine.Build(layer, origin, this.scatterPool!, sampler);
                return engine;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[WorldPainter.Scatter] Layer [{layerIndex}] '{layer.name}' (instanced-prop) " +
                    $"engine threw {ex.GetType().Name}: {ex.Message}", this);
                return null;
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
            public void Dispose() => this.inner.Dispose();
        }
    }
}
