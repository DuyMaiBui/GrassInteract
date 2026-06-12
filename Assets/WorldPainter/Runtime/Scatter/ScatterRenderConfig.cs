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

        public ScatterRenderConfig(Material? material, ShadowCastingMode shadowCastingMode, ScatterLod[] lods, float renderCullDistance)
        {
            this.material = material;
            this.shadowCastingMode = shadowCastingMode;
            this.lods = lods;
            this.renderCullDistance = renderCullDistance;
        }

        public Material? Material => this.material;
        public ShadowCastingMode ShadowCastingMode => this.shadowCastingMode;
        public ScatterLod[] Lods => this.lods ?? System.Array.Empty<ScatterLod>();

        /// <summary>Hard render cull distance (metres). Instances past this distance do not render.</summary>
        public float RenderCullDistance => this.renderCullDistance;

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

            return new ScatterRenderConfig(cfg.Material, cfg.ShadowCastingMode, cfg.Lods, migrated);
        }
#endif
    }
}
