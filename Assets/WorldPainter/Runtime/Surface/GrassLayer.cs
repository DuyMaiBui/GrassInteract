#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// One grass appearance variant within a <see cref="GrassLayer"/>. Variants share the layer's
    /// mesh/material/wind/bend config and differ ONLY by texture; each owns its own per-tile R8
    /// density channel (keyed by <c>layerId#index</c>) painted independently.
    /// </summary>
    [Serializable]
    public struct GrassVariant
    {
        [Tooltip("Variant display name (used for the density-channel id and inspector).")]
        public string name;

        [Tooltip("Albedo (_BaseMap) override applied to the shared material for this variant.")]
        public Texture2D? texture;

        [Tooltip("This variant's own field-level R-channel density map (painted independently). " +
                 "Created/assigned by the editor lifecycle; null = variant not yet authored (skipped).")]
        public Texture2D? densityMap;
    }

    /// <summary>
    /// Unified GRASS authoring layer. Holds ONE shared scatter config (mesh/material/wind/bend/
    /// bounds/placement) plus a palette of <see cref="GrassVariant"/> entries. Each variant scatters
    /// by its OWN per-tile R8 density channel via its own frozen-engine instance (built in
    /// <c>WorldPainter.SurfaceLayers</c> through the <c>GrassVariantScatterLayer</c> adapter — Phase 2).
    ///
    /// Composes the same SSOT config structs as <see cref="DensityScatterLayer"/> so engines read
    /// through the identical accessors.
    /// </summary>
    public sealed class GrassLayer : WorldPainterLayer
    {
        // ── Shared config (SSOT structs — authored once for all variants) ──────

        [SerializeField] private ScatterRenderConfig    render;
        [SerializeField] private ScatterWindConfig      wind;
        [SerializeField] private ScatterDeformConfig    deform;
        [SerializeField] private ScatterBoundsConfig    bounds;
        [SerializeField] private ScatterPlacementConfig placement;

        // ── Shared placement params (mirror DensityScatterLayer) ───────────────

        [Tooltip("World-space XZ size of the field.")]
        [SerializeField] private Vector2 fieldBounds = new Vector2(100f, 100f);

        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        [SerializeField] private int seed = 0;

        [Tooltip("Placement allowed only when ground slope (deg) is within [x, y].")]
        [SerializeField] private Vector2 slopeRange = new Vector2(0f, 90f);

        [Min(1)]
        [Tooltip("Candidate instances scattered per variant across the field.")]
        [SerializeField] private int targetInstances = 50000;

        [Tooltip("Per-layer uniform rotation offset applied to every instance.")]
        [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

        [Tooltip("Per-instance random pitch (X-axis) range in degrees.")]
        [SerializeField] private Vector2 randomPitchRange = Vector2.zero;

        [Tooltip("Per-instance random roll (Z-axis) range in degrees.")]
        [SerializeField] private Vector2 randomRollRange = Vector2.zero;

        [Tooltip("Align instance up-axis to terrain/surface normal.")]
        [SerializeField] private bool alignToNormal = false;

        // ── Palette ────────────────────────────────────────────────────────────

        [Tooltip("Grass variants. Each owns its own painted density channel; all share the config above.")]
        [SerializeField] private GrassVariant[] palette = Array.Empty<GrassVariant>();

        // ── Identity ───────────────────────────────────────────────────────────

        public override LayerKind Kind => LayerKind.Grass;
        public override int PaletteCount => this.palette.Length;

        // ── Shared-config accessors ────────────────────────────────────────────

        public ScatterRenderConfig    Render    => this.render;
        public ScatterWindConfig      Wind      => this.wind;
        public ScatterDeformConfig    Deform    => this.deform;
        public ScatterBoundsConfig    Bounds    => this.bounds;
        public ScatterPlacementConfig Placement => this.placement;

        // ── Shared placement accessors ─────────────────────────────────────────

        public Vector2 FieldBounds => this.fieldBounds;
        public Vector2 ScaleRange => this.scaleRange;
        public int Seed => this.seed;
        public Vector2 SlopeRange => this.slopeRange;
        public int TargetInstances => this.targetInstances;
        public Vector3 RotationOffsetEuler => this.rotationOffsetEuler;
        public Vector2 RandomPitchRange => this.randomPitchRange;
        public Vector2 RandomRollRange => this.randomRollRange;
        public bool AlignToNormal => this.alignToNormal;

        public bool IsOriented =>
            this.alignToNormal
            || this.randomPitchRange != Vector2.zero
            || this.randomRollRange != Vector2.zero;

        // ── Palette access ─────────────────────────────────────────────────────

        /// <summary>Read-only palette of grass variants.</summary>
        public IReadOnlyList<GrassVariant> Palette => this.palette;

        // ── Editor authoring setters (used by WorldMapAssetLifecycle) ──────────

        /// <summary>The mutable palette array. Editor lifecycle only.</summary>
        internal GrassVariant[] EditorPalette => this.palette;

        /// <summary>Replaces the palette. Editor lifecycle only.</summary>
        internal void EditorSetPalette(GrassVariant[] variants) => this.palette = variants;

        /// <summary>Sets the shared render material (keeps LODs/shadow/cull). Editor lifecycle only.</summary>
        internal void EditorSetMaterial(Material material)
        {
            this.render = new ScatterRenderConfig(
                material, this.render.ShadowCastingMode, this.render.Lods, this.render.RenderCullDistance);
        }

        /// <summary>Sets the shared LOD meshes (keeps material/shadow/cull). Editor lifecycle only.</summary>
        internal void EditorSetLods(ScatterLod[] lods)
        {
            this.render = new ScatterRenderConfig(
                this.render.Material, this.render.ShadowCastingMode, lods, this.render.RenderCullDistance);
        }
    }
}
