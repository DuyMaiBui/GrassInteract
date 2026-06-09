#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// Per-LOD mesh + maximum camera distance pair.
    /// Used by <see cref="ScatterLayer.Lods"/> to unify LOD data for both Grass and Mesh kinds.
    /// The LOD is active when camera distance &lt;= <see cref="maxDistance"/>. LOD0 should have
    /// the smallest maxDistance (highest detail, close range); the last entry covers all remaining
    /// distances.
    /// </summary>
    [System.Serializable]
    public struct ScatterLod
    {
        [Tooltip("Mesh for this LOD level. LOD0 = highest detail.")]
        public Mesh? mesh;

        [Tooltip("Maximum camera distance (metres) at which this LOD is still active. " +
                 "The last LOD covers all remaining distances beyond the previous entry.")]
        [Min(0f)]
        public float maxDistance;
    }

    /// <summary>
    /// Abstract base for scatter layers. Concrete subclasses:
    /// <see cref="DensityScatterLayer"/> (procedural density-map scatter) and
    /// <see cref="InstanceScatterLayer"/> (authored per-instance sidecar).
    ///
    /// Engine route: <see cref="InteractsWithDeform"/> == true → grass pipeline
    /// (GrassCpuEngine / GrassGpuEngine); false → mesh-prop pipeline (MeshScatterEngine).
    ///
    /// Instances of this type are sub-assets of <see cref="TerrainScatterConfig"/>; do NOT create
    /// directly via <c>Assets &gt; Create</c>.
    ///
    /// SSOT: every shared per-layer concern lives here — LOD meshes, render material, shadow mode,
    /// wind tunables, bend/trample tunables, and AABB headroom. Placement-specific fields live on
    /// the concrete subclasses.
    /// </summary>
    public abstract class ScatterLayer : ScriptableObject
    {
        // ── Deform (wind + interactors) ────────────────────────────────────────

        [BoxGroup("Deform")]
        [Tooltip("If true, this layer's instances sway with the global wind. ON for grass layers, OFF for mesh layers — set explicitly per layer to override.")]
        [SerializeField] private bool affectedByWind = true;

        [BoxGroup("Deform")]
        [Tooltip("If true, this layer's instances lean away from interactors (the orange sphere etc.). ON for grass layers, OFF for mesh layers.")]
        [SerializeField] private bool affectedByInteractors = true;

        // ── Rendering ──────────────────────────────────────────────────────────

        [BoxGroup("Rendering")]
        [Tooltip("Render material. Pipeline is selected by InteractsWithDeform: true -> grass shader (InstancedGrass/IndirectGrass); false -> mesh-prop shader (ScatterInstanced).")]
        [UnityEngine.Serialization.FormerlySerializedAs("grassMaterial")]
        [SerializeField] private Material? material;

        [BoxGroup("Rendering")]
        [Tooltip("Shadow casting for this layer. Off is recommended for dense mobile grass.")]
        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;

        // ── Wind ──────────────────────────────────────────────────────────────

        /// <summary>Wind model the GPU shader applies. CPU simulator keeps Sine regardless.</summary>
        public enum WindMode
        {
            /// <summary>Directional sin() wave per blade (legacy). Cheapest; matches CPU sim exactly.</summary>
            Sine,

            /// <summary>2-octave Perlin gust + ripple sampled in the vertex shader (GPU-only).</summary>
            Perlin,
        }

        [BoxGroup("Wind")]
        [Tooltip("Wind model. Sine = directional sin() wave (legacy, matches CPU sim). " +
                 "Perlin = 2-octave gust+ripple in the GPU shader (CPU sim still uses Sine).")]
        [SerializeField] private WindMode windMode = WindMode.Sine;

        [BoxGroup("Wind")]
        [Tooltip("Ambient wind direction in the XZ plane (auto-normalized when bound).")]
        [SerializeField] private Vector2 windDirection = new Vector2(1f, 0f);

        [BoxGroup("Wind")]
        [Range(0f, 2f)]
        [Tooltip("Horizontal sway amplitude at the blade tip, in metres. Scales by height so roots stay put.")]
        [SerializeField] private float windStrength = 0.15f;

        [BoxGroup("Wind")]
        [Range(0f, 5f)]
        [Tooltip("Sine mode: sway oscillation speed (cycles scale with _Time).")]
        [SerializeField] private float windFrequency = 1.2f;

        [BoxGroup("Wind")]
        [Range(0.01f, 2f)]
        [Tooltip("Sine mode: spatial noise scale — how quickly the gust phase varies across the field.")]
        [SerializeField] private float windNoiseScale = 0.25f;

        [BoxGroup("Wind")]
        [Range(0.005f, 0.5f)]
        [Tooltip("Perlin mode: spatial frequency of the slow gust octave (smaller = larger patches).")]
        [SerializeField] private float windGustScale = 0.05f;

        [BoxGroup("Wind")]
        [Range(0.05f, 2f)]
        [Tooltip("Perlin mode: spatial frequency of the fast ripple octave.")]
        [SerializeField] private float windRippleScale = 0.4f;

        [BoxGroup("Wind")]
        [Range(0f, 3f)]
        [Tooltip("Perlin mode: scroll speed of the gust octave (metres/sec along _WindDir).")]
        [SerializeField] private float windGustSpeed = 0.3f;

        [BoxGroup("Wind")]
        [Range(0f, 5f)]
        [Tooltip("Perlin mode: scroll speed of the ripple octave (metres/sec along _WindDir).")]
        [SerializeField] private float windRippleSpeed = 1.5f;

        [BoxGroup("Wind")]
        [Range(0f, 1f)]
        [Tooltip("Perlin mode: how much the ripple octave contributes on top of the gust (0 = gust only).")]
        [SerializeField] private float windRippleWeight = 0.35f;

        // ── Trample ───────────────────────────────────────────────────────────

        [BoxGroup("Trample")]
        [Range(0f, 4f)]
        [Tooltip("Lateral lean amplitude at the blade tip, in metres, at full interactor magnitude. " +
                 "Leans AWAY from the interactor. 0 = no lean.")]
        [SerializeField] private float bendStrength = 0.7f;

        [BoxGroup("Trample")]
        [Range(0f, 1f)]
        [Tooltip("Height loss at full interactor magnitude (0..1). Mats the trampled core straight DOWN.")]
        [SerializeField] private float flatten = 0.5f;

        [BoxGroup("Trample")]
        [Min(0f)]
        [Tooltip("Recovery speed (metres/second of upright restoration). How fast a leaned blade returns toward upright.")]
        [SerializeField] private float recoveryRate = 4f;

        // ── Bounds ────────────────────────────────────────────────────────────

        [BoxGroup("Bounds")]
        [Tooltip("Unscaled height (metres) of the LOD0 blade mesh.")]
        [Min(0.01f)]
        [SerializeField] private float maxBladeHeight = 1f;

        [BoxGroup("Bounds")]
        [Tooltip("Extra headroom (metres) added to the field-wide AABB beyond the scaled blade height. " +
                 "Covers wind/trample deform so bent blades are never frustum-culled.")]
        [Min(0f)]
        [SerializeField] private float bendHeadroom = 1f;

        // ── GPU-Driven ────────────────────────────────────────────────────────

        [BoxGroup("GPU-Driven")]
        [Min(1)]
        [Tooltip("World-space XZ cell size (metres) for the GPU-driven spatial grid. " +
                 "Smaller = finer culling granularity but more GPU overhead; 8–16 m is recommended.")]
        [SerializeField] private int chunkSize = 16;

        // ── LOD / Render ──────────────────────────────────────────────────────

        [BoxGroup("LOD Render")]
        [Tooltip("Per-LOD mesh + switch distance pairs. LOD0 (highest detail) first.\n" +
                 "maxDistance = the farthest camera distance (metres) at which this LOD is still used.")]
        [SerializeField] private ScatterLod[] lods = System.Array.Empty<ScatterLod>();

        // ── Placement (virtual base defaults) ────────────────────────────────
        // These fields moved to DensityScatterLayer in Phase A. The base class declares virtual
        // accessors with sane defaults so engines that take a ScatterLayer can still call them.

        [BoxGroup("Placement")]
        [Tooltip("Colliders the placement raycast snaps instances onto. Instances with no hit fall back to the field-plane Y.")]
        [SerializeField] private LayerMask groundSnapMask = ~0;

        // ── Public accessors ──────────────────────────────────────────────────

        public bool AffectedByWind         => this.affectedByWind;
        public bool AffectedByInteractors  => this.affectedByInteractors;

        /// <summary>
        /// True when either wind sway or interactor lean is enabled — used by engines to decide
        /// whether to upload deform buffers at all. Also determines the engine route:
        /// true → grass pipeline; false → mesh-prop pipeline.
        /// </summary>
        public bool InteractsWithDeform    => this.affectedByWind || this.affectedByInteractors;

        /// <summary>
        /// Single render material. Pipeline selected by <see cref="InteractsWithDeform"/>:
        /// true → InstancedGrass / IndirectGrass shader;
        /// false → ScatterInstanced shader.
        /// </summary>
        public Material? Material => this.material;

        /// <summary>
        /// Returns the placement strategy for this layer.
        /// Concrete subclasses implement this to return their respective strategy.
        /// </summary>
        public abstract IScatterPlacement CreatePlacement();

        /// <summary>
        /// The density map for this layer. Override in <see cref="DensityScatterLayer"/>.
        /// For <see cref="InstanceScatterLayer"/> this returns null.
        /// </summary>
        public virtual Texture2D? DensityMap => null;

        /// <summary>
        /// The authored-instances sidecar sub-asset. Non-null only on <see cref="InstanceScatterLayer"/>.
        /// </summary>
        public virtual AuthoredInstancesData? AuthoredInstances => null;

        /// <summary>
        /// Minimum spacing (metres) between placed instances during a Place-brush stroke.
        /// Override in <see cref="InstanceScatterLayer"/>. Default 0.5 matches prior base default.
        /// </summary>
        public virtual float PlaceSpacing => 0.5f;

        // Virtual placement accessors — concrete values live on DensityScatterLayer;
        // base provides sane defaults so engine code compiles without casting.
        /// <summary>World-space XZ size of the field, centered on the ScatterField transform.</summary>
        public virtual Vector2 FieldBounds => new Vector2(100f, 100f);

        /// <summary>Instance scale random range [min, max].</summary>
        public virtual Vector2 ScaleRange => new Vector2(0.8f, 1.2f);

        /// <summary>RNG seed for procedural placement.</summary>
        public virtual int Seed => 0;

        /// <summary>Slope range [minDeg, maxDeg] within which placement is allowed.</summary>
        public virtual Vector2 SlopeRange => new Vector2(0f, 90f);

        /// <summary>Terrain alphamap layer index for splat-mask filtering. -1 = off.</summary>
        public virtual int SplatLayerIndex => -1;

        /// <summary>Minimum splat weight for placement when SplatLayerIndex >= 0.</summary>
        public virtual float SplatThreshold => 0.5f;

        /// <summary>Uniform rotation offset (Euler degrees) applied to every instance.</summary>
        public virtual Vector3 RotationOffsetEuler => Vector3.zero;

        /// <summary>Per-instance random pitch (X-axis) range in degrees.</summary>
        public virtual Vector2 RandomPitchRange => Vector2.zero;

        /// <summary>Per-instance random roll (Z-axis) range in degrees.</summary>
        public virtual Vector2 RandomRollRange => Vector2.zero;

        /// <summary>Align instance up-axis to terrain/surface normal.</summary>
        public virtual bool AlignToNormal => false;

        /// <summary>
        /// True when per-instance oriented packing is needed in the GPU buffers.
        /// Activated by AlignToNormal or non-zero pitch/roll ranges.
        /// </summary>
        public virtual bool IsOriented => false;

        public LayerMask GroundSnapMask => this.groundSnapMask;

        // ── Unified LOD accessors ─────────────────────────────────────────────

        /// <summary>Raw LOD entries. LOD0 = highest detail.</summary>
        public ScatterLod[] Lods => this.lods;

        /// <summary>
        /// Ordered array of LOD meshes (LOD0 first) derived from <see cref="lods"/>.
        /// Returns empty when <see cref="lods"/> is empty.
        /// </summary>
        public Mesh[] LodMeshes
        {
            get
            {
                var meshes = new Mesh[this.lods.Length];
                for (int i = 0; i < this.lods.Length; ++i)
                    meshes[i] = this.lods[i].mesh!;
                return meshes;
            }
        }

        /// <summary>
        /// Ordered array of LOD switch distances (metres).
        /// Length = <see cref="lods"/>.Length - 1.
        /// </summary>
        public float[] LodMaxDistances
        {
            get
            {
                int distCount = Mathf.Max(0, this.lods.Length - 1);
                var dists = new float[distCount];
                for (int i = 0; i < distCount; ++i)
                    dists[i] = this.lods[i].maxDistance;
                return dists;
            }
        }

        // ── Rendering accessors ───────────────────────────────────────────────

        /// <summary>Shadow casting mode for this layer.</summary>
        public ShadowCastingMode ShadowCastingMode => this.shadowCastingMode;

        // ── Wind accessors ────────────────────────────────────────────────────

        /// <summary>Wind model — Sine (legacy, CPU-parity) or Perlin (GPU-only 2-octave gust+ripple).</summary>
        public WindMode Mode => this.windMode;

        /// <summary>Ambient wind direction in the XZ plane (auto-normalized when bound).</summary>
        public Vector2 WindDirection => this.windDirection;

        /// <summary>Horizontal sway amplitude at the blade tip, in metres.</summary>
        public float WindStrength => this.windStrength;

        /// <summary>Sine mode: sway oscillation speed.</summary>
        public float WindFrequency => this.windFrequency;

        /// <summary>Sine mode: spatial noise scale — per-blade phase variation.</summary>
        public float WindNoiseScale => this.windNoiseScale;

        /// <summary>Perlin mode: spatial frequency of the slow gust octave.</summary>
        public float WindGustScale => this.windGustScale;

        /// <summary>Perlin mode: spatial frequency of the fast ripple octave.</summary>
        public float WindRippleScale => this.windRippleScale;

        /// <summary>Perlin mode: scroll speed of the gust octave.</summary>
        public float WindGustSpeed => this.windGustSpeed;

        /// <summary>Perlin mode: scroll speed of the ripple octave.</summary>
        public float WindRippleSpeed => this.windRippleSpeed;

        /// <summary>Perlin mode: ripple-on-gust mix amount.</summary>
        public float WindRippleWeight => this.windRippleWeight;

        // ── Trample accessors ─────────────────────────────────────────────────

        /// <summary>Lateral lean amplitude at the blade tip at full interactor magnitude.</summary>
        public float BendStrength => this.bendStrength;

        /// <summary>Height loss at full interactor magnitude (0..1).</summary>
        public float Flatten => this.flatten;

        /// <summary>Recovery speed (metres/second of upright restoration).</summary>
        public float RecoveryRate => this.recoveryRate;

        // ── Bounds accessors ──────────────────────────────────────────────────

        /// <summary>Unscaled height (metres) of the LOD0 blade mesh.</summary>
        public float MaxBladeHeight => this.maxBladeHeight;

        /// <summary>Extra headroom (metres) added to the field-wide AABB beyond the scaled blade height.</summary>
        public float BendHeadroom => this.bendHeadroom;

        // ── GPU-Driven accessors ──────────────────────────────────────────────

        /// <summary>World-space XZ cell size (metres) for the GPU-driven spatial grid.</summary>
        public int ChunkSize => this.chunkSize;

        // ── Validation ────────────────────────────────────────────────────────

        /// <summary>
        /// Validates the shared layer properties. Subclasses override to add placement-type-specific
        /// validation, then call base.Validate.
        /// </summary>
        public virtual bool Validate(out string error)
        {
            if (this.ScaleRange.x <= 0f || this.ScaleRange.y < this.ScaleRange.x)
            {
                error = $"ScaleRange ({this.ScaleRange}) must be positive and non-decreasing.";
                return false;
            }

            if (this.FieldBounds.x <= 0f || this.FieldBounds.y <= 0f)
            {
                error = $"FieldBounds ({this.FieldBounds}) must be positive.";
                return false;
            }

            error = string.Empty;
            return true;
        }

    }
}
