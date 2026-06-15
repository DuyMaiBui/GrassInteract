#nullable enable
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Asset that owns all <see cref="ScatterLayer"/>s, density textures, and
    /// <see cref="BrushStamp"/>s for a terrain-scale scatter field, plus shared GPU resources
    /// (compute shader, indirect material).
    ///
    /// Layers and their density maps are stored as sub-assets. One <c>.asset</c> file = one
    /// complete scatter project. Wind, bend, and render parameters are per-layer (on each
    /// <see cref="ScatterLayer"/> sub-asset directly).
    ///
    /// Create via <c>Assets &gt; Create &gt; WorldPainter &gt; Terrain Scatter Config</c>.
    /// Assign to <see cref="ScatterField.Config"/>; the field drives from <see cref="Layers"/>,
    /// <see cref="CullCompute"/>, and <see cref="IndirectMaterial"/> defined here.
    ///
    /// Note: NO Terrain field here — a Terrain is a scene object and cannot be referenced by a
    /// project asset. The <see cref="ScatterField"/> component keeps the <c>boundTerrain</c>
    /// binding.
    /// </summary>
    [CreateAssetMenu(menuName = "WorldPainter/Terrain Scatter Config", fileName = "TerrainScatterConfig")]
    public sealed class TerrainScatterConfig : ScriptableObject
    {
        // ── GPU Resources ─────────────────────────────────────────────────────

        [TitleGroup("GPU Resources")]
        [Tooltip("The GrassCull compute shader (GrassCull.compute). Required for the GPU indirect tier.")]
        [SerializeField] private ComputeShader? cullCompute;

        [TitleGroup("GPU Resources")]
        [Tooltip("Base material using the WorldPainter/IndirectGrass shader. Required for the GPU tier.")]
        [SerializeField] private Material? indirectMaterial;

        // ── Layers ────────────────────────────────────────────────────────────

        [TabGroup("Main", "Layers")]
        [Tooltip("Ordered list of scatter layers owned by this config. Each layer is built into one " +
                 "engine by the ScatterField that references this config.")]
        [SerializeField] private List<ScatterLayer> layers = new();

        // ── Brushes ───────────────────────────────────────────────────────────

        [TabGroup("Main", "Brushes")]
        [Tooltip("Library of brush stamps available for painting. Stamps are sub-assets of this config.")]
        [SerializeField] private List<BrushStamp> brushStamps = new();

        // ── Public accessors ──────────────────────────────────────────────────

        /// <summary>Read-only view of the scatter layers owned by this config.</summary>
        public IReadOnlyList<ScatterLayer> Layers => this.layers;

        /// <summary>Read-only view of the brush stamps owned by this config.</summary>
        public IReadOnlyList<BrushStamp> BrushStamps => this.brushStamps;

        /// <summary>The GrassCull compute shader. Required for the GPU indirect tier.</summary>
        public ComputeShader? CullCompute => this.cullCompute;

        /// <summary>Base indirect material. Required for the GPU indirect tier.</summary>
        public Material? IndirectMaterial => this.indirectMaterial;

    }
}
