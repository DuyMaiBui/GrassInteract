#nullable enable
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassInteract
{
    /// <summary>
    /// Render/LOD configuration for an interactive grass field: LOD meshes + distance thresholds, shadow
    /// casting, ambient-wind tunables, and the blade-height bounds used for cull-safe chunk AABBs. All
    /// render tunables live here — no magic numbers in the runtime systems.
    ///
    /// Placement (field bounds, density, scatter count, seed, ground snap) lives on <see cref="GrassLayer"/>,
    /// which references one of these. One config may be shared by several layers.
    /// </summary>
    [CreateAssetMenu(menuName = "GrassInteract/Grass LOD Config", fileName = "GrassLODConfig")]
    public sealed class GrassLODConfig : ScriptableObject
    {
        [Header("Rendering")]
        [SerializeField] private Material? grassMaterial;

        [Tooltip("LOD0 (highest detail) first. Length MUST equal LodMaxDistances.Length + 1.")]
        [SerializeField] private Mesh[] lodMeshes = Array.Empty<Mesh>();

        [Tooltip("Ascending camera distances (metres). Entry i = the maximum distance at which LOD i is " +
                 "still used. Length MUST equal LodMeshes.Length - 1. Must be strictly ascending and > 0.")]
        [SerializeField] private float[] lodMaxDistances = Array.Empty<float>();

        [Tooltip("Shadow casting for the grass. Requires the shader's ShadowCaster pass (included). " +
                 "Off is recommended for dense mobile grass.")]
        [SerializeField] private ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;

        [Header("Wind")]
        [Tooltip("Ambient wind direction in the XZ plane (auto-normalized when bound).")]
        [SerializeField] private Vector2 windDirection = new Vector2(1f, 0f);

        [Range(0f, 2f)]
        [Tooltip("Horizontal sway amplitude at the blade tip, in metres. Scales by height so roots stay put.")]
        [SerializeField] private float windStrength = 0.15f;

        [Range(0f, 5f)]
        [Tooltip("Sway oscillation speed (cycles scale with _Time).")]
        [SerializeField] private float windFrequency = 1.2f;

        [Range(0.01f, 2f)]
        [Tooltip("Spatial noise scale — how quickly the gust phase varies across the field (per-blade phase).")]
        [SerializeField] private float windNoiseScale = 0.25f;

        [Header("Trample (lean-away deform)")]
        [Range(0f, 4f)]
        [Tooltip("Lateral lean amplitude at the blade tip, in metres, at full trample magnitude. The blade " +
                 "leans AWAY from the interactor — direction + magnitude come from the trample vector map " +
                 "(GrassTrampleMap). Keep near the footprint scale so the lean stays contained. 0 = no lean.")]
        [SerializeField] private float bendStrength = 0.7f;

        [Range(0f, 1f)]
        [Tooltip("Height loss at full trample magnitude (0..1). Mats the trampled core straight DOWN (no " +
                 "gradient needed), so the most-trampled blades read as pressed flat, not merely leaned.")]
        [SerializeField] private float flatten = 0.5f;

        [Header("Bounds (culling safety)")]
        [Tooltip("Unscaled height (metres) of the LOD0 blade mesh. The Tools ▸ GrassInteract ▸ Build " +
                 "Blade Meshes generator produces 1 m blades. Update this if you swap in taller meshes.")]
        [Min(0.01f)] [SerializeField] private float maxBladeHeight = 1f;

        [Tooltip("Extra headroom (metres) added to chunk AABBs beyond the scaled blade height, to cover " +
                 "wind/trample deform so bent blades are never frustum-culled. Also applied as lateral padding.")]
        [Min(0f)] [SerializeField] private float bendHeadroom = 1f;

        public Material? GrassMaterial => this.grassMaterial;
        public Mesh[] LodMeshes => this.lodMeshes;
        public float[] LodMaxDistances => this.lodMaxDistances;
        public ShadowCastingMode ShadowCastingMode => this.shadowCastingMode;
        public Vector2 WindDirection => this.windDirection;
        public float WindStrength => this.windStrength;
        public float WindFrequency => this.windFrequency;
        public float WindNoiseScale => this.windNoiseScale;
        public float BendStrength => this.bendStrength;
        public float Flatten => this.flatten;
        public float MaxBladeHeight => this.maxBladeHeight;
        public float BendHeadroom => this.bendHeadroom;

        /// <summary>Validates the render config. Returns false + a human-readable reason on first problem.</summary>
        public bool Validate(out string error)
        {
            if (this.grassMaterial == null)
            {
                error = "GrassMaterial is not assigned.";
                return false;
            }

            if (this.lodMeshes == null || this.lodMeshes.Length == 0)
            {
                error = "LodMeshes is empty — assign at least one LOD mesh.";
                return false;
            }

            for (int i = 0; i < this.lodMeshes.Length; ++i)
            {
                if (this.lodMeshes[i] == null)
                {
                    error = $"LodMeshes[{i}] is null.";
                    return false;
                }
            }

            int expectedThresholds = this.lodMeshes.Length - 1;
            int actualThresholds = this.lodMaxDistances?.Length ?? 0;
            if (actualThresholds != expectedThresholds)
            {
                error = $"LodMaxDistances length ({actualThresholds}) must equal LodMeshes.Length - 1 " +
                        $"({expectedThresholds}).";
                return false;
            }

            for (int i = 0; i < actualThresholds; ++i)
            {
                if (this.lodMaxDistances![i] <= 0f)
                {
                    error = $"LodMaxDistances[{i}] must be > 0.";
                    return false;
                }
                if (i > 0 && this.lodMaxDistances[i] <= this.lodMaxDistances[i - 1])
                {
                    error = "LodMaxDistances must be strictly ascending (equal thresholds disable a LOD).";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
