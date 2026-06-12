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
    /// WorldPainter partial — multi-tile GPU render submit logic.
    ///
    /// Mirrors <see cref="GpuTerrainRenderer"/> exactly:
    ///   - Per-tile <see cref="GpuTerrainEngine"/> + <see cref="TerrainTileGpuResources"/> lists.
    ///   - <see cref="TryBuild"/> builds engines on demand; <see cref="DisposeEngines"/> tears down.
    ///   - OnEnable / OnDisable wire the RenderPipeline edit-mode hook.
    ///   - Exposes internal seam accessors consumed by <c>WorldPainterSculptTool</c>.
    ///
    /// This file ships in the runtime assembly — NO UnityEditor symbols (those live
    /// inside #if UNITY_EDITOR blocks only).
    /// </summary>
    public sealed partial class WorldPainter
    {
        // ── Inspector infra ───────────────────────────────────────────────────

        [HideInInspector] [SerializeField] private ComputeShader? cullCompute   = null;
        [HideInInspector] [SerializeField] private Material?      patchMaterial = null;

        [SerializeField]
        [Tooltip("LOD range distances in metres, index 0 = finest.")]
        private float[] lodRangesM = new float[] { 32f, 64f, 128f, 256f };

        // ── Runtime engine state ──────────────────────────────────────────────

        private readonly List<GpuTerrainEngine>        engines      = new();
        private readonly List<TerrainTileGpuResources> gpuResources = new();
        private readonly Dictionary<Vector2Int, int>   coordToIndex = new();

        /// <summary>True once <see cref="TryBuild"/> has succeeded with ≥1 tile.</summary>
        internal bool IsBuilt { get; private set; }

        // ── Internal seam accessors (for WorldPainterSculptTool) ──────────────

        internal GpuTerrainEngine? EngineForCoord(Vector2Int coord)
            => this.coordToIndex.TryGetValue(coord, out int idx) ? this.engines[idx] : null;

        internal TerrainTileGpuResources? ResourcesForCoord(Vector2Int coord)
            => this.coordToIndex.TryGetValue(coord, out int idx) ? this.gpuResources[idx] : null;

        internal void BeginSculptPreview(Vector2Int coord, RenderTexture rt)
            => this.EngineForCoord(coord)?.BeginSculptPreview(rt);

        internal void EndSculptPreview(Vector2Int coord)
            => this.EngineForCoord(coord)?.EndSculptPreview();

        internal void CommitHeight(Vector2Int coord)
            => this.EngineForCoord(coord)?.EndSculptPreview();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable()
        {
            this.TryBuild();
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering += this.OnBeginCameraRenderingEdit;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            RenderPipelineManager.beginCameraRendering -= this.OnBeginCameraRenderingEdit;
#endif
            this.DisposeEngines();
        }

#if UNITY_EDITOR
        private void OnBeginCameraRenderingEdit(ScriptableRenderContext ctx, Camera cam)
        {
            if (Application.isPlaying) return;
            if (!this.IsBuilt) this.TryBuild();
            this.SubmitTerrain(cam);
        }

        [ContextMenu("Rebuild")]
        private void Rebuild() => this.TryBuild();
#endif

        // ── Build / Dispose ───────────────────────────────────────────────────

        internal void TryBuild()
        {
            this.DisposeEngines();

            if (!this.ResolveInfra()) return;
            if (this.tiles == null) return;

            for (int i = 0; i < this.tiles.Count; i++)
                this.BuildOneTile(i);

            this.IsBuilt = this.engines.Count > 0;
            if (this.IsBuilt)
                Debug.Log($"[WorldPainter] Built {this.engines.Count} tile(s).");
        }

        private bool ResolveInfra()
        {
#if UNITY_EDITOR
            if (this.cullCompute == null)
                this.cullCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                    "Assets/WorldPainter/Shaders/TerrainNodeCull.compute");
            if (this.patchMaterial == null)
                this.patchMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/WorldPainter/Materials/TerrainPatch.mat");
#endif
            if (this.cullCompute == null || this.patchMaterial == null)
            {
                Debug.LogWarning("[WorldPainter] cullCompute or patchMaterial unresolved — " +
                    "assign manually or ensure assets exist at expected paths.");
                return false;
            }
            return true;
        }

        private void BuildOneTile(int tileIndex)
        {
            var entry = this.tiles![tileIndex];
            var tile  = entry.tileAsset;
            if (tile == null) return;

            if (!tile.IsHeightValid)
            {
                Debug.LogWarning($"[WorldPainter] Tile[{tileIndex}] '{tile.name}' " +
                    "heightData invalid — skipping.");
                return;
            }

            if (this.coordToIndex.ContainsKey(tile.tileCoord))
            {
                Debug.LogError($"[WorldPainter] Duplicate tileCoord {tile.tileCoord} " +
                    $"on Tile[{tileIndex}] — skipping to avoid coord map collision.");
                return;
            }

            var gpu    = new TerrainTileGpuResources();
            gpu.Upload(tile);

            var engine = new GpuTerrainEngine(this.cullCompute!, this.patchMaterial!);
            engine.Build(tile, gpu, this.lodRangesM);

            bool ok = engine.SelfTest(out string msg);
            Debug.Log($"[WorldPainter] Tile[{tileIndex}] {tile.tileCoord}: {msg}");
            if (!ok)
            {
                Debug.LogError($"[WorldPainter] Tile[{tileIndex}] SelfTest failed — skipped.");
                engine.Dispose();
                gpu.Dispose();
                return;
            }

            int idx = this.engines.Count;
            this.engines.Add(engine);
            this.gpuResources.Add(gpu);
            this.coordToIndex[tile.tileCoord] = idx;

            Debug.Assert(this.engines.Count == this.gpuResources.Count,
                "[WorldPainter] engines/gpuResources list desync after BuildOneTile.");
        }

        private void DisposeEngines()
        {
            Debug.Assert(this.engines.Count == this.gpuResources.Count,
                "[WorldPainter] engines/gpuResources list desync in DisposeEngines.");
            for (int i = 0; i < this.engines.Count; i++)
            {
                this.engines[i].Dispose();
                this.gpuResources[i].Dispose();
            }
            this.engines.Clear();
            this.gpuResources.Clear();
            this.coordToIndex.Clear();
            this.IsBuilt = false;
        }

        // ── Submit ────────────────────────────────────────────────────────────

        private void SubmitTerrain(Camera? cam)
        {
            Vector3 camPos = cam != null ? cam.transform.position :
                             (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            foreach (var engine in this.engines)
                engine.Submit(cam, camPos);
        }
    }
}
