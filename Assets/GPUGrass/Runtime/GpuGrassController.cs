#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GPUGrass
{
    /// <summary>
    /// The single component you drop on a Terrain to render interactive GPUGrass — the facade over the
    /// render tier (<see cref="IGpuGrassRenderer"/>). Holds the config + baked placement, picks the tier,
    /// and drives the per-frame sim/draw in both edit and play modes. Added + wired automatically by
    /// <c>GpuGrassAutoSetup</c> (no manual setup).
    ///
    /// Driver discipline (mirrors the proven WorldPainter path):
    /// • PLAY  → LateUpdate: Step + Submit(camera = null) so grass renders in every camera.
    /// • EDIT  → Step once per frame (EditorApplication.update) + per-camera Submit from
    ///   beginCameraRendering, so the material constant buffer is bound (no black/garbage in Scene view).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GPUGrass/GPU Grass Controller")]
    public sealed class GpuGrassController : MonoBehaviour
    {
        [SerializeField] private Terrain? terrain;
        [SerializeField] private GpuGrassConfig? config;
        [SerializeField] private GpuGrassBakeData? bake;

        private IGpuGrassRenderer? engine;
        private readonly GpuGrassDensityGovernor densityGovernor = new();

        /// <summary>Current adaptive-density fraction (0..1; 1 = full) for inspector/diagnostics.</summary>
        public float CurrentDensity => this.densityGovernor.Density;

        public Terrain? Terrain { get => this.terrain; set => this.terrain = value; }
        public GpuGrassConfig? Config { get => this.config; set => this.config = value; }
        public GpuGrassBakeData? Bake { get => this.bake; set => this.bake = value; }

        /// <summary>
        /// Pass 2 assigns a factory that constructs the correct render tier (GPU indirect / CPU instanced)
        /// from <see cref="GpuGrassTierProbe"/> + <see cref="GpuGrassConfig.TierMode"/>. Until then
        /// <see cref="Rebuild"/> no-ops, so this foundation compiles + runs without the render core.
        /// </summary>
        public static System.Func<GpuGrassConfig, IGpuGrassRenderer?>? RendererFactory;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += this.OnBeginCameraRendering;
#if UNITY_EDITOR
            EditorApplication.update += this.EditorStep;
#endif
            this.Rebuild();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= this.OnBeginCameraRendering;
#if UNITY_EDITOR
            EditorApplication.update -= this.EditorStep;
#endif
            this.engine?.Dispose();
            this.engine = null;
        }

        /// <summary>The tier resolved on the last <see cref="Rebuild"/> (for inspector/diagnostics).</summary>
        public GrassDeviceTier ResolvedTier { get; private set; } = GrassDeviceTier.Disabled;

        /// <summary>
        /// Rebuilds for the current device tier. Policy: GPU → interactive GPUGrass (terrain grass off);
        /// TerrainFallback → no GPUGrass, Unity's built-in terrain grass on; Disabled → no grass.
        /// Safe to call anytime.
        /// </summary>
        public void Rebuild()
        {
            this.engine?.Dispose();
            this.engine = null;

            if (this.config == null)
                return;

            this.ResolvedTier = this.ResolveTier();
            this.ApplyTerrainDetailForTier(this.ResolvedTier);

            // Only the GPU tier runs the GPUGrass renderer; the other tiers render via the terrain (or not).
            if (this.ResolvedTier != GrassDeviceTier.Gpu)
                return;
            if (this.bake == null || this.bake.InstanceCount == 0)
                return;
            if (RendererFactory == null)
                return; // render core not wired (bootstrap missing)

            IGpuGrassRenderer? built = RendererFactory(this.config);
            if (built == null)
                return; // factory could not resolve render assets (e.g. missing compute/material)

            this.engine = built;
            this.engine.Build(this.config, this.bake);
            this.densityGovernor.Reset(); // start each (re)build at full density + fresh frame-time signal
        }

        private GrassDeviceTier ResolveTier()
        {
            if (this.config == null)
                return GrassDeviceTier.Disabled;

            return this.config.TierMode switch
            {
                GrassTierMode.ForceGpu              => GrassDeviceTier.Gpu,
                GrassTierMode.ForceTerrainFallback  => GrassDeviceTier.TerrainFallback,
                GrassTierMode.ForceDisabled         => GrassDeviceTier.Disabled,
                _ => GpuGrassTierProbe.ClassifyAuto(
                        this.config.EnableTerrainFallback, this.config.LowEndMemoryThresholdMB),
            };
        }

        /// <summary>
        /// Drives the terrain's own detail (grass) rendering to match the tier: ON only for the
        /// TerrainFallback tier, OFF otherwise. Applied at RUNTIME only — in edit mode the auto-setup owns
        /// <c>detailObjectDistance</c> (editor is always GPU-capable, so it stays off there).
        /// </summary>
        private void ApplyTerrainDetailForTier(GrassDeviceTier tier)
        {
            if (!Application.isPlaying || this.terrain == null || this.config == null)
                return;

            this.terrain.detailObjectDistance = tier == GrassDeviceTier.TerrainFallback
                ? this.config.TerrainFallbackDetailDistance
                : 0f;
        }

        private Vector3 LodReference =>
            Camera.main != null ? Camera.main.transform.position : this.transform.position;

        // PLAY: drive sim + draw from the player loop; null camera = render in all cameras.
        private void LateUpdate()
        {
            if (!Application.isPlaying || this.engine == null)
                return;

            this.engine.Step(Time.deltaTime);

            // Adaptive density: thin grass under load, restore under headroom (mobile safety valve).
            // Play-mode only — edit-mode frame time is dominated by editor overhead, not the grass.
            if (this.config != null && this.config.EnableAdaptiveDensity)
            {
                float density = this.densityGovernor.Tick(
                    Time.unscaledDeltaTime, this.config.AdaptiveTargetFps, this.config.MinDensity);
                this.engine.SetDensity(density);
            }

            this.engine.Submit(null, this.LodReference);
        }

#if UNITY_EDITOR
        // EDIT: advance the sim once per frame; the per-camera draw happens in beginCameraRendering.
        private void EditorStep()
        {
            if (Application.isPlaying || this.engine == null)
                return;

            this.engine.Step(Time.unscaledDeltaTime);
        }
#endif

        // EDIT: per-camera submit so the material cbuffer is bound (Scene + Game view render correctly).
        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (Application.isPlaying || this.engine == null)
                return;

            this.engine.Submit(cam, this.LodReference);
        }
    }
}
