#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GrassInteract.EditorTools
{
    /// <summary>
    /// CPU spatial hash for fast authored-instance queries in the Scene view.
    ///
    /// Cell size = <see cref="ScatterLayer.ChunkSize"/> (reuses existing baker grid).
    /// Dictionary maps cell ID (x*PRIME ^ z) to a list of authored instance indices.
    ///
    /// P1 scope: Rebuild + Invalidate + QueryRadius (AABB cell walk).
    /// P2 scope: Ray-vs-sphere single-pick (deferred).
    /// </summary>
    internal sealed class InstancePickingService
    {
        // ── Spatial hash ──────────────────────────────────────────────────────

        private readonly Dictionary<long, List<int>> cellMap = new();
        private bool isValid;
        private float cellSize;

        // Positions cache for QueryRadius distance filtering.
        private List<Vector3>? cachedPositions;

        // ── Build ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the spatial hash from the working list of <paramref name="data"/>.
        /// <paramref name="layer"/> supplies the cell size (ChunkSize).
        ///
        /// Call on mouse-up after a Place / Erase stroke (not per-stamp, to avoid O(N) rebuild per tick).
        /// </summary>
        internal void Rebuild(AuthoredInstancesData data, ScatterLayer layer)
        {
            this.cellMap.Clear();
            this.cellSize = Mathf.Max(1f, layer.ChunkSize);

            var list = data.WorkingList;
            this.cachedPositions = new List<Vector3>(list.Count);

            for (int i = 0; i < list.Count; ++i)
            {
                var pos = list[i].position;
                this.cachedPositions.Add(pos);
                long key = this.CellKey(pos);
                if (!this.cellMap.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(4);
                    this.cellMap[key] = bucket;
                }
                bucket.Add(i);
            }

            this.isValid = true;
        }

        // ── Invalidate ────────────────────────────────────────────────────────

        /// <summary>
        /// Marks the hash dirty. The next <see cref="QueryRadius"/> call will return an empty
        /// enumerable until <see cref="Rebuild"/> is called again. Invoke on mouse-up after edits.
        /// </summary>
        internal void Invalidate()
        {
            this.isValid = false;
            this.cellMap.Clear();
            this.cachedPositions = null;
        }

        /// <summary>True when the hash is built and up-to-date.</summary>
        internal bool IsValid => this.isValid;

        // ── QueryRadius ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns indices of all authored instances whose position falls within <paramref name="radius"/>
        /// of <paramref name="center"/> (XZ distance check, Y is ignored for erase brush convenience).
        ///
        /// Uses a cell AABB walk — cheap, but may return indices slightly outside the radius when cells
        /// span the boundary. Callers should verify distance if exact bounds are required.
        /// </summary>
        internal IEnumerable<int> QueryRadius(Vector3 center, float radius)
        {
            if (!this.isValid || this.cachedPositions == null || this.cellSize <= 0f)
                yield break;

            float r2 = radius * radius;
            int minCx = Mathf.FloorToInt((center.x - radius) / this.cellSize);
            int maxCx = Mathf.FloorToInt((center.x + radius) / this.cellSize);
            int minCz = Mathf.FloorToInt((center.z - radius) / this.cellSize);
            int maxCz = Mathf.FloorToInt((center.z + radius) / this.cellSize);

            for (int cx = minCx; cx <= maxCx; ++cx)
            {
                for (int cz = minCz; cz <= maxCz; ++cz)
                {
                    long key = PackCellKey(cx, cz);
                    if (!this.cellMap.TryGetValue(key, out var bucket))
                        continue;

                    foreach (int idx in bucket)
                    {
                        if (idx >= this.cachedPositions.Count) continue;
                        var p = this.cachedPositions[idx];
                        float dx = p.x - center.x;
                        float dz = p.z - center.z;
                        if (dx * dx + dz * dz <= r2)
                            yield return idx;
                    }
                }
            }
        }

        // ── RaycastPick ───────────────────────────────────────────────────────

        /// <summary>
        /// Casts <paramref name="ray"/> against all authored instances and returns the index of the
        /// nearest hit instance, or null if no hit. Uses a naive AABB-cell step along the ray and
        /// ray-vs-sphere tests per candidate instance.
        ///
        /// <paramref name="bestT"/> is updated to the distance of the nearest hit so callers can
        /// do multi-layer picking with a shared bestT budget.
        /// </summary>
        /// <param name="ray">World-space pick ray (e.g. from HandleUtility.GUIPointToWorldRay).</param>
        /// <param name="layer">Layer supplying LodMeshes[0] for sphere radius estimation.</param>
        /// <param name="sidecar">Sidecar holding the authored instance records.</param>
        /// <param name="bestT">In/Out — nearest t so far. Updated when a closer hit is found.</param>
        /// <returns>Index into the working list of the nearest hit instance, or null.</returns>
        internal int? RaycastPick(Ray ray, ScatterLayer layer, AuthoredInstancesData sidecar, ref float bestT)
        {
            if (!this.isValid || this.cachedPositions == null || this.cellSize <= 0f)
                return null;

            // Estimate per-instance sphere radius from mesh bounds.
            float sphereR = this.EstimateSphereRadius(layer);

            var list = sidecar.WorkingList;
            int? bestIdx = null;

            // Walk all occupied cells that could possibly intersect the ray within a broad AABB.
            // For ≤100k instances the naive full-list walk is fast enough (P2 goal: <16 ms).
            // Cell-acceleration is applied: we only visit cells whose AABB the ray intersects.
            foreach (var kvp in this.cellMap)
            {
                long key = kvp.Key;
                int cx = (int)(uint)(key & 0xFFFFFFFF);
                int cz = (int)(uint)((key >> 32) & 0xFFFFFFFF);

                // Cell AABB (unbounded in Y — include a generous Y range).
                float xMin = cx * this.cellSize;
                float xMax = xMin + this.cellSize;
                float zMin = cz * this.cellSize;
                float zMax = zMin + this.cellSize;

                // Expand by sphere radius so we don't miss border instances.
                float expand = sphereR;
                xMin -= expand; xMax += expand;
                zMin -= expand; zMax += expand;

                // Quick XZ slab test: does ray intersect this cell's XZ footprint?
                if (!RayIntersectsXZSlab(ray, xMin, xMax, zMin, zMax))
                    continue;

                foreach (int idx in kvp.Value)
                {
                    if (idx >= list.Count) continue;
                    var rec = list[idx];
                    float r = this.GetInstanceRadius(layer, rec);

                    if (RaySphereIntersect(ray, rec.position, r, out float t))
                    {
                        if (t >= 0f && t < bestT)
                        {
                            bestT    = t;
                            bestIdx  = idx;
                        }
                    }
                }
            }

            return bestIdx;
        }

        // ── Picking helpers ───────────────────────────────────────────────────

        private float EstimateSphereRadius(ScatterLayer layer)
        {
            Mesh[]? meshes = layer.LodMeshes;
            if (meshes == null || meshes.Length == 0) return 1f;
            Mesh m = meshes[0];
            if (m == null) return 1f;
            return m.bounds.extents.magnitude;
        }

        private float GetInstanceRadius(ScatterLayer layer, InstanceRecord rec)
        {
            float baseR = this.EstimateSphereRadius(layer);
            // V2: rec.scale is float (uniform). Use it directly.
            float scaleMax = Mathf.Max(rec.scale, 0.01f);
            return baseR * Mathf.Max(scaleMax, 0.01f);
        }

        /// <summary>
        /// Fast XZ slab test. Returns true if the ray could potentially pass through the
        /// [xMin..xMax] × (-inf..+inf) × [zMin..zMax] column.
        /// </summary>
        private static bool RayIntersectsXZSlab(Ray ray, float xMin, float xMax, float zMin, float zMax)
        {
            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;

            // X slab.
            if (Mathf.Abs(ray.direction.x) > 1e-6f)
            {
                float t1 = (xMin - ray.origin.x) / ray.direction.x;
                float t2 = (xMax - ray.origin.x) / ray.direction.x;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
            }
            else if (ray.origin.x < xMin || ray.origin.x > xMax)
                return false;

            // Z slab.
            if (Mathf.Abs(ray.direction.z) > 1e-6f)
            {
                float t1 = (zMin - ray.origin.z) / ray.direction.z;
                float t2 = (zMax - ray.origin.z) / ray.direction.z;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
            }
            else if (ray.origin.z < zMin || ray.origin.z > zMax)
                return false;

            return tMax >= tMin && tMax >= 0f;
        }

        /// <summary>
        /// Analytic ray-vs-sphere intersection. Returns true and sets <paramref name="t"/> to the
        /// nearest positive hit distance if the ray hits the sphere.
        /// </summary>
        private static bool RaySphereIntersect(Ray ray, Vector3 center, float radius, out float t)
        {
            Vector3 oc = ray.origin - center;
            float b  = Vector3.Dot(oc, ray.direction);
            float c  = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc < 0f)
            {
                t = float.MaxValue;
                return false;
            }
            float sqrtDisc = Mathf.Sqrt(disc);
            float t0 = -b - sqrtDisc;
            float t1 = -b + sqrtDisc;
            t = t0 >= 0f ? t0 : t1;
            return t >= 0f;
        }

        // ── Cell key helpers ──────────────────────────────────────────────────

        private long CellKey(Vector3 worldPos) =>
            PackCellKey(
                Mathf.FloorToInt(worldPos.x / this.cellSize),
                Mathf.FloorToInt(worldPos.z / this.cellSize));

        private static long PackCellKey(int cx, int cz) =>
            ((long)(uint)cx) | ((long)(uint)cz << 32);
    }
}
