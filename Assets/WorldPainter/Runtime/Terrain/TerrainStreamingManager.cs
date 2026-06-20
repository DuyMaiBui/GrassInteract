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
        private readonly HashSet<Vector2Int>     desiredScratch = new HashSet<Vector2Int>();

        // Cached method-group delegate — avoids a fresh delegate allocation per Enqueue call.
        private System.Action<Vector2Int, TerrainTileAsset, int>? onTileLoaded;

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

        /// <summary>
        /// P8: Register all tiles referenced by <paramref name="manifest"/> into the
        /// streaming index using Editor <c>AssetDatabase</c> loads (in-Editor path) or the
        /// already-loaded <see cref="tileRegistry"/> entries (runtime path).
        ///
        /// In player builds, tiles must be pre-loaded (e.g. via Addressables) and
        /// registered individually via <see cref="RegisterTile"/> before this is called.
        /// This method handles the Editor-Play shortcut: it loads each baked standalone
        /// asset by path via AssetDatabase and registers it directly.
        ///
        /// Safe to call multiple times — already-registered coords are overwritten with
        /// the same reference (idempotent).
        /// </summary>
        /// <param name="manifest">Bake manifest produced by <c>WorldMapBaker.BakeAll</c>.</param>
        public void RegisterFromManifest(TileBakeManifest manifest)
        {
            if (manifest == null) return;

            int registered = 0;
            foreach (var entry in manifest.Entries)
            {
                if (string.IsNullOrEmpty(entry.assetPath)) continue;

                // In Editor (including Editor Play): load the asset directly.
                // In player builds: asset must already be in tileRegistry (pre-loaded).
                TerrainTileAsset? asset = this.ResolveManifestEntry(entry.assetPath);
                if (asset == null)
                {
                    WpLog.Warning($"[TerrainStreamingManager] Manifest entry {entry.coord} " +
                        $"points to '{entry.assetPath}' but asset could not be resolved. " +
                        "Pre-load baked tiles via Addressables in player builds.");
                    continue;
                }

                this.tileIndex[entry.coord] = asset;
                registered++;
            }

            WpLog.Log($"[TerrainStreamingManager] Registered {registered} tile(s) from manifest.");
        }

        /// <summary>
        /// Resolves a manifest asset path to a <see cref="TerrainTileAsset"/> reference.
        /// Editor path: AssetDatabase load. Runtime path: search the inspector tileRegistry.
        /// </summary>
        private TerrainTileAsset? ResolveManifestEntry(string assetPath)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainTileAsset>(assetPath);
#else
            // In player builds, the caller must pre-load baked tiles and push them into
            // tileRegistry (or call RegisterTile individually). We search the registry
            // as a best-effort fallback by path-derived name.
            foreach (var asset in this.tileRegistry)
            {
                if (asset != null && asset.name == System.IO.Path.GetFileNameWithoutExtension(assetPath))
                    return asset;
            }
            return null;
#endif
        }

        // ── Core tick ─────────────────────────────────────────────────────────

        private void Tick(Camera cam, Vector3 camPos)
        {
            // 1. Drain async loader → main-thread GPU uploads (capped per frame).
            // M2 fix: pass the budget into DrainMainThreadQueue so uploads are capped at
            // the DRAIN site (not just at enqueue time). Previously all queued callbacks ran
            // in one frame, defeating the per-frame budget and reintroducing hitch risk.
            this.loader.DrainMainThreadQueue(TerrainStreamingConfig.MAX_UPLOADS_PER_FRAME);

            // 2. Compute desired ring (into a reused scratch set — no per-tick HashSet alloc).
            TerrainResidencyRing.ComputeDesiredInto(camPos, this.desiredScratch);
            HashSet<Vector2Int> desired = this.desiredScratch;

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

                this.loader.Enqueue(coord, asset, this.onTileLoaded ??= this.OnTileLoaded);
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
                WpLog.Warning($"[TerrainStreamingManager] MAX_RESIDENT_TILES " +
                    $"({TerrainStreamingConfig.MAX_RESIDENT_TILES}) reached; skipping tile {coord}.");
                return;
            }

            var gpuRes = new TerrainTileGpuResources();
            gpuRes.Upload(asset);

            var engine = new GpuTerrainEngine(this.cullCompute, this.patchMaterial);
            engine.Build(asset, gpuRes, this.lodRangesM);

            var resident = new TerrainTileResidencySet.ResidentTile(asset, gpuRes, engine);
            if (!this.residencySet.Add(coord, resident))
            {
                // Lost a double-load race — Add is the SSOT guard. Dispose the freshly-built
                // engine + GPU resources we would otherwise leak (GraphicsBuffers, textures,
                // a material clone). Engine first, then GPU resources — matches Evict order.
                engine.Dispose();
                gpuRes.Dispose();
            }
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
