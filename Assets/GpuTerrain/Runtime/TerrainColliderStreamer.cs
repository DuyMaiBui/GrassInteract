#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// MonoBehaviour: manages a near-tile ring of heightfield colliders.
    ///
    /// Each tick (LateUpdate + editor-mode render callback):
    ///   1. Compute desired near ring (COLLIDER_RING_RADIUS) around the camera.
    ///   2. Diff against live set → enqueue builds / schedule evictions.
    ///   3. Cook up to MAX_COOKS_PER_FRAME new colliders per tick (amortised).
    ///   4. Evict handles that left the ring immediately (no hysteresis for colliders).
    ///
    /// Tile source: optional <see cref="TerrainStreamingManager"/> for streaming mode;
    /// falls back to a tile registry serialized on this component.
    ///
    /// Collider ring is always ≤ the streaming ring so we never try to build a collider
    /// for a tile whose height data is not yet resident.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GpuTerrain/Terrain Collider Streamer")]
    public sealed class TerrainColliderStreamer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Tooltip("Optional: streaming manager that holds the resident tile registry. " +
                 "Leave null to use the tile registry below.")]
        [SerializeField] private TerrainStreamingManager? streamingManager;

        [Tooltip("Fallback tile list used when streamingManager is null (single-tile / demo mode).")]
        [SerializeField] private List<TerrainTileAsset> tileRegistry = new List<TerrainTileAsset>();

        // ── Runtime state ─────────────────────────────────────────────────────

        // Coord → active collider handle.
        private readonly Dictionary<Vector2Int, TerrainColliderProvider.Handle> live =
            new Dictionary<Vector2Int, TerrainColliderProvider.Handle>();

        // Coords pending cook (FIFO; amortised across frames).
        private readonly Queue<Vector2Int> cookQueue = new Queue<Vector2Int>();

        // Tile index built from tileRegistry (fallback mode).
        private readonly Dictionary<Vector2Int, TerrainTileAsset> tileIndex =
            new Dictionary<Vector2Int, TerrainTileAsset>();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.RebuildTileIndex();
        }

        private void OnDisable()
        {
            this.EvictAll();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            Camera? cam = Camera.main;
            if (cam != null) this.Tick(cam.transform.position);
        }

#if UNITY_EDITOR
        private void OnRenderObject()
        {
            if (Application.isPlaying) return;
            Camera? cam = Camera.current;
            if (cam != null) this.Tick(cam.transform.position);
        }
#endif

        // ── Core tick ─────────────────────────────────────────────────────────

        private void Tick(Vector3 camPos)
        {
            // 1. Compute desired near ring.
            var desired = TerrainResidencyRing.ComputeDesired(camPos, TerrainColliderConfig.COLLIDER_RING_RADIUS);

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

                var handle = TerrainColliderProvider.Build(tile, this.transform);
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

        private bool IsPendingCook(Vector2Int coord)
        {
            // O(n) but cookQueue is tiny (≤ 9 entries); acceptable.
            foreach (var c in this.cookQueue)
                if (c == coord) return true;
            return false;
        }

        private TerrainTileAsset? ResolveTile(Vector2Int coord)
        {
            this.tileIndex.TryGetValue(coord, out var tile);
            return tile;
        }

        private void RebuildTileIndex()
        {
            this.tileIndex.Clear();
            foreach (var asset in this.tileRegistry)
                if (asset != null)
                    this.tileIndex[asset.tileCoord] = asset;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Register a tile at runtime (streaming mode: called by TerrainStreamingManager).</summary>
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

        /// <summary>Number of live collider handles (test / inspector helper).</summary>
        public int LiveCount => this.live.Count;

        /// <summary>Number of cooks pending in the queue (test helper).</summary>
        public int PendingCookCount => this.cookQueue.Count;
    }
}
