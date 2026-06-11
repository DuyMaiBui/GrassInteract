#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// Bookkeeping for the set of currently-resident tiles.
    ///
    /// Stores coord → ResidentTile (asset + GPU resources + rendering engine).
    /// Provides Diff(desired) → (toLoad, toEvict) in one call.
    ///
    /// Double-buffered desired set (mirrors InstanceVisibilityColliderDriver):
    ///   - Read: stable snapshot from last committed round.
    ///   - Write: only committed atomically in CommitDesired().
    ///   - Double-load guard: Add() is a no-op if the coord is already resident.
    ///   - Evict calls Dispose on GPU resources — no leak.
    /// </summary>
    public sealed class TerrainTileResidencySet
    {
        // ── Resident tile record ──────────────────────────────────────────────

        public sealed class ResidentTile
        {
            public readonly TerrainTileAsset      Asset;
            public readonly TerrainTileGpuResources GpuResources;
            public readonly ITerrainEngine        Engine;

            public ResidentTile(
                TerrainTileAsset asset,
                TerrainTileGpuResources gpuResources,
                ITerrainEngine engine)
            {
                this.Asset       = asset       ?? throw new ArgumentNullException(nameof(asset));
                this.GpuResources = gpuResources ?? throw new ArgumentNullException(nameof(gpuResources));
                this.Engine      = engine       ?? throw new ArgumentNullException(nameof(engine));
            }
        }

        // ── State ─────────────────────────────────────────────────────────────

        private readonly Dictionary<Vector2Int, ResidentTile> residents =
            new Dictionary<Vector2Int, ResidentTile>();

        // Scratch lists (reused each Diff call — no per-frame alloc)
        private readonly List<Vector2Int> scratchLoad  = new List<Vector2Int>(32);
        private readonly List<Vector2Int> scratchEvict = new List<Vector2Int>(32);

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>Number of currently-resident tiles.</summary>
        public int Count => this.residents.Count;

        /// <summary>All resident tile coordinates (live enumerable — do not mutate during iteration).</summary>
        public IEnumerable<Vector2Int> Coords => this.residents.Keys;

        /// <summary>Returns true if the coord is currently resident.</summary>
        public bool Contains(Vector2Int coord) => this.residents.ContainsKey(coord);

        /// <summary>Returns the resident tile or null.</summary>
        public ResidentTile? Get(Vector2Int coord) =>
            this.residents.TryGetValue(coord, out var tile) ? tile : null;

        // ── Diff ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes which coords to load (in desired but not resident) and
        /// which to evict (resident but not in desired).
        /// Results are written into the provided lists (cleared first).
        /// Does NOT mutate resident state — caller drives Add/Evict.
        /// </summary>
        public void Diff(
            HashSet<Vector2Int> desired,
            List<Vector2Int>    toLoad,
            List<Vector2Int>    toEvict)
        {
            toLoad.Clear();
            toEvict.Clear();

            // toLoad: desired but not yet resident
            foreach (Vector2Int coord in desired)
            {
                if (!this.residents.ContainsKey(coord))
                    toLoad.Add(coord);
            }

            // toEvict: resident but no longer in desired
            this.scratchEvict.Clear();
            foreach (Vector2Int coord in this.residents.Keys)
            {
                if (!desired.Contains(coord))
                    this.scratchEvict.Add(coord);
            }
            toEvict.AddRange(this.scratchEvict);
        }

        // ── Mutate ────────────────────────────────────────────────────────────

        /// <summary>
        /// Add a tile to the resident set.
        /// Double-load guard: if coord is already resident this is a no-op
        /// (returns false); the old entry is NOT disposed or replaced.
        /// </summary>
        public bool Add(Vector2Int coord, ResidentTile tile)
        {
            if (this.residents.ContainsKey(coord))
                return false; // double-load guard

            if (tile == null) throw new ArgumentNullException(nameof(tile));
            this.residents[coord] = tile;
            return true;
        }

        /// <summary>
        /// Evict a tile: disposes GPU resources + engine, removes from resident set.
        /// No-op if coord is not resident.
        /// </summary>
        public void Evict(Vector2Int coord)
        {
            if (!this.residents.TryGetValue(coord, out var tile))
                return;

            this.residents.Remove(coord);

            // Dispose in safe order: engine first (releases graphics buffers),
            // then GPU resources (destroys textures).
            tile.Engine.Dispose();
            tile.GpuResources.Dispose();
        }

        /// <summary>
        /// Evict all resident tiles. Used on shutdown.
        /// </summary>
        public void EvictAll()
        {
            this.scratchLoad.Clear();
            this.scratchLoad.AddRange(this.residents.Keys);
            foreach (Vector2Int coord in this.scratchLoad)
                this.Evict(coord);
        }
    }
}
