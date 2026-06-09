#nullable enable
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
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

        public ScatterRenderConfig(Material? material, ShadowCastingMode shadowCastingMode, ScatterLod[] lods)
        {
            this.material = material;
            this.shadowCastingMode = shadowCastingMode;
            this.lods = lods;
        }

        public Material? Material => this.material;
        public ShadowCastingMode ShadowCastingMode => this.shadowCastingMode;
        public ScatterLod[] Lods => this.lods ?? System.Array.Empty<ScatterLod>();

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
    }
}
