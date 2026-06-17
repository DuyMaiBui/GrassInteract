#nullable enable
using WorldPainter;
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// SSOT analytic ray↔terrain-surface intersection in PAINTING space (= WorldPainter root local
    /// space). Shared by the brush stroke (<see cref="WorldPainterSculptTool"/>) and the
    /// click-to-select terrain picker (<see cref="WorldPainterTerrainPicker"/>) so both land on the
    /// SAME visible surface.
    ///
    /// The GPU terrain has no collider in edit mode (it renders from the full-res height texture via
    /// VTF), so a CPU-authoritative two-pass analytic cast is used instead of Physics.Raycast:
    /// intersect a coarse plane at <c>minHeight</c> for an approximate XZ, sample the real surface
    /// height there (<see cref="TerrainHeightSampleCpu"/>), then re-intersect at that height. This is
    /// SSOT with the rendered mesh, unlike the streamed low-res physics collider (which sits metres
    /// off on slopes and is stale right after a sculpt). Physics.Raycast remains the fallback for
    /// non-terrain pickables (scatter-prop colliders) when the ray is not over any tile.
    /// </summary>
    internal static class WorldPainterTerrainRaycast
    {
        /// <summary>
        /// Casts the world-space camera <paramref name="worldRay"/> against <paramref name="painter"/>'s
        /// terrain and returns the contact point in PAINTING space. Returns false on a clean miss.
        /// For an identity root the painting↔world transform is a passthrough — zero behaviour change.
        /// </summary>
        internal static bool TryPick(Ray worldRay, WorldPainter painter, out Vector3 paintingPoint)
        {
            // Convert the world-space scene camera ray to painting space so the analytical terrain
            // methods receive coordinates consistent with TerrainWorldGrid / tile heightmaps.
            Transform root = painter.transform;
            var paintingRay = new Ray(
                root.InverseTransformPoint(worldRay.origin),
                root.InverseTransformDirection(worldRay.direction));

            // Tiles live in the WorldMapAsset (SSOT, P2+); painter.Tiles is the legacy back-compat list.
            if (painter.Map != null && TryMapSurfaceHit(paintingRay, painter.Map, out paintingPoint))
                return true;
            if (TryInlineTilesSurfaceHit(paintingRay, painter, out paintingPoint))
                return true;

            // Physics.Raycast operates in world space — use the original world-space ray and convert
            // the hit point back to painting space.
            if (Physics.Raycast(worldRay, out RaycastHit hit, Mathf.Infinity))
            {
                paintingPoint = root.InverseTransformPoint(hit.point);
                return true;
            }

            paintingPoint = Vector3.zero;
            return false;
        }

        /// <summary>
        /// CPU-authoritative ray↔terrain-surface intersection for the legacy inline
        /// <c>painter.Tiles</c> list (empty under the WorldMapAsset SSOT path). Same two-pass
        /// coarse-plane → CPU height sample → re-intersect as <see cref="TryMapSurfaceHit"/>,
        /// guarding that the coarse XZ actually falls inside the candidate tile.
        /// </summary>
        private static bool TryInlineTilesSurfaceHit(Ray ray, WorldPainter painter, out Vector3 paintingPoint)
        {
            paintingPoint = Vector3.zero;
            foreach (var entry in painter.Tiles)
            {
                var tile = entry.tileAsset;
                if (tile == null) continue;
                // Coarse plane at minHeight (flat zero-filled tiles sit exactly here; the second
                // pass corrects Y on sculpted tiles). midY would float above the camera on a fresh
                // flat tile and make Plane.Raycast miss — see TryMapSurfaceHit for the rationale.
                var coarse = new Plane(Vector3.up, new Vector3(0f, tile.minHeight, 0f));
                if (!coarse.Raycast(ray, out float d0) || d0 <= 0f || d0 >= 1e6f) continue;

                Vector3 approx = ray.GetPoint(d0);
                if (TerrainWorldGrid.WorldToTileCoord(approx.x, approx.z) != tile.tileCoord) continue;

                if (TerrainHeightSampleCpu.TrySampleWorld(tile, approx.x, approx.z, out float surfaceY))
                {
                    var surface = new Plane(Vector3.up, new Vector3(0f, surfaceY, 0f));
                    if (surface.Raycast(ray, out float d1) && d1 > 0f && d1 < 1e6f)
                    {
                        paintingPoint = ray.GetPoint(d1); return true;
                    }
                }

                paintingPoint = approx; return true; // coarse hit when sample failed
            }
            return false;
        }

        /// <summary>
        /// Analytic ray↔terrain-surface intersection for edit mode (no collider on the GPU
        /// terrain). Two-pass: intersect a coarse surface plane to get an approximate XZ,
        /// sample the real surface height there, then re-intersect at that height so the brush
        /// sits on the terrain (exact for flat tiles, close for gentle slopes).
        ///
        /// Uses <c>minHeight</c> for the coarse plane, NOT <c>midY=(min+max)/2</c>. The mid-
        /// point would be above the scene camera for a newly-created flat tile (minHeight=0,
        /// maxHeight=512 ⇒ midY=256), causing <c>Plane.Raycast</c> to miss and returning no
        /// hit, which makes every frame report <c>hasHit=false</c> — disabling both the preview
        /// ring and all sculpt dispatch.
        /// </summary>
        private static bool TryMapSurfaceHit(Ray ray, WorldMapAsset map, out Vector3 paintingPoint)
        {
            paintingPoint = Vector3.zero;
            foreach (var tile in map.EnumerateTiles())
            {
                if (tile == null) continue;
                // Coarse plane at minHeight — the surface for a flat zero-filled tile is exactly
                // here. For sculpted tiles with varying height the second pass corrects the Y.
                var coarse = new Plane(Vector3.up, new Vector3(0f, tile.minHeight, 0f));
                if (!coarse.Raycast(ray, out float d0) || d0 <= 0f || d0 >= 1e6f) continue;

                Vector3    approx = ray.GetPoint(d0);
                Vector2Int coord  = TerrainWorldGrid.WorldToTileCoord(approx.x, approx.z);
                var surfaceTile   = map.GetTile(coord);
                if (surfaceTile != null &&
                    TerrainHeightSampleCpu.TrySampleWorld(surfaceTile, approx.x, approx.z, out float surfaceY))
                {
                    var surface = new Plane(Vector3.up, new Vector3(0f, surfaceY, 0f));
                    if (surface.Raycast(ray, out float d1) && d1 > 0f && d1 < 1e6f)
                    {
                        paintingPoint = ray.GetPoint(d1); return true;
                    }
                }

                paintingPoint = approx; return true; // coarse hit when off-tile / sample failed
            }
            return false;
        }
    }
}
