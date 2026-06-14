#nullable enable
using System;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// Per-tile density texture entry for a <see cref="GrassLayer"/>. Keyed by tile grid coordinate.
    /// </summary>
    [Serializable]
    public struct TileDensityTexture
    {
        [Tooltip("Tile grid coordinate this density texture belongs to.")]
        public Vector2Int coord;

        [Tooltip("R8/RGBA32 density Texture2D sub-asset for this tile. Null if not yet created.")]
        public Texture2D? tex;
    }

    /// <summary>
    /// Unified GRASS authoring layer. Holds ONE shared scatter config (mesh/material/wind/bend/
    /// bounds/placement) plus a per-tile density texture array. Each tile has its own R8 density
    /// channel painted independently; all tiles share the layer's single material and LODs.
    ///
    /// Composes the same SSOT config structs as the unified surface layers so engines read
    /// through the identical accessors.
    /// </summary>
    public sealed class GrassLayer : WorldPainterLayer
    {
        // ── Shared config (SSOT structs — authored once for all tiles) ────────

        [SerializeField] private ScatterRenderConfig    render;
        [SerializeField] private ScatterWindConfig      wind;
        [SerializeField] private ScatterDeformConfig    deform;
        [SerializeField] private ScatterBoundsConfig    bounds;
        [SerializeField] private ScatterPlacementConfig placement;

        // ── Shared placement params ───────────────────────────────────────────

        [Tooltip("World-space XZ size of the field.")]
        [SerializeField] private Vector2 fieldBounds = new Vector2(100f, 100f);

        [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        [SerializeField] private int seed = 0;

        [Tooltip("Placement allowed only when ground slope (deg) is within [x, y].")]
        [SerializeField] private Vector2 slopeRange = new Vector2(0f, 90f);

        [Min(0f)]
        [Tooltip("Target candidate density in instances per 1×1 m². " +
                 "Per-tile candidate count = density × tile area (TILE_SIZE_M²). " +
                 "Default 0.76 ≈ the legacy 50 000 candidates per 256×256 tile.")]
        [SerializeField] private float targetDensityPerSqM = 0.76f;

        [Tooltip("Per-layer uniform rotation offset applied to every instance.")]
        [SerializeField] private Vector3 rotationOffsetEuler = Vector3.zero;

        [Tooltip("Per-instance random pitch (X-axis) range in degrees.")]
        [SerializeField] private Vector2 randomPitchRange = Vector2.zero;

        [Tooltip("Per-instance random roll (Z-axis) range in degrees.")]
        [SerializeField] private Vector2 randomRollRange = Vector2.zero;

        [Tooltip("Align instance up-axis to terrain/surface normal.")]
        [SerializeField] private bool alignToNormal = false;

        // ── Per-tile density textures ──────────────────────────────────────────

        [Tooltip("Per-tile density textures — one entry per registered tile. " +
                 "Created/assigned by the editor lifecycle; empty = layer has no tiles yet (valid, renders nothing).")]
        [SerializeField] private TileDensityTexture[] densityTiles = Array.Empty<TileDensityTexture>();

        // ── Identity ───────────────────────────────────────────────────────────

        public override LayerKind Kind => LayerKind.Grass;
        public override int PaletteCount => this.densityTiles.Length;

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

        /// <summary>Authored density in instances per 1×1 m². Multiplied by tile area at scatter time.</summary>
        public float TargetDensityPerSqM => this.targetDensityPerSqM;
        public Vector3 RotationOffsetEuler => this.rotationOffsetEuler;
        public Vector2 RandomPitchRange => this.randomPitchRange;
        public Vector2 RandomRollRange => this.randomRollRange;
        public bool AlignToNormal => this.alignToNormal;

        public bool IsOriented =>
            this.alignToNormal
            || this.randomPitchRange != Vector2.zero
            || this.randomRollRange != Vector2.zero;

        // ── Per-tile density access ────────────────────────────────────────────

        /// <summary>
        /// Returns the density <see cref="Texture2D"/> for the given tile coordinate,
        /// or null if no entry exists for that coord.
        /// </summary>
        public Texture2D? GetTileDensity(Vector2Int coord)
        {
            var tiles = this.densityTiles;
            if (tiles == null) return null;
            for (int i = 0; i < tiles.Length; ++i)
                if (tiles[i].coord == coord) return tiles[i].tex;
            return null;
        }

        // ── Editor authoring accessors ─────────────────────────────────────────

        /// <summary>The mutable densityTiles array. Editor lifecycle only.</summary>
        internal TileDensityTexture[] EditorTileDensities => this.densityTiles;

        /// <summary>Replaces the densityTiles array. Editor lifecycle only.</summary>
        internal void EditorSetTileDensities(TileDensityTexture[] tiles) => this.densityTiles = tiles;

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

        internal void EditorSetRender(ScatterRenderConfig cfg)          => this.render    = cfg;
        internal void EditorSetWind(ScatterWindConfig cfg)              => this.wind      = cfg;
        internal void EditorSetDeform(ScatterDeformConfig cfg)          => this.deform    = cfg;
        internal void EditorSetBounds(ScatterBoundsConfig cfg)          => this.bounds    = cfg;
        internal void EditorSetPlacement(ScatterPlacementConfig cfg)    => this.placement = cfg;
    }
}
