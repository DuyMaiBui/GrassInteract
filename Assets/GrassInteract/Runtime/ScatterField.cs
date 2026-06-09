#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Genre-neutral scene orchestrator for a multi-layer scatter field. Drives all resources from
    /// a <see cref="TerrainScatterConfig"/> asset; builds one <see cref="IGrassEngine"/> per
    /// Grass-kind layer and drives their Step/Submit calls from the player loop.
    ///
    /// <see cref="SeedLayers"/> is a virtual extension hook (base no-op) that subclasses may override
    /// to inject layers before <see cref="Rebuild"/> iterates the list.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ScatterField : MonoBehaviour
    {
        // ── Tier selection mode enum ──────────────────────────────────────────

        /// <summary>
        /// Controls which rendering engine tier is used for Grass-kind layers.
        /// </summary>
        public enum GrassTierMode
        {
            /// <summary>
            /// Let <see cref="GrassTierProbe.TryGpu"/> decide: GPU when the device supports it and
            /// <c>cullCompute</c> + <c>indirectMaterial</c> are assigned; CPU otherwise.
            /// </summary>
            Auto,

            /// <summary>Always use the CPU tier (safe fallback; works on all devices).</summary>
            ForceCpu,

            /// <summary>
            /// Always use the GPU indirect tier. Only valid when <c>cullCompute</c> +
            /// <c>indirectMaterial</c> are both assigned; logs an error and falls back to CPU otherwise.
            /// Development / QA override only — do not ship with this value.
            /// </summary>
            ForceGpu,
        }

        // ── Serialized fields ─────────────────────────────────────────────────

        [Tooltip("Assign a TerrainScatterConfig — required.")]
        [SerializeField] private TerrainScatterConfig? config;

        [Min(0)]
        [Tooltip("Instancing slabs to pre-allocate at build. 0 = lazy.")]
        [SerializeField] private int prewarmSlabs = 0;

        [Tooltip(
            "Auto: use GrassTierProbe to select the best tier for this device.\n" +
            "ForceCpu: always use the CPU renderer.\n" +
            "ForceGpu: always use GPU indirect (development override only).")]
        [SerializeField] private GrassTierMode forceTier = GrassTierMode.Auto;

        [Tooltip("Extra metres of per-blade cull headroom beyond the automatic blade reach. GPU tier only.")]
        [SerializeField] private float extraCullMargin = 0f;

        [Tooltip("Optional Unity Terrain: samples height, holes, and slope from TerrainData and centers " +
                 "the field on the terrain. Leave null to use the legacy Physics.Raycast path.")]
        [SerializeField] private Terrain? boundTerrain;

        // ── Runtime state ─────────────────────────────────────────────────────

        // Pool is field-owned; its lifetime spans multiple engine rebuilds so slab reuse stays intact.
        private InstanceBatchPool? pool;

        // One engine per layer (parallel list; null entry = Mesh-kind or failed-build layer).
        private readonly List<IGrassEngine?> engines = new();

        // ── Public properties ─────────────────────────────────────────────────

        /// <summary>Name of the most recently selected tier across all built layers. Empty before first build.</summary>
        public string ActiveTierName { get; private set; } = string.Empty;

        /// <summary>The assigned TerrainScatterConfig asset. Null = no config (Required).</summary>
        public TerrainScatterConfig? Config => this.config;

        /// <summary>
        /// Read-only view of the active layers list.
        /// Returns Config.Layers when Config is assigned; otherwise empty.
        /// </summary>
        public IReadOnlyList<ScatterLayer> Layers =>
            this.config != null ? this.config.Layers : System.Array.Empty<ScatterLayer>();

        /// <summary>
        /// Read-only view of each engine's WorldBounds (parallel to Layers).
        /// Used by the parity harness and editor gizmos.
        /// </summary>
        public IReadOnlyList<Bounds> EngineWorldBounds
        {
            get
            {
                var result = new Bounds[this.engines.Count];
                for (int i = 0; i < this.engines.Count; ++i)
                    result[i] = this.engines[i]?.WorldBounds ?? default;
                return result;
            }
        }

        // ── Serialized terrain reference for subclasses ───────────────────────

        /// <summary>The bound terrain (if any). Readable by subclasses and harnesses.</summary>
        protected Terrain? BoundTerrain => this.boundTerrain;

        // ── Editor field-space resolution (mirrors BuildContext; consumed by editor tools) ──

        /// <summary>Resolved field origin: terrain center when a terrain is bound, else transform position.</summary>
        internal Vector3 ResolveFieldOrigin()
        {
            if (this.boundTerrain != null && this.boundTerrain.terrainData != null)
            {
                Vector3 size = this.boundTerrain.terrainData.size;
                Vector3 pos  = this.boundTerrain.transform.position;
                return pos + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            }
            return this.transform.position;
        }

        /// <summary>Resolved field XZ bounds: terrain size when bound, else the layer's FieldBounds.</summary>
        internal Vector2 ResolveFieldBoundsXZ(ScatterLayer? layer)
        {
            if (this.boundTerrain != null && this.boundTerrain.terrainData != null)
            {
                Vector3 size = this.boundTerrain.terrainData.size;
                return new Vector2(size.x, size.z);
            }
            return layer != null ? layer.FieldBounds : new Vector2(100f, 100f);
        }

        /// <summary>Ground raycast mask for a layer's placement (used by editor painting/placement).</summary>
        internal LayerMask ResolveGroundMask(ScatterLayer layer) => layer.Placement.GroundSnapMask;

        // ── MonoBehaviour lifecycle ───────────────────────────────────────────

        private void OnEnable()
        {
            this.Rebuild();
        }

        private void OnDisable()
        {
        }

        private void OnDestroy()
        {
            this.DisposeAllEngines();
            this.pool?.Clear();
        }


        // ── Shared context builder ────────────────────────────────────────────

        /// <summary>
        /// Captures all field-level build inputs (layers, GPU resources, sampler, origin, tier probe)
        /// into a lightweight struct. Both <see cref="Rebuild"/> and <see cref="RebuildLayer"/> use
        /// this single source so tier-selection logic is never duplicated.
        /// </summary>
        private struct FieldBuildContext
        {
            public IReadOnlyList<ScatterLayer> Layers;
            public ComputeShader? CullCompute;
            public Material? IndirectMaterial;
            public ISurfaceSampler Sampler;
            public Vector3 Origin;
            public bool GpuCapable;
            public string ProbeReason;
        }

        private FieldBuildContext BuildContext()
        {
            var ctx = new FieldBuildContext
            {
                Layers = this.config!.Layers,
                CullCompute = this.config.CullCompute,
                IndirectMaterial = this.config.IndirectMaterial,
            };

            // Sampler + origin (field-level, shared by all layers).
            if (this.boundTerrain != null && this.boundTerrain.terrainData != null)
            {
                ctx.Sampler = new TerrainSurfaceSampler(this.boundTerrain);
                Vector3 terrainSize = this.boundTerrain.terrainData.size;
                Vector3 terrainPos  = this.boundTerrain.transform.position;
                ctx.Origin = terrainPos + new Vector3(terrainSize.x * 0.5f, 0f, terrainSize.z * 0.5f);
            }
            else
            {
                LayerMask mask = ctx.Layers.Count > 0 && ctx.Layers[0] != null
                    ? ctx.Layers[0].Placement.GroundSnapMask
                    : ~0;
                ctx.Sampler = new RaycastSurfaceSampler(mask, this.transform.position.y);
                ctx.Origin  = this.transform.position;
            }

            // Tier probe — once per build context, shared by all Grass-kind layers.
#if UNITY_EDITOR || !UNITY_WEBGL
            ctx.GpuCapable = GrassTierProbe.TryGpu(out ctx.ProbeReason);
#else
            ctx.GpuCapable  = false;
            ctx.ProbeReason = "WebGL";
#endif
            return ctx;
        }

        // ── Engine build ──────────────────────────────────────────────────────

        /// <summary>
        /// Hook called at the very start of <see cref="Rebuild"/> before any engine is built.
        /// Subclasses may override this to inject layers before the list is iterated.
        /// Base implementation is a no-op.
        /// </summary>
        protected virtual void SeedLayers() { }

        /// <summary>(Re)builds all layer engines. Safe to call repeatedly.</summary>
        public void Rebuild()
        {
            try
            {
                // Let subclasses inject legacy layers before we iterate.
                this.SeedLayers();

                if (this.config == null)
                {
                    Debug.LogError($"[{nameof(ScatterField)}] No TerrainScatterConfig assigned.", this);
                    return;
                }

                var ctx = this.BuildContext();

                if (ctx.Layers.Count == 0)
                {
                    Debug.LogWarning($"[{nameof(ScatterField)}] No layers assigned.", this);
                    return;
                }

                this.pool ??= new InstanceBatchPool(this.prewarmSlabs);

                this.DisposeAllEngines();
                this.engines.Clear();

                // Tier probe log is per-layer in SelectAndBuildEngine; capture lastTierName for field.
                string lastTierName = string.Empty;

                for (int i = 0; i < ctx.Layers.Count; ++i)
                {
                    ScatterLayer? layer = ctx.Layers[i];
                    if (layer == null)
                    {
                        Debug.LogWarning($"[{nameof(ScatterField)}] Layer [{i}] is null — skipping.", this);
                        this.engines.Add(null);
                        continue;
                    }

                    // Engine route: InstanceScatterLayer → InstancedPropEngine (instanced-prop pipeline).
                    if (layer is InstanceScatterLayer instanceLayer)
                    {
                        IGrassEngine? propEngine = this.TryBuildInstancedPropEngine(
                            i, instanceLayer, ctx.Origin, ctx.Sampler, ctx.CullCompute);
                        this.engines.Add(propEngine);
                        if (propEngine != null)
                            Debug.Log($"[{nameof(ScatterField)}] Layer [{i}] '{instanceLayer.name}' is InstanceScatterLayer → InstancedPropEngine.", this);
                        continue;
                    }

                    // Density/grass pipeline — falls through to tier selection (SelectAndBuildEngine).
                    if (!layer.Validate(out string error))
                    {
                        Debug.LogError($"[{nameof(ScatterField)}] Layer [{i}] '{layer.name}' invalid: {error}", this);
                        this.engines.Add(null);
                        continue;
                    }

                    IGrassEngine engine = this.SelectAndBuildEngine(
                        i, layer, ctx.Origin, ctx.Sampler, ctx.GpuCapable, ctx.ProbeReason,
                        ctx.CullCompute, ctx.IndirectMaterial);
                    engine.Build(layer, ctx.Origin, this.pool, ctx.Sampler);
                    this.engines.Add(engine);
                    lastTierName = this.ActiveTierName;

                    Debug.Log($"[{nameof(ScatterField)}] Layer [{i}] '{layer.name}' tier={this.ActiveTierName} " +
                              $"(forceTier={this.forceTier}) on {SystemInfo.graphicsDeviceName}", this);
                }

                if (lastTierName.Length > 0)
                    this.ActiveTierName = lastTierName;

                WarnIfMultipleEnabledFields();
            }
            finally
            {
            }
        }

        /// <summary>
        /// Disposes and rebuilds the engine at index <paramref name="idx"/> without touching
        /// other slots. Faster than a full <see cref="Rebuild"/> for per-layer inspector edits.
        /// </summary>
        public void RebuildLayer(int idx)
        {
            try
            {
                if (this.config == null || idx < 0 || idx >= this.config.Layers.Count) return;

                var ctx = this.BuildContext();

                // Ensure the engines list is at least as long as idx+1 (pad with nulls).
                while (this.engines.Count <= idx) this.engines.Add(null);

                // Dispose the existing engine at this slot only.
                // Do NOT call pool.Clear() — the pool is field-owned and spans rebuilds.
                this.engines[idx]?.Dispose();
                this.engines[idx] = null;

                this.pool ??= new InstanceBatchPool(this.prewarmSlabs);

                ScatterLayer? layer = ctx.Layers[idx];
                if (layer == null) return;

                // Engine route: InstanceScatterLayer → InstancedPropEngine.
                if (layer is InstanceScatterLayer instanceLayer)
                {
                    this.engines[idx] = this.TryBuildInstancedPropEngine(idx, instanceLayer, ctx.Origin, ctx.Sampler, ctx.CullCompute);
                    if (this.engines[idx] != null)
                        Debug.Log($"[{nameof(ScatterField)}] RebuildLayer [{idx}] '{instanceLayer.name}' is InstanceScatterLayer → InstancedPropEngine.", this);
                    return;
                }

                // Grass pipeline.
                if (!layer.Validate(out string error))
                {
                    Debug.LogError($"[{nameof(ScatterField)}] RebuildLayer [{idx}] '{layer.name}' invalid: {error}", this);
                    return;
                }

                IGrassEngine engine = this.SelectAndBuildEngine(
                    idx, layer, ctx.Origin, ctx.Sampler, ctx.GpuCapable, ctx.ProbeReason,
                    ctx.CullCompute, ctx.IndirectMaterial);
                engine.Build(layer, ctx.Origin, this.pool, ctx.Sampler);
                this.engines[idx] = engine;

                Debug.Log($"[{nameof(ScatterField)}] RebuildLayer [{idx}] '{layer.name}' tier={this.ActiveTierName} " +
                          $"(forceTier={this.forceTier}) on {SystemInfo.graphicsDeviceName}", this);
            }
            finally
            {
            }
        }

        // ── Engine selection ──────────────────────────────────────────────────

        private IGrassEngine SelectAndBuildEngine(
            int layerIndex,
            ScatterLayer layer,
            Vector3 buildOrigin,
            ISurfaceSampler sampler,
            bool gpuCapable,
            string probeReason,
            ComputeShader? activeCullCompute,
            Material? activeIndirectMaterial)
        {
            switch (this.forceTier)
            {
                case GrassTierMode.ForceCpu:
                    this.ActiveTierName = "CPU";
                    Debug.Log($"[{nameof(ScatterField)}] Layer [{layerIndex}] ForceCpu override → CPU tier.", this);
                    return new GrassCpuEngine();

                case GrassTierMode.ForceGpu:
                    if (activeCullCompute == null || activeIndirectMaterial == null)
                    {
                        Debug.LogError(
                            $"[{nameof(ScatterField)}] Layer [{layerIndex}] ForceGpu requested but " +
                            "cullCompute or indirectMaterial is not assigned — falling back to CPU.", this);
                        this.ActiveTierName = "CPU";
                        return new GrassCpuEngine();
                    }
                    return this.TryBuildGpuEngine(layerIndex, $"ForceGpu layer[{layerIndex}]",
                        layer, buildOrigin, sampler, activeCullCompute, activeIndirectMaterial);

                case GrassTierMode.Auto:
                default:
                    Debug.Log($"[{nameof(ScatterField)}] Layer [{layerIndex}] Probe: {probeReason}", this);
                    if (!gpuCapable || activeCullCompute == null || activeIndirectMaterial == null)
                    {
                        if (gpuCapable && (activeCullCompute == null || activeIndirectMaterial == null))
                        {
                            Debug.LogWarning(
                                $"[{nameof(ScatterField)}] Layer [{layerIndex}] Auto: device supports GPU " +
                                "tier but cullCompute or indirectMaterial not assigned — CPU tier.", this);
                        }
                        this.ActiveTierName = "CPU";
                        return new GrassCpuEngine();
                    }
                    return this.TryBuildGpuEngine(layerIndex, $"Auto layer[{layerIndex}]",
                        layer, buildOrigin, sampler, activeCullCompute, activeIndirectMaterial);
            }
        }

        private IGrassEngine TryBuildGpuEngine(
            int layerIndex,
            string source,
            ScatterLayer layer,
            Vector3 buildOrigin,
            ISurfaceSampler sampler,
            ComputeShader activeCullCompute,
            Material activeIndirectMaterial)
        {
            try
            {
                var gpuEngine = new GrassGpuEngine(activeCullCompute, activeIndirectMaterial, this.extraCullMargin);
                gpuEngine.Build(layer, buildOrigin, this.pool!, sampler);

                if (!gpuEngine.SelfTest(out string testReason))
                {
                    Debug.LogWarning(
                        $"[{nameof(ScatterField)}] {source}: {testReason} → GPU self-test failed on " +
                        $"{SystemInfo.graphicsDeviceName} → CPU tier.", this);
                    gpuEngine.Dispose();
                    this.ActiveTierName = "CPU";
                    return new GrassCpuEngine();
                }

                Debug.Log($"[{nameof(ScatterField)}] {source}: {testReason}", this);
                this.ActiveTierName = "GPU";
                return new PreBuiltEngineWrapper(gpuEngine);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[{nameof(ScatterField)}] {source}: GPU engine threw {ex.GetType().Name} on " +
                    $"{SystemInfo.graphicsDeviceName} → CPU tier.\n{ex.Message}", this);
                this.ActiveTierName = "CPU";
                return new GrassCpuEngine();
            }
        }

        // ── Instanced-prop engine builder ─────────────────────────────────────

        /// <summary>
        /// Builds an <see cref="InstancedPropEngine"/> for an <see cref="InstanceScatterLayer"/>.
        /// Returns null (logged) if <paramref name="activeCullCompute"/> is null or the layer's
        /// mesh/material fields are missing.
        /// </summary>
        private IGrassEngine? TryBuildInstancedPropEngine(
            int layerIndex,
            InstanceScatterLayer layer,
            Vector3 buildOrigin,
            ISurfaceSampler sampler,
            ComputeShader? activeCullCompute)
        {
            if (activeCullCompute == null)
            {
                Debug.LogError(
                    $"[{nameof(ScatterField)}] Layer [{layerIndex}] '{layer.name}' (instanced-prop): " +
                    "cullCompute is not assigned. Assign GrassCull.compute in the TerrainScatterConfig. Skipping.", this);
                return null;
            }

            if (!layer.Validate(out string error))
            {
                Debug.LogError(
                    $"[{nameof(ScatterField)}] Layer [{layerIndex}] '{layer.name}' (instanced-prop) invalid: {error}", this);
                return null;
            }

            if (layer.Render.LodMeshes.Length == 0)
            {
                Debug.LogError(
                    $"[{nameof(ScatterField)}] Layer [{layerIndex}] '{layer.name}' (instanced-prop): " +
                    "LodMeshes is empty. Assign at least one LOD mesh in the layer's lods array.", this);
                return null;
            }

            Material? mat = layer.Render.Material;
            if (mat == null)
            {
                Debug.LogError(
                    $"[{nameof(ScatterField)}] Layer [{layerIndex}] '{layer.name}' (instanced-prop): " +
                    "Material is not assigned. Assign a material using the ScatterInstanced shader.", this);
                return null;
            }

            try
            {
                var engine = new InstancedPropEngine(activeCullCompute, mat);
                engine.Build(layer, buildOrigin, this.pool!, sampler);
                return engine;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[{nameof(ScatterField)}] Layer [{layerIndex}] '{layer.name}' (instanced-prop) " +
                    $"engine threw {ex.GetType().Name}: {ex.Message}", this);
                return null;
            }
        }

        // ── Multiple-field warning ────────────────────────────────────────────

        private static void WarnIfMultipleEnabledFields()
        {
            ScatterField[] all = FindObjectsByType<ScatterField>(FindObjectsSortMode.None);
            int enabledCount = 0;
            foreach (ScatterField f in all)
                if (f.isActiveAndEnabled)
                    ++enabledCount;

            if (enabledCount > 1)
                Debug.LogError($"[{nameof(ScatterField)}] {enabledCount} enabled fields found. The bend " +
                    "simulator + interactor registry are per-field — only ONE field per scene is supported.");
        }

        // ── Player-loop / editor-loop drivers ────────────────────────────────

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;
            this.StepAll(Time.deltaTime);
            this.SubmitAll(null);
        }


        // ── Render helpers ────────────────────────────────────────────────────

        internal void StepAll(float dt)
        {
            for (int i = 0; i < this.engines.Count; ++i)
                this.engines[i]?.Step(dt);
        }

        internal void SubmitAll(Camera? targetCamera)
        {
            if (this.engines.Count == 0)
                return;

            Vector3 lodRef;
            if (targetCamera != null)
            {
                lodRef = targetCamera.transform.position;
            }
            else
            {
                Camera main = Camera.main;
                lodRef = main != null ? main.transform.position : this.transform.position;
            }

            for (int i = 0; i < this.engines.Count; ++i)
                this.engines[i]?.Submit(targetCamera, lodRef);
        }

        private void DisposeAllEngines()
        {
            for (int i = 0; i < this.engines.Count; ++i)
                this.engines[i]?.Dispose();
            this.engines.Clear();
        }


        // ── PreBuiltEngineWrapper ─────────────────────────────────────────────

        /// <summary>
        /// Thin wrapper that forwards all <see cref="IGrassEngine"/> calls to an already-built
        /// <see cref="GrassGpuEngine"/> and makes <see cref="Build"/> a no-op (the engine was already
        /// built + self-tested inside <see cref="TryBuildGpuEngine"/>).
        /// </summary>
        private sealed class PreBuiltEngineWrapper : IGrassEngine
        {
            private readonly GrassGpuEngine inner;

            internal PreBuiltEngineWrapper(GrassGpuEngine inner)
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
