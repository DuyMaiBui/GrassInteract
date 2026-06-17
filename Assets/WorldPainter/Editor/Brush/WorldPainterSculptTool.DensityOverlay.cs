#nullable enable
using WorldPainter;
using UnityEditor;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Density heatmap overlay half of <see cref="WorldPainterSculptTool"/> (partial).
    ///
    /// During a GRASS stroke the blade scatter is deferred to mouse-up (re-scattering every
    /// painted tile each ~15 Hz drain was the large-area lag source — see
    /// <see cref="PreviewActiveScatter"/>). With the blades gone mid-stroke, this overlay is the
    /// live painted-area indicator: one tinted ground quad per touched tile, textured with that
    /// tile's in-progress density RT (<c>densityRtCache</c>, updated by the compute dispatch each
    /// stamp — no flush needed for display).
    ///
    /// Entry point is <see cref="DrawDensityOverlay"/>.
    /// Drawn with <see cref="Graphics.DrawMeshNow"/> during <see cref="EventType.Repaint"/>, in
    /// PAINTING space pre-multiplied by the root TRS (mirrors
    /// <see cref="TerrainBrushPreview"/>'s mask decal — DrawMeshNow ignores Handles.matrix, so the
    /// root transform is baked into the draw matrix). The cache empties on
    /// <see cref="TeardownActiveStroke"/> (mouse-up), so the overlay self-removes when real blades return.
    /// </summary>
    internal sealed partial class WorldPainterSculptTool
    {
        // internal so tests can reference it instead of hardcoding the magic string.
        internal const string HEATMAP_SHADER = "Hidden/WorldPainter/DensityHeatmap";

        // Lift the flat quad above the surface so it isn't z-fought by the GPU terrain. ZTest Always
        // in the shader already shows it through terrain; the lift just keeps it visually above hills.
        private const float HEATMAP_LIFT = 0.25f; // metres above sampled tile-centre height

        private Material? heatmapMat;
        private Mesh?     heatmapQuad;

        /// <summary>
        /// Draws the density heatmap for every touched tile in the active grass stroke. No-op when
        /// the active layer isn't grass, the cache is empty, or the shader isn't imported (graceful
        /// skip — the brush ring still draws, matching <see cref="TerrainBrushPreview"/>'s decal).
        /// Call only during <see cref="EventType.Repaint"/>.
        /// </summary>
        private void DrawDensityOverlay(WorldPainter painter)
        {
            if (WorldPainterState.EffectiveLayerType(painter) != LayerType.Grass) return;
            if (this.densityRtCache.Count == 0) return;

            if (this.heatmapMat == null)
            {
                var shader = Shader.Find(HEATMAP_SHADER);
                if (shader == null) return; // shader not imported → skip overlay, keep the brush ring
                this.heatmapMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (this.heatmapQuad == null) this.heatmapQuad = BuildHeatmapQuad();
            if (!this.heatmapMat.SetPass(0)) return;

            Matrix4x4 rootMatrix = painter.transform.localToWorldMatrix;
            const float tile = TerrainWorldGrid.TILE_SIZE_M;
            const float half = tile * 0.5f;

            foreach (var kv in this.densityRtCache)
            {
                Vector2Int coord = kv.Key;
                if (coord == LEGACY_COORD) continue; // legacy single-map path has no tile rect

                RenderTexture rt = kv.Value.rt;
                if (rt == null) continue;

                // Painting-space tile centre. Tiles span [origin, origin + TILE_SIZE_M] in XZ.
                Vector2 origin = TerrainWorldGrid.TileOriginWorld(coord);
                float cx = origin.x + half;
                float cz = origin.y + half;

                // Flat quad Y = sampled tile-centre height + lift (painting space). Off-tile → 0.
                // Re-sampled per repaint, but bounded by touched-tile count (a few cheap bilinear
                // CPU samples) — not the per-tile scatter cost this change removed; and with ZTest
                // Always the quad shows through terrain regardless, so the Y is display-only.
                float cy = 0f;
                var tileAsset = this.FindTile(painter, coord);
                if (tileAsset != null)
                    TerrainHeightSampleCpu.TrySampleWorld(tileAsset, cx, cz, out cy);

                this.heatmapMat.SetTexture("_MainTex", rt);

                var localMatrix = Matrix4x4.TRS(
                    new Vector3(cx, cy + HEATMAP_LIFT, cz),
                    Quaternion.identity,
                    new Vector3(tile, 1f, tile));
                Graphics.DrawMeshNow(this.heatmapQuad, rootMatrix * localMatrix);
            }
        }

        /// <summary>Releases the lazily-created overlay material + mesh. Called from <see cref="Disable"/>.</summary>
        private void DisposeDensityOverlay()
        {
            if (this.heatmapMat != null)  { Object.DestroyImmediate(this.heatmapMat);  this.heatmapMat = null; }
            if (this.heatmapQuad != null) { Object.DestroyImmediate(this.heatmapQuad); this.heatmapQuad = null; }
        }

        /// <summary>
        /// Unit quad (1×1) centred on origin, flat on the XZ plane, UVs 0..1 (tile density UV space).
        /// internal so tests can assert its geometry.
        /// </summary>
        internal static Mesh BuildHeatmapQuad()
        {
            var mesh = new Mesh { name = "WorldPainterDensityHeatmapQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
            };
            mesh.uv        = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }
    }
}
