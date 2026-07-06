#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPUGrass
{
    /// <summary>Device-tier selection mode for grass.</summary>
    public enum GrassTierMode
    {
        /// <summary>Probe the device (<see cref="GpuGrassTierProbe.ClassifyAuto"/>) and pick the tier.</summary>
        Auto,
        /// <summary>Force interactive GPUGrass (errors on non-compute devices — debug only).</summary>
        ForceGpu,
        /// <summary>Force Unity's built-in Terrain detail grass (non-interactive fallback) on all devices.</summary>
        ForceTerrainFallback,
        /// <summary>Force no grass on all devices.</summary>
        ForceDisabled,
    }

    /// <summary>Where the editor bake reads grass placement from.</summary>
    public enum GrassPlacementSource
    {
        /// <summary>Painted Terrain detail (grass) layer — density-weighted by what you painted.</summary>
        DetailLayer,
        /// <summary>The Terrain SURFACE mesh — scatter uniformly over the whole terrain (no painting
        /// required), masked by slope + normalized altitude. Use when you want grass everywhere.</summary>
        TerrainSurface,
    }

    /// <summary>
    /// All tunables for a GPUGrass field, in one ScriptableObject (SSOT). Authored once, referenced by a
    /// <see cref="GpuGrassController"/>. Placement params drive the editor bake; render/wind/bend params are
    /// read by the renderer each frame.
    /// </summary>
    [CreateAssetMenu(menuName = "GPUGrass/Grass Config", fileName = "GpuGrassConfig")]
    public sealed class GpuGrassConfig : ScriptableObject
    {
        [Header("Placement (editor bake)")]
        [Tooltip("Where the bake reads coverage from: the painted detail layer, or the whole terrain surface mesh.")]
        [SerializeField] private GrassPlacementSource placementSource = GrassPlacementSource.DetailLayer;
        [Min(0f)] [SerializeField] private float targetDensityPerSqM = 0.76f;
        [SerializeField] private Vector2 scaleRange = new(0.8f, 1.2f);
        [SerializeField] private Vector2 bladeHeightRange = new(0.3f, 0.6f);
        [Tooltip("Min/max ground slope (degrees) grass is allowed on.")]
        [SerializeField] private Vector2 slopeRange = new(0f, 60f);
        [Tooltip("TerrainSurface mode only: normalized altitude band [0=lowest,1=highest] grass may grow in " +
                 "(e.g. (0,0.7) keeps grass off mountain tops). (0,1) = no altitude limit.")]
        [SerializeField] private Vector2 heightRange01 = new(0f, 1f);
        [SerializeField] private int seed = 0;

        [Header("LOD / Render")]
        [SerializeField] private Mesh[] lodMeshes = Array.Empty<Mesh>();
        [Tooltip("Camera distance (m) at which each LOD ends; length = lodMeshes.Length - 1.")]
        [SerializeField] private float[] lodMaxDistances = { 15f, 40f };
        [Tooltip("Whole-field cull distance in metres — grass past this is not drawn (mobile-friendly). " +
                 "0 = never cull by distance (renders to infinity).")]
        [Min(0f)] [SerializeField] private float renderCullDistance = 80f;
        [Tooltip("Base grass material (GPUGrass/IndirectGrass shader). The renderer clones it per LOD.")]
        [SerializeField] private Material? grassMaterial;
        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;
        [SerializeField] private bool receiveShadows;
        [Tooltip("Optional base map applied to the grass material by Auto-Setup. For see-through blade cards, " +
                 "assign a texture whose ALPHA channel carves the blade silhouette and enable Alpha Clip below. " +
                 "Leave empty for solid procedural-mesh blades.")]
        [SerializeField] private Texture2D? baseMap;
        [Tooltip("Alpha-clip cutout: discards fragments where baseMap.a < Alpha Cutoff (transparent blade " +
                 "cards). Needs a baseMap with a real alpha channel — off = solid opaque blades. Auto-Setup " +
                 "keeps the material's _ALPHACLIP keyword in sync with this flag.")]
        [SerializeField] private bool alphaClip;
        [Range(0f, 1f)]
        [Tooltip("Alpha-clip threshold — only used when Alpha Clip is on.")]
        [SerializeField] private float alphaCutoff = 0.4f;

        [Header("Render assets (wired by Auto-Setup — serialized so they ship in player builds)")]
        [Tooltip("GPUGrass GrassCull compute shader (ChunkCull/WriteArgsB/BladeCullCount/WriteLodOffsets/BladeCullScatter kernels).")]
        [SerializeField] private ComputeShader? cullCompute;
        [Tooltip("GPUGrass/IndirectGrass shader. Used to build a base material when grassMaterial is unset.")]
        [SerializeField] private Shader? indirectShader;

        [Header("Wind (ambient sway)")]
        [SerializeField] private Vector2 windDirection = new(1f, 0.3f);
        [Min(0f)] [SerializeField] private float windStrength = 0.15f;
        [Min(0f)] [SerializeField] private float windFrequency = 1.2f;
        [Min(0f)] [SerializeField] private float windNoiseScale = 0.1f;

        [Header("Bend (interactor / trail)")]
        [Min(0f)] [SerializeField] private float bendStrength = 1f;
        [Range(0f, 1f)] [SerializeField] private float flatten = 0.3f;
        [Tooltip("How fast a leaned blade returns upright (units/sec).")]
        [Min(0f)] [SerializeField] private float recoveryRate = 2f;
        [SerializeField] private bool enableTrailInteractors = true;

        [Header("Adaptive density (mobile load governor)")]
        [Tooltip("When on, grass density auto-thins under load (low FPS) and restores under headroom — a " +
                 "thermal/perf safety valve on weaker devices. GPU-side skip, so thinning is ~free.")]
        [SerializeField] private bool enableAdaptiveDensity = true;
        [Tooltip("Frame rate the governor tries to hold; grass thins when the frame time exceeds 1000/this.")]
        [Min(1f)] [SerializeField] private float adaptiveTargetFps = 60f;
        [Tooltip("Density floor (fraction of full) the governor will never drop below — keeps the field from " +
                 "going bald even at max load. 1 = no thinning allowed.")]
        [Range(0f, 1f)] [SerializeField] private float minDensity = 0.6f;

        [Header("Occlusion (Hi-Z)")]
        [Tooltip("GPU Hi-Z occlusion cull: skips grass chunks hidden behind terrain/geometry. " +
                 "Auto-disabled when no camera depth texture is available. Default OFF: on a mostly-flat " +
                 "mobile field the per-frame depth resolve + pyramid build is pure overhead versus frustum + " +
                 "distance + LOD2 thinning, which already remove most off-screen/far grass far cheaper. Opt " +
                 "back in for hilly / heavily-occluded terrain (see docs/GPUGrass.md §8.2 #1).")]
        [SerializeField] private bool enableOcclusionCulling = false;

        [Header("Device tier policy")]
        [SerializeField] private GrassTierMode tierMode = GrassTierMode.Auto;
        [Tooltip("Auto tier: when the device has no compute support, fall back to Unity's built-in Terrain " +
                 "detail grass (non-interactive) instead of disabling grass.")]
        [SerializeField] private bool enableTerrainFallback = true;
        [Tooltip("Auto tier: devices with less system memory than this (MB) get NO grass. 0 = never disable by memory.")]
        [Min(0)] [SerializeField] private int lowEndMemoryThresholdMB = 2048;
        [Tooltip("Terrain Detail Distance to restore when using the built-in-terrain-grass fallback tier.")]
        [Min(0f)] [SerializeField] private float terrainFallbackDetailDistance = 80f;

        // ── Placement accessors ────────────────────────────────────────────────
        public float TargetDensityPerSqM => this.targetDensityPerSqM;
        public Vector2 ScaleRange => this.scaleRange;
        public Vector2 BladeHeightRange => this.bladeHeightRange;
        public Vector2 SlopeRange => this.slopeRange;
        public GrassPlacementSource PlacementSource => this.placementSource;
        public Vector2 HeightRange01 => this.heightRange01;
        public int Seed => this.seed;

        // ── Render accessors ───────────────────────────────────────────────────
        public Mesh[] LodMeshes => this.lodMeshes;
        public float[] LodMaxDistances => this.lodMaxDistances;
        public float RenderCullDistance => this.renderCullDistance;
        public Material? GrassMaterial => this.grassMaterial;
        public ShadowCastingMode ShadowCastingMode => this.shadowCastingMode;
        public bool ReceiveShadows => this.receiveShadows;
        public Texture2D? BaseMap => this.baseMap;
        public bool AlphaClip => this.alphaClip;
        public float AlphaCutoff => this.alphaCutoff;
        public ComputeShader? CullCompute => this.cullCompute;
        public Shader? IndirectShader => this.indirectShader;

        // ── Adaptive density accessors ─────────────────────────────────────────
        public bool EnableAdaptiveDensity => this.enableAdaptiveDensity;
        public float AdaptiveTargetFps => this.adaptiveTargetFps;
        public float MinDensity => this.minDensity;

        /// <summary>Assigns the render assets (Auto-Setup, editor). Serialized so they ship in builds.</summary>
        public void SetRenderAssets(ComputeShader? cull, Shader? indirect, Material? material)
        {
            this.cullCompute    = cull;
            this.indirectShader = indirect;
            if (material != null) this.grassMaterial = material;
        }

        /// <summary>Assigns the per-LOD blade meshes (Auto-Setup, editor). Index 0 = nearest LOD.</summary>
        public void SetLodMeshes(Mesh[] meshes) => this.lodMeshes = meshes ?? Array.Empty<Mesh>();

        /// <summary>
        /// Assigns the LOD switch distances (Auto-Setup, editor). Length should be
        /// <c>LodMeshes.Length - 1</c>; an empty array routes every blade to LOD0 (single-mesh fields).
        /// </summary>
        public void SetLodMaxDistances(float[] distances) => this.lodMaxDistances = distances ?? Array.Empty<float>();

        // ── Perf / tier mutators (editor tooling, e.g. the Mobile Preset) ───────
        // Direct field setters so editor tooling never round-trips through a string-keyed SerializedObject
        // (a mistyped/absent property there fails silently). These are the SSOT write path — unit-testable.
        public void SetRenderCullDistance(float metres) => this.renderCullDistance = Mathf.Max(0f, metres);
        public void SetTargetDensityPerSqM(float density) => this.targetDensityPerSqM = Mathf.Max(0f, density);
        public void SetMinDensity(float floor01) => this.minDensity = Mathf.Clamp01(floor01);
        public void SetAdaptiveDensity(bool enabled) => this.enableAdaptiveDensity = enabled;
        public void SetOcclusionCulling(bool enabled) => this.enableOcclusionCulling = enabled;
        public void SetShadows(ShadowCastingMode casting, bool receive)
        {
            this.shadowCastingMode = casting;
            this.receiveShadows = receive;
        }
        public void SetTierMode(GrassTierMode mode) => this.tierMode = mode;
        /// <summary>Assigns the grass material base map (Auto-Setup / authoring). SSOT for the material's _BaseMap.</summary>
        public void SetBaseMap(Texture2D? map) => this.baseMap = map;
        /// <summary>Enables/disables alpha-clip cutout and its threshold (Auto-Setup / authoring). SSOT for the
        /// material's _Alphaclip float, _ALPHACLIP keyword, and _Cutoff.</summary>
        public void SetAlphaClip(bool enabled, float cutoff)
        {
            this.alphaClip = enabled;
            this.alphaCutoff = Mathf.Clamp01(cutoff);
        }

        // ── Wind accessors ─────────────────────────────────────────────────────
        public Vector2 WindDirection => this.windDirection;
        public float WindStrength => this.windStrength;
        public float WindFrequency => this.windFrequency;
        public float WindNoiseScale => this.windNoiseScale;

        // ── Bend accessors ─────────────────────────────────────────────────────
        public float BendStrength => this.bendStrength;
        public float Flatten => this.flatten;
        public float RecoveryRate => this.recoveryRate;
        public bool EnableTrailInteractors => this.enableTrailInteractors;

        // ── Occlusion ──────────────────────────────────────────────────────────
        public bool EnableOcclusionCulling => this.enableOcclusionCulling;

        // ── Tier policy ────────────────────────────────────────────────────────
        public GrassTierMode TierMode => this.tierMode;
        public bool EnableTerrainFallback => this.enableTerrainFallback;
        public int LowEndMemoryThresholdMB => this.lowEndMemoryThresholdMB;
        public float TerrainFallbackDetailDistance => this.terrainFallbackDetailDistance;
    }
}
