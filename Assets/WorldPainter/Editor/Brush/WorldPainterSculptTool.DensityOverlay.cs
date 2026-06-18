#nullable enable
using System.Collections.Generic;
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

        // Lift the overlay above the surface so it isn't z-fought by the GPU terrain. ZTest Always
        // in the shader already shows it through terrain; the lift just keeps it visually above hills.
        private const float HEATMAP_LIFT = 0.25f; // metres above the sampled surface height

        // Tessellation of the per-tile overlay. The overlay grid CONFORMS to the terrain surface
        // (per-vertex height sample) instead of being one flat quad — a flat quad at a single
        // tile-centre height projects off the sculpted surface (the offset is amplified by a
        // non-identity root scale), so the density colour landed beside the surface-snapped grass.
        internal const int HEATMAP_GRID_CELLS = 24; // cells per tile edge → (N+1)² verts

        private Material? heatmapMat;

        // One conformed mesh PER tile coord. A single shared mesh mutated via SetVertices between
        // immediate DrawMeshNow calls is a Unity footgun — the shared vertex buffer can be clobbered
        // by the next tile's SetVertices before the previous tile's draw flushes, so a multi-tile drag
        // rendered every tile with the LAST tile's geometry (correct for a single tile, wrong the
        // moment the brush straddles two). Per-coord meshes share no buffer, so each tile draws its own.
        private readonly Dictionary<Vector2Int, Mesh> heatmapMeshes = new();
        private Vector3[]? heatmapVerts; // reused conformed vertex build buffer (no per-draw alloc)

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
            this.PruneStaleHeatmapMeshes();

            Matrix4x4 rootMatrix = painter.transform.localToWorldMatrix;
            const float tile = TerrainWorldGrid.TILE_SIZE_M;
            const int   n    = HEATMAP_GRID_CELLS + 1; // verts per tile edge

            this.heatmapVerts ??= new Vector3[n * n];

            foreach (var kv in this.densityRtCache)
            {
                Vector2Int coord = kv.Key;
                if (coord == LEGACY_COORD) continue; // legacy single-map path has no tile rect

                RenderTexture rt = kv.Value.rt;
                if (rt == null) continue;

                // Tessellate the tile rect [origin, origin + TILE_SIZE_M] into an n×n grid CONFORMED
                // to the terrain surface via a per-vertex height sample — the same technique the brush
                // ring uses (TerrainBrushPreview.AppendConformed). The previous single flat quad sat at
                // one tile-centre height, so on sculpted terrain (and amplified by a non-identity root
                // scale) it projected beside the surface-snapped grass; a conformed grid drapes the
                // density on the actual surface where the blades land. Bilinear CPU samples, bounded by
                // touched-tile count × (N+1)² — cheap relative to the per-tile scatter rebuild this
                // overlay already replaces during a drag.
                Vector2 origin    = TerrainWorldGrid.TileOriginWorld(coord); // painting-space corner
                var     tileAsset = this.FindTile(painter, coord);

                for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i)
                {
                    float u  = i / (float)HEATMAP_GRID_CELLS;
                    float v  = j / (float)HEATMAP_GRID_CELLS;
                    float px = origin.x + u * tile;
                    float pz = origin.y + v * tile;
                    float py = 0f;
                    if (tileAsset != null)
                        TerrainHeightSampleCpu.TrySampleWorld(tileAsset, px, pz, out py);
                    this.heatmapVerts[j * n + i] = new Vector3(px, py + HEATMAP_LIFT, pz);
                }

                // Per-coord mesh: no shared vertex buffer across tiles, so a multi-tile drag draws each
                // tile with its OWN geometry (a single shared mesh would render every tile with the last
                // tile's vertices — the cross-tile preview corruption).
                Mesh mesh = this.GetOrCreateHeatmapMesh(coord);
                mesh.SetVertices(this.heatmapVerts);

                // Bind THIS tile's density RT, then SetPass — Graphics.DrawMeshNow renders with the
                // pass state captured at SetPass time, so the texture must be bound BEFORE SetPass.
                // A single SetPass before the loop made every tile draw with the same (stale) texture
                // — all tiles showed one tile's density. Per-tile SetPass binds each tile's own RT.
                this.heatmapMat.SetTexture("_MainTex", rt);
                if (!this.heatmapMat.SetPass(0)) return;

                // Vertices are already in painting space; rootMatrix maps painting → world (applies the
                // WorldPainter root TRS incl. non-identity scale), exactly as the conformed brush ring.
                Graphics.DrawMeshNow(mesh, rootMatrix);
            }
        }

        /// <summary>Returns (or lazily builds) the conformed overlay mesh for one tile coord.</summary>
        private Mesh GetOrCreateHeatmapMesh(Vector2Int coord)
        {
            if (!this.heatmapMeshes.TryGetValue(coord, out Mesh mesh) || mesh == null)
            {
                mesh = BuildHeatmapQuad();
                this.heatmapMeshes[coord] = mesh;
            }
            return mesh;
        }

        /// <summary>
        /// Destroys overlay meshes for coords no longer in the density RT cache (stroke ended / tile
        /// untouched), so the per-coord mesh table tracks the live painted set instead of growing for
        /// the session. Cheap: the cache holds only the few tiles touched this stroke.
        /// </summary>
        private void PruneStaleHeatmapMeshes()
        {
            if (this.heatmapMeshes.Count == 0) return;
            this.staleHeatmapCoords.Clear();
            foreach (var kv in this.heatmapMeshes)
                if (!this.densityRtCache.ContainsKey(kv.Key))
                    this.staleHeatmapCoords.Add(kv.Key);
            foreach (var coord in this.staleHeatmapCoords)
            {
                if (this.heatmapMeshes.TryGetValue(coord, out Mesh mesh) && mesh != null)
                    Object.DestroyImmediate(mesh);
                this.heatmapMeshes.Remove(coord);
            }
        }

        private readonly List<Vector2Int> staleHeatmapCoords = new(); // reused prune scratch buffer

        /// <summary>Releases the lazily-created overlay material + per-coord meshes. Called from <see cref="Disable"/>.</summary>
        private void DisposeDensityOverlay()
        {
            if (this.heatmapMat != null) { Object.DestroyImmediate(this.heatmapMat); this.heatmapMat = null; }
            foreach (var kv in this.heatmapMeshes)
                if (kv.Value != null) Object.DestroyImmediate(kv.Value);
            this.heatmapMeshes.Clear();
        }

        /// <summary>
        /// Tessellated unit grid (1×1) centred on origin, flat on the XZ plane, UVs 0..1 (tile density
        /// UV space). The (<see cref="HEATMAP_GRID_CELLS"/>+1)² vertices are a template: positions are
        /// rewritten per tile at draw time to conform to the terrain surface (see DrawDensityOverlay);
        /// only the UVs + triangle topology are reused. The heatmap shader is Cull Off, so triangle
        /// winding is irrelevant. internal so tests can assert its geometry.
        /// </summary>
        internal static Mesh BuildHeatmapQuad()
        {
            const int cells = HEATMAP_GRID_CELLS;
            const int n     = cells + 1; // verts per edge

            var verts = new Vector3[n * n];
            var uvs   = new Vector2[n * n];
            for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i)
            {
                float u = i / (float)cells;
                float v = j / (float)cells;
                verts[j * n + i] = new Vector3(u - 0.5f, 0f, v - 0.5f);
                uvs[j * n + i]   = new Vector2(u, v);
            }

            var tris = new int[cells * cells * 6];
            int t = 0;
            for (int j = 0; j < cells; ++j)
            for (int i = 0; i < cells; ++i)
            {
                int a = j * n + i;
                int b = a + 1;
                int c = a + n;
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }

            var mesh = new Mesh { name = "WorldPainterDensityHeatmapGrid", hideFlags = HideFlags.HideAndDontSave };
            if (verts.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            return mesh;
        }
    }
}
