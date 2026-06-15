#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldPainter
{
    /// <summary>
    /// Shared rendering configuration for any scatter layer.
    /// Composed by concrete layer types rather than inherited from a base class.
    /// </summary>
    [System.Serializable]
    public struct ScatterRenderConfig
    {
        [BoxGroup("Rendering")]
        [Tooltip("Render material. Pipeline is selected by deform interaction: true -> grass shader; false -> mesh-prop shader.")]
        [UnityEngine.Serialization.FormerlySerializedAs("grassMaterial")]
        [SerializeField] private Material? material;

        [BoxGroup("Rendering")]
        [Tooltip("Shadow casting for this layer. Off is recommended for dense mobile grass.")]
        [SerializeField] private ShadowCastingMode shadowCastingMode;

        [BoxGroup("LOD Render")]
        [Tooltip("Per-LOD mesh + switch distance pairs. LOD0 (highest detail) first.")]
        [SerializeField] private ScatterLod[] lods;

        [BoxGroup("LOD Render")]
        [Tooltip("Hard render cull distance (metres). Instances beyond this distance are not rendered. " +
                 "The last LOD covers [last LOD switch distance .. renderCullDistance); past it = CULLED.")]
        [Min(0f)]
        [SerializeField] private float renderCullDistance;

        // ── Phase 2: PBR material fields (SSOT — all default off/null) ────────────
        // All toggles default false so existing layers run the original lighting path (regression baseline).

        [BoxGroup("PBR Materials")]
        [Tooltip("Enable normal-map sampling. Grass synthesizes TBN from blade basis; props use mesh tangents.")]
        [SerializeField] private bool useNormalMap;

        [BoxGroup("PBR Materials")]
        [Tooltip("Normal map texture.")]
        [SerializeField] private Texture2D? normalMap;

        [BoxGroup("PBR Materials")]
        [Tooltip("Normal map strength multiplier (1 = full strength).")]
        [SerializeField] private float normalStrength;

        [BoxGroup("PBR Materials")]
        [Tooltip("Enable full URP PBR lighting (UniversalFragmentPBR). When off, the original stylized path runs.")]
        [SerializeField] private bool usePbr;

        // URP mask map channel convention: Metallic R / AO G / (detail B — unused) / Smoothness A.
        // This matches the Unity Lit shader mask map layout.
        [BoxGroup("PBR Materials")]
        [Tooltip("Mask map: R=Metallic, G=Occlusion, B=unused, A=Smoothness. Only sampled when usePbr is true.")]
        [SerializeField] private Texture2D? maskMap;

        [BoxGroup("PBR Materials")]
        [Tooltip("Enable emission.")]
        [SerializeField] private bool useEmission;

        [BoxGroup("PBR Materials")]
        [Tooltip("Emission texture. Combined with emissionColor.")]
        [SerializeField] private Texture2D? emissionMap;

        [BoxGroup("PBR Materials")]
        [Tooltip("Emission color multiplier (HDR). Combined with emissionMap.")]
        [SerializeField] private Color emissionColor;

        // ── Phase 3: Shadow fields (SSOT — all default off/0 so keyword-off path == Phase 2 all-OFF) ───
        // All shadow toggles default false/0 — regression baseline: all-OFF + bias-0 == Phase 2 all-OFF state.

        [BoxGroup("Shadows")]
        [Tooltip("Receive shadows cast by other objects onto this scatter layer. Off by default.")]
        [SerializeField] private bool receiveShadows;

        [BoxGroup("Shadows")]
        [Tooltip("Shadow strength multiplier (0=no shadow, 1=full shadow). Only used when Shadow Tint is enabled.")]
        [Min(0f)]
        [SerializeField] private float shadowStrength;

        [BoxGroup("Shadows")]
        [Tooltip("Tint applied to the shadowed region. White = neutral (no tint). Only used when Shadow Tint is enabled.")]
        [SerializeField] private Color shadowTint;

        [BoxGroup("Shadows")]
        [Tooltip("Per-layer shadow depth bias offset. 0 = no-op (default). Positive values push the shadow caster away from the surface.")]
        [SerializeField] private float shadowDepthBias;

        [BoxGroup("Shadows")]
        [Tooltip("Per-layer shadow normal bias offset. 0 = no-op (default). Shrinks shadow acne by offsetting along the surface normal.")]
        [SerializeField] private float shadowNormalBias;

        [BoxGroup("Shadows")]
        [Tooltip("Alpha-clip the shadow caster by base-map alpha. OFF = solid-quad caster (default). Requires _Cutoff to be sensible (0.5 default baked into the shader property).")]
        [SerializeField] private bool useAlphaclipShadows;

        public ScatterRenderConfig(Material? material, ShadowCastingMode shadowCastingMode, ScatterLod[] lods, float renderCullDistance)
        {
            this.material = material;
            this.shadowCastingMode = shadowCastingMode;
            this.lods = lods;
            this.renderCullDistance = renderCullDistance;
            this.useNormalMap    = false;
            this.normalMap       = null;
            this.normalStrength  = 1f;
            this.usePbr          = false;
            this.maskMap         = null;
            this.useEmission     = false;
            this.emissionMap     = null;
            this.emissionColor   = Color.black;
            // Phase 3 defaults — all OFF / 0 so keyword-off path is unchanged.
            this.receiveShadows      = false;
            this.shadowStrength      = 1f;
            this.shadowTint          = Color.white;
            this.shadowDepthBias     = 0f;
            this.shadowNormalBias    = 0f;
            this.useAlphaclipShadows = false;
        }

        /// <summary>Copy constructor with an overridden renderCullDistance. Preserves all other fields including PBR and shadows.</summary>
        internal ScatterRenderConfig(ScatterRenderConfig src, float overrideCullDistance)
        {
            this.material           = src.material;
            this.shadowCastingMode  = src.shadowCastingMode;
            this.lods               = src.lods;
            this.renderCullDistance = overrideCullDistance;
            this.useNormalMap       = src.useNormalMap;
            this.normalMap          = src.normalMap;
            this.normalStrength     = src.normalStrength;
            this.usePbr             = src.usePbr;
            this.maskMap            = src.maskMap;
            this.useEmission        = src.useEmission;
            this.emissionMap        = src.emissionMap;
            this.emissionColor      = src.emissionColor;
            // Phase 3 — carry shadow fields through the copy constructor so MigrateCull doesn't drop them.
            this.receiveShadows      = src.receiveShadows;
            this.shadowStrength      = src.shadowStrength;
            this.shadowTint          = src.shadowTint;
            this.shadowDepthBias     = src.shadowDepthBias;
            this.shadowNormalBias    = src.shadowNormalBias;
            this.useAlphaclipShadows = src.useAlphaclipShadows;
        }

        public Material? Material => this.material;
        public ShadowCastingMode ShadowCastingMode => this.shadowCastingMode;
        public ScatterLod[] Lods => this.lods ?? System.Array.Empty<ScatterLod>();

        /// <summary>Hard render cull distance (metres). Instances past this distance do not render.</summary>
        public float RenderCullDistance => this.renderCullDistance;

        // ── Phase 2: PBR accessors ────────────────────────────────────────────────
        public bool       UseNormalMap   => this.useNormalMap;
        public Texture2D? NormalMap      => this.normalMap;
        public float      NormalStrength => this.normalStrength > 0f ? this.normalStrength : 1f;
        public bool       UsePbr         => this.usePbr;
        public Texture2D? MaskMap        => this.maskMap;
        public bool       UseEmission    => this.useEmission;
        public Texture2D? EmissionMap    => this.emissionMap;
        public Color      EmissionColor  => this.emissionColor;

        // ── Phase 3: Shadow accessors ─────────────────────────────────────────
        public bool  ReceiveShadows => this.receiveShadows;
        // Guard against zero/default: pre-existing layers deserialize shadowStrength=0 (new field).
        // Return 1 (full shadow) as the intended default when the serialized value was never set.
        // Mirrors the NormalStrength guard pattern.
        public float ShadowStrength => this.shadowStrength > 0f ? this.shadowStrength : 1f;
        // Guard against zero/default: pre-existing layers deserialize shadowTint=(0,0,0,0).
        // Return white as the intended default when the serialized alpha is zero.
        public Color ShadowTint => this.shadowTint.a > 0f ? this.shadowTint : Color.white;
        public float ShadowDepthBias  => this.shadowDepthBias;
        public float ShadowNormalBias => this.shadowNormalBias;
        public bool  UseAlphaclipShadows => this.useAlphaclipShadows;

        public Mesh[] LodMeshes
        {
            get
            {
                ScatterLod[] src = this.lods ?? System.Array.Empty<ScatterLod>();
                var meshes = new Mesh[src.Length];
                for (int i = 0; i < src.Length; ++i)
                    meshes[i] = src[i].mesh!;
                return meshes;
            }
        }

        public float[] LodMaxDistances
        {
            get
            {
                ScatterLod[] src = this.lods ?? System.Array.Empty<ScatterLod>();
                int distCount = Mathf.Max(0, src.Length - 1);
                var dists = new float[distCount];
                for (int i = 0; i < distCount; ++i)
                    dists[i] = src[i].maxDistance;
                return dists;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Returns a copy of <paramref name="cfg"/> with <see cref="RenderCullDistance"/> back-filled when it is zero.
        /// Idempotent: returns the original value unchanged when <see cref="RenderCullDistance"/> is already positive.
        /// Migration formula: max(2 * lastSwitchDistance, 500) — preserves the legacy derived far-cull.
        /// </summary>
        public static ScatterRenderConfig MigrateCull(ScatterRenderConfig cfg)
        {
            if (cfg.RenderCullDistance > 0f)
                return cfg;

            float[] dists    = cfg.LodMaxDistances; // length == lods.Length - 1
            // Legacy far cull was max(2 * lastSwitchDistance, 500). <2 LODs → dists empty → 500m floor.
            float lastSwitch = dists.Length > 0 ? dists[dists.Length - 1] : 0f;
            float migrated   = Mathf.Max(2f * lastSwitch, 500f);

            return new ScatterRenderConfig(cfg, migrated);
        }
#endif
    }
}
