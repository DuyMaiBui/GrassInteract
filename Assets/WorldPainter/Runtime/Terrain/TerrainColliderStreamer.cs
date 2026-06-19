#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// MonoBehaviour: heightfield collider generation for terrain tiles — zero-wiring.
    ///
    /// Self-wiring sibling of <see cref="WorldPainter"/> (<c>[RequireComponent]</c>): tiles, camera,
    /// and collider range all resolve from the sibling. No manual tile-source assignment.
    ///
    /// Two generation paths:
    ///   • RUNTIME (play mode): a near-tile ring streams around the main camera every LateUpdate —
    ///     missing colliders are cooked on demand as tiles enter range and evicted as they leave.
    ///     Each runtime tick derives the desired ring from <c>WorldPainter.lodRangesM[colliderLodBand]</c>
    ///     via <see cref="TerrainColliderRing"/>, diffs against the live set, cooks up to
    ///     <see cref="TerrainColliderConfig.MAX_COOKS_PER_FRAME"/> per frame, and evicts tiles that left.
    ///   • EDIT MODE: no automatic streaming. Cook colliders manually via the inspector
    ///     "Generate Colliders (All Tiles)" button (<see cref="GenerateAllColliders"/>); remove them
    ///     with "Clear Colliders" (<see cref="ClearColliders"/>).
    ///
    /// Tile resolution order: runtime-registration override → sibling <c>WorldPainter.Map</c> (P2) →
    /// legacy inline <c>WorldPainter.Tiles</c> (linear scan).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(WorldPainter))]
    [AddComponentMenu("WorldPainter/Terrain Collider Streamer")]
    public sealed class TerrainColliderStreamer : MonoBehaviour
    {
        // ── Defaults ──────────────────────────────────────────────────────────

        private const int DEFAULT_COLLIDER_LOD_BAND = 3;  // 256 m ≈ classic 3×3 ring
        private const int DEFAULT_HEIGHTFIELD_RES   = 65; // 2^6 + 1

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Collision")]
        [Tooltip("Cook terrain colliders around the camera. Off → all live colliders are evicted.")]
        [SerializeField] private bool generateColliders = true;

        [Tooltip("Which WorldPainter LOD band sets collider range. 3 (256 m) ≈ the classic 3×3 ring. " +
                 "Clamps into the sibling's configured band count.")]
        [SerializeField] private int colliderLodBand = DEFAULT_COLLIDER_LOD_BAND;

        [Tooltip("Heightfield collider resolution per tile edge (must be 2^n + 1; auto-rounded up if not).")]
        [SerializeField] private int heightfieldRes = DEFAULT_HEIGHTFIELD_RES;

        [Tooltip("Unity layer assigned to cooked terrain-collider host GameObjects so physics layer " +
                 "masks (collision matrix / raycast masks) can include or exclude terrain. Applied when " +
                 "a collider is cooked and re-applied to live hosts when changed. Pick via the inspector " +
                 "layer dropdown.")]
        [SerializeField] private int colliderLayer; // 0 = Default layer; set in the custom editor's LayerField

        [Tooltip("Debug: show cooked collider hosts in the Hierarchy (selectable, still never saved). " +
                 "Off keeps them hidden. For visualizing collision geometry use Window ▸ Analysis ▸ Physics Debugger.")]
        [SerializeField] private bool debugShowColliders;

        // ── Runtime state ─────────────────────────────────────────────────────

        // Sibling WorldPainter (RequireComponent guarantees it exists; cached in OnEnable).
        private WorldPainter? worldPainter;

        // Coord → active collider handle.
        private readonly Dictionary<Vector2Int, TerrainColliderProvider.Handle> live =
            new Dictionary<Vector2Int, TerrainColliderProvider.Handle>();

        // Coords pending cook (FIFO; amortised across frames).
        private readonly Queue<Vector2Int> cookQueue = new Queue<Vector2Int>();

        // Runtime-registration override store (tier 1): RegisterTile / UnregisterTile.
        private readonly Dictionary<Vector2Int, TerrainTileAsset> tileIndex =
            new Dictionary<Vector2Int, TerrainTileAsset>();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.worldPainter = this.GetComponent<WorldPainter>();

            // Validate the heightfield resolution up front (TerrainData.heightmapResolution throws
            // if it is not 2^n+1) and write the corrected value back so the inspector never drifts.
            int valid = TerrainColliderProvider.NearestValidHeightfieldRes(this.heightfieldRes);
            if (valid != this.heightfieldRes)
            {
                this.heightfieldRes = valid;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }

            // A domain reload resets `live` (managed state) but cooked hosts can survive — purge any
            // untracked orphans so a tile that already has a (now-untracked) collider is not re-cooked.
            this.PurgeOrphanColliders();
        }

        private void OnDisable()
        {
            this.EvictAll();
        }

        // Inspector edits (incl. play-mode): clamp the layer into [0,31] and re-stamp any live hosts
        // so a layer change applies to already-cooked colliders, not just future ones.
        private void OnValidate()
        {
            this.colliderLayer = Mathf.Clamp(this.colliderLayer, 0, 31);
            this.ApplyLayerToLive();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            Camera? cam = Camera.main;
            if (cam != null) this.Tick(cam.transform.position);
        }

        // ── Core tick ─────────────────────────────────────────────────────────

        private void Tick(Vector3 camPos)
        {
            // Guard: a bare instance (AddComponent race / legacy serialized object without the
            // sibling) must no-op rather than NRE.
            if (this.worldPainter == null) return;

            // 1. Compute desired ring from the chosen LOD band (off → evict everything).
            if (!this.generateColliders)
            {
                this.EvictAll();
                return;
            }
            float range = this.worldPainter.LodRangeMetres(this.colliderLodBand);

            // Convert camera world position to painting space so the ring picks tiles correctly
            // when the WorldPainter root has a non-identity TRS. Identity root → no-op (binder
            // returns the same position unchanged).
            WorldRootBinder binder = this.worldPainter.RootBinder;
            Vector3 camPosPainting = binder.IsIdentity ? camPos : binder.WorldToPainting(camPos);

            var desired = TerrainColliderRing.ComputeDesired(camPosPainting, range);

            // Keep live host visibility in sync with the debug toggle (cheap; ≤ ring size).
            foreach (var handle in this.live.Values)
                handle.SetVisible(this.debugShowColliders);

            // 2. Evict tiles that left the ring.
            var toEvict = new List<Vector2Int>();
            foreach (var coord in this.live.Keys)
                if (!desired.Contains(coord)) toEvict.Add(coord);
            foreach (var coord in toEvict)
                this.Evict(coord);

            // 3. Enqueue new tiles that need colliders.
            foreach (var coord in desired)
            {
                if (!this.live.ContainsKey(coord) && !this.IsPendingCook(coord))
                    this.cookQueue.Enqueue(coord);
            }

            // 4. Cook up to MAX_COOKS_PER_FRAME per tick.
            int cooked = 0;
            while (this.cookQueue.Count > 0 && cooked < TerrainColliderConfig.MAX_COOKS_PER_FRAME)
            {
                Vector2Int coord = this.cookQueue.Dequeue();
                if (this.live.ContainsKey(coord)) continue; // already built by earlier enqueue
                if (!desired.Contains(coord))    continue;  // stale enqueue after camera moved

                TerrainTileAsset? tile = this.ResolveTile(coord);
                if (tile == null) continue;

                var handle = TerrainColliderProvider.Build(tile, this.transform, this.heightfieldRes,
                                                           this.debugShowColliders, this.colliderLayer);
                if (handle != null)
                {
                    this.live[coord] = handle;
                    cooked++;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Evict(Vector2Int coord)
        {
            if (this.live.TryGetValue(coord, out var handle))
            {
                handle.Release();
                this.live.Remove(coord);
            }
        }

        private void EvictAll()
        {
            foreach (var handle in this.live.Values)
                handle.Release();
            this.live.Clear();
            this.cookQueue.Clear();
        }

        /// <summary>Re-stamp the configured collider layer onto every live host (after a layer change).</summary>
        private void ApplyLayerToLive()
        {
            foreach (var handle in this.live.Values)
                handle.SetLayer(this.colliderLayer);
        }

        /// <summary>
        /// Destroy cooked collider hosts parented under this component that are NOT tracked in
        /// <see cref="live"/> — orphans left when a domain reload reset the managed live map but the
        /// host GameObjects (HideAndDontSave) survived. Without this, a reloaded streamer re-cooks a
        /// fresh collider beside the orphan, duplicating it.
        /// </summary>
        private void PurgeOrphanColliders()
        {
            for (int i = this.transform.childCount - 1; i >= 0; --i)
            {
                Transform child = this.transform.GetChild(i);
                if (child == null) continue;
                if (!child.name.StartsWith(TerrainColliderProvider.HOST_PREFIX)) continue;
                if (Application.isPlaying) Object.Destroy(child.gameObject);
                else                       Object.DestroyImmediate(child.gameObject);
            }
        }

        private bool IsPendingCook(Vector2Int coord)
        {
            // O(n) but cookQueue is tiny (≤ ring size); acceptable.
            foreach (var c in this.cookQueue)
                if (c == coord) return true;
            return false;
        }

        /// <summary>
        /// Resolve a tile by coord: runtime override (tier 1) → sibling Map (tier 2, O(1)) →
        /// legacy inline Tiles list (tier 3, linear scan with null-guard).
        /// </summary>
        private TerrainTileAsset? ResolveTile(Vector2Int coord)
        {
            // Tier 1: runtime-registration override.
            if (this.tileIndex.TryGetValue(coord, out var registered))
                return registered;

            if (this.worldPainter == null) return null;

            // Tier 2: WorldMapAsset container (P2 path) — O(1).
            WorldMapAsset? map = this.worldPainter.Map;
            if (map != null)
            {
                TerrainTileAsset? mapped = map.GetTile(coord);
                if (mapped != null) return mapped;
            }

            // Tier 3: legacy inline tiles list — linear scan, null-guard on the asset ref.
            List<TileEntry> tiles = this.worldPainter.Tiles;
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Count; ++i)
                {
                    TerrainTileAsset? asset = tiles[i].tileAsset;
                    if (asset != null && asset.tileCoord == coord)
                        return asset;
                }
            }

            return null;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Register a tile at runtime (override store; e.g. future streaming integration).</summary>
        public void RegisterTile(TerrainTileAsset tile)
        {
            if (tile != null)
                this.tileIndex[tile.tileCoord] = tile;
        }

        /// <summary>Unregister and immediately evict a tile's collider.</summary>
        public void UnregisterTile(Vector2Int coord)
        {
            this.tileIndex.Remove(coord);
            this.Evict(coord);
        }

        /// <summary>
        /// Drop the live collider for <paramref name="coord"/> so the next tick re-cooks it from
        /// the tile's current height data. Call after a sculpt writeback: the streamer otherwise
        /// only (re)builds a collider when a tile ENTERS the ring, so an in-ring tile keeps stale
        /// collision geometry after an edit and physics raycasts land on the pre-sculpt surface.
        /// No-op if the tile has no live collider (it will be cooked fresh on ring entry anyway).
        /// </summary>
        public void InvalidateCollider(Vector2Int coord) => this.Evict(coord);

        /// <summary>
        /// Manually cook a heightfield collider for EVERY known tile (sibling map + legacy inline
        /// list + runtime-registered overrides), skipping tiles that already have a live collider.
        /// Unlike the runtime ring, this ignores camera range and the per-frame cook budget — it is
        /// the "manual create" path driven by the inspector button in edit mode. Returns the number
        /// of colliders newly cooked.
        /// </summary>
        public int GenerateAllColliders()
        {
            this.worldPainter ??= this.GetComponent<WorldPainter>();

            // Gather the distinct coord set across all three tile sources.
            var coords = new HashSet<Vector2Int>();
            foreach (var coord in this.tileIndex.Keys)
                coords.Add(coord);
            WorldMapAsset? map = this.worldPainter != null ? this.worldPainter.Map : null;
            if (map != null)
                foreach (var tile in map.EnumerateTiles())
                    if (tile != null) coords.Add(tile.tileCoord);
            List<TileEntry>? tiles = this.worldPainter != null ? this.worldPainter.Tiles : null;
            if (tiles != null)
                foreach (var entry in tiles)
                    if (entry.tileAsset != null) coords.Add(entry.tileAsset.tileCoord);

            int cooked = 0;
            foreach (var coord in coords)
            {
                if (this.live.ContainsKey(coord)) continue; // already cooked
                TerrainTileAsset? tile = this.ResolveTile(coord);
                if (tile == null) continue;
                var handle = TerrainColliderProvider.Build(tile, this.transform, this.heightfieldRes,
                                                           this.debugShowColliders, this.colliderLayer);
                if (handle != null)
                {
                    this.live[coord] = handle;
                    cooked++;
                }
            }
            return cooked;
        }

        /// <summary>
        /// Unity layer for cooked terrain-collider hosts. Setting it clamps into [0,31], immediately
        /// re-stamps every live host, and applies to all subsequently cooked colliders — the runtime
        /// entry point for changing the collider layer mid-play.
        /// </summary>
        public int ColliderLayer
        {
            get => this.colliderLayer;
            set
            {
                this.colliderLayer = Mathf.Clamp(value, 0, 31);
                this.ApplyLayerToLive();
            }
        }

        /// <summary>Evict all live colliders and clear the pending cook queue (inspector "Clear" button).</summary>
        public void ClearColliders() => this.EvictAll();

        /// <summary>Number of live collider handles (test / inspector helper).</summary>
        public int LiveCount => this.live.Count;

        /// <summary>Number of cooks pending in the queue (test helper).</summary>
        public int PendingCookCount => this.cookQueue.Count;

#if UNITY_EDITOR
        // ── Test seams (EditMode only; internal visible to WorldPainter.Tests) ──

        internal void TickForTest(Vector3 camPos) => this.Tick(camPos);
        internal TerrainTileAsset? ResolveTileForTest(Vector2Int coord) => this.ResolveTile(coord);
        internal bool GenerateCollidersForTest { get => this.generateColliders; set => this.generateColliders = value; }
        internal int  ColliderLodBandForTest   { get => this.colliderLodBand;   set => this.colliderLodBand = value; }
        internal bool DebugShowCollidersForTest { get => this.debugShowColliders; set => this.debugShowColliders = value; }
        internal int  ColliderLayerForTest      { get => this.colliderLayer;      set => this.colliderLayer = value; }
        internal void PurgeOrphansForTest()    => this.PurgeOrphanColliders();
#endif
    }
}
