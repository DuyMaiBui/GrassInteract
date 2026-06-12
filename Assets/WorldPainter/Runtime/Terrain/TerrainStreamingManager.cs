#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldPainter
{
    /// <summary>
    /// [ExecuteAlways] MonoBehaviour: drives the Phase 3 multi-tile streaming loop.
    ///
    /// Per player-loop tick:
    ///   1. Drain async loader callbacks → GPU upload (up to MAX_UPLOADS_PER_FRAME).
    ///   2. Compute desired ring around camera.
    ///   3. Diff(desired) → enqueue loads for missing tiles (within upload budget).
    ///   4. Evict far tiles (beyond RING_RADIUS + HYSTERESIS_TILES).
    ///   5. Submit each resident tile's GpuTerrainEngine.
    ///
    /// Resident tile count is hard-capped at MAX_RESIDENT_TILES.
    /// All GPU upload/dispose is main-thread only.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("WorldPainter/Terrain Streaming Manager")]
    public sealed class TerrainStreamingManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [SerializeField] private ComputeShader?   cullCompute   = null;
        [SerializeField] private Material?        patchMaterial = null;
        [SerializeField] [Tooltip("Tiles to stream (editor-assigned or runtime-registered).")]
        private List<TerrainTileAsset> tileRegistry = new List<TerrainTileAsset>();
        [SerializeField] private float[] lodRangesM = new float[] { 32f, 64f, 128f, 256f };

        // ── Runtime state ──────────────────────────────────────────────────────

        private readonly TerrainTileResidencySet residencySet  = new TerrainTileResidencySet();
        private readonly TerrainTileLoader       loader        = new TerrainTileLoader();
        private readonly List<Vector2Int>        scratchLoad   = new List<Vector2Int>(32);
        private readonly List<Vector2Int>        scratchEvict  = new List<Vector2Int>(32);

        // Tile index (coord → asset) built from tileRegistry.
        private readonly Dictionary<Vector2Int, TerrainTileAsset> tileIndex =
            new Dictionary<Vector2Int, TerrainTileAsset>();

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void OnEnable()
        {
            this.RebuildTileIndex();
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering += this.OnBeginCameraRenderingEdit;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering -= this.OnBeginCameraRenderingEdit;
#endif
            this.residencySet.EvictAll();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            Camera? cam = Camera.main;
            if (cam == null) return;
            this.Tick(cam, cam.transform.position);
        }

#if UNITY_EDITOR
        private void OnBeginCameraRenderingEdit(ScriptableRenderContext ctx, Camera cam)
        {
            if (Application.isPlaying) return;
            this.Tick(cam, cam.transform.position);
        }
#endif

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Number of currently-resident tiles.</summary>
        public int ResidentCount => this.residencySet.Count;

        /// <summary>
        /// Register a tile asset at runtime (e.g. loaded via Addressables).
        /// Adds it to the streaming index so it can be picked up by the next ring diff.
        /// </summary>
        public void RegisterTile(TerrainTileAsset asset)
        {
            if (asset == null) return;
            this.tileIndex[asset.tileCoord] = asset;
        }

        // ── Core tick ─────────────────────────────────────────────────────────

        private void Tick(Camera cam, Vector3 camPos)
        {
            // 1. Drain async loader → main-thread GPU uploads (capped per frame).
            // M2 fix: pass the budget into DrainMainThreadQueue so uploads are capped at
            // the DRAIN site (not just at enqueue time). Previously all queued callbacks ran
            // in one frame, defeating the per-frame budget and reintroducing hitch risk.
            this.loader.DrainMainThreadQueue(TerrainStreamingConfig.MAX_UPLOADS_PER_FRAME);

            // 2. Compute desired ring.
            HashSet<Vector2Int> desired = TerrainResidencyRing.ComputeDesired(camPos);

            // 3. Diff → load/evict lists.
            this.residencySet.Diff(desired, this.scratchLoad, this.scratchEvict);

            // 4. Evict far tiles first (free memory before loading new).
            foreach (Vector2Int coord in this.scratchEvict)
            {
                if (!TerrainResidencyRing.IsWithinEvictThreshold(coord, camPos))
                {
                    this.loader.CancelTile(coord);
                    this.residencySet.Evict(coord);
                }
            }

            // 5. Enqueue loads up to per-frame budget and resident cap.
            int queued = 0;
            foreach (Vector2Int coord in this.scratchLoad)
            {
                if (queued >= TerrainStreamingConfig.MAX_UPLOADS_PER_FRAME)
                    break;
                if (this.residencySet.Count >= TerrainStreamingConfig.MAX_RESIDENT_TILES)
                    break;
                if (!this.tileIndex.TryGetValue(coord, out var asset))
                    continue;

                this.loader.Enqueue(coord, asset, this.OnTileLoaded);
                queued++;
            }

            // 6. Submit all resident tiles.
            Vector3 cp = cam.transform.position;
            foreach (Vector2Int coord in this.residencySet.Coords)
            {
                var tile = this.residencySet.Get(coord);
                tile?.Engine.Submit(cam, cp);
            }
        }

        // ── Load callback (main thread) ───────────────────────────────────────

        private void OnTileLoaded(Vector2Int coord, TerrainTileAsset asset, int generation)
        {
            if (this.cullCompute == null || this.patchMaterial == null) return;
            if (this.residencySet.Contains(coord)) return; // double-load guard

            if (this.residencySet.Count >= TerrainStreamingConfig.MAX_RESIDENT_TILES)
            {
                Debug.LogWarning($"[TerrainStreamingManager] MAX_RESIDENT_TILES " +
                    $"({TerrainStreamingConfig.MAX_RESIDENT_TILES}) reached; skipping tile {coord}.");
                return;
            }

            var gpuRes = new TerrainTileGpuResources();
            gpuRes.Upload(asset);

            var engine = new GpuTerrainEngine(this.cullCompute, this.patchMaterial);
            engine.Build(asset, gpuRes, this.lodRangesM);

            var resident = new TerrainTileResidencySet.ResidentTile(asset, gpuRes, engine);
            this.residencySet.Add(coord, resident);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RebuildTileIndex()
        {
            this.tileIndex.Clear();
            foreach (var asset in this.tileRegistry)
            {
                if (asset != null)
                    this.tileIndex[asset.tileCoord] = asset;
            }
        }
    }
}
