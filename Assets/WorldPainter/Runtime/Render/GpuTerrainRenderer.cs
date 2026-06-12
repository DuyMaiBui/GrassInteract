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
    /// [ExecuteAlways] MonoBehaviour: owns one GpuTerrainEngine per tile in the tiles list,
    /// submitting from the player loop.  One shared lodRangesM across all tiles.
    ///
    /// Submit discipline (mirrors GrassRenderer + GrassGpuEngine):
    ///  - Play mode: LateUpdate submits with null camera (renders in all cameras).
    ///  - Edit mode: EditorApplication.update + beginCameraRendering fires per-camera
    ///    so Scene and Game views each get exactly one draw (no N× overdraw).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("WorldPainter/Gpu Terrain Renderer")]
    public sealed class GpuTerrainRenderer : MonoBehaviour
    {
        // ── Inspector fields ──────────────────────────────────────────────────
        [SerializeField] private List<TerrainTileAsset> tiles = new();
        [HideInInspector] [SerializeField] private ComputeShader? cullCompute   = null;
        [HideInInspector] [SerializeField] private Material?      patchMaterial = null;
        [SerializeField] [Tooltip("LOD range distances in metres, index 0 = finest.")]
        private float[] lodRangesM = new float[] { 32f, 64f, 128f, 256f };

        // ── Runtime state ─────────────────────────────────────────────────────
        private readonly List<GpuTerrainEngine>          engines      = new();
        private readonly List<TerrainTileGpuResources>   gpuResources = new();
        private readonly Dictionary<Vector2Int, int>     coordToIndex = new();
        private bool isBuilt;

        // ── Internal seam accessors ───────────────────────────────────────────

        internal IReadOnlyList<TerrainTileAsset> Tiles => this.tiles;

        internal GpuTerrainEngine? EngineForCoord(Vector2Int coord)
            => this.coordToIndex.TryGetValue(coord, out int idx) ? this.engines[idx] : null;

        internal TerrainTileGpuResources? ResourcesForCoord(Vector2Int coord)
            => this.coordToIndex.TryGetValue(coord, out int idx) ? this.gpuResources[idx] : null;

        internal void BeginSculptPreview(Vector2Int coord, RenderTexture rt)
            => this.EngineForCoord(coord)?.BeginSculptPreview(rt);

        internal void EndSculptPreview(Vector2Int coord)
            => this.EngineForCoord(coord)?.EndSculptPreview();

        internal void CommitHeight(Vector2Int coord)
        {
            var engine = this.EngineForCoord(coord);
            engine?.EndSculptPreview(); // rebind Texture2D after same-object reuse Upload
        }

        // ── Unity lifecycle ────────────────────────────────────────────────────

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

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (!this.isBuilt) this.TryBuild();
            this.SubmitForCamera(null);
        }

#if UNITY_EDITOR
        private void OnBeginCameraRenderingEdit(ScriptableRenderContext ctx, Camera cam)
        {
            if (Application.isPlaying) return;
            if (!this.isBuilt) this.TryBuild();
            this.SubmitForCamera(cam);
        }
#endif

        // ── Build / Dispose ───────────────────────────────────────────────────

        private void TryBuild()
        {
            this.DisposeEngines();

            if (!this.ResolveInfra()) return;

            for (int i = 0; i < this.tiles.Count; i++)
                this.BuildOneTile(i);

            this.isBuilt = this.engines.Count > 0;
            if (this.isBuilt)
                Debug.Log($"[GpuTerrainRenderer] Built {this.engines.Count} tile(s).");
        }

        // Returns false (and LogError) when infra is unresolvable.
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
            if (this.cullCompute == null)
            {
                Debug.LogError("[GpuTerrainRenderer] cullCompute is null and could not be " +
                    "auto-resolved from Assets/WorldPainter/Shaders/TerrainNodeCull.compute. " +
                    "Assign it manually or ensure the asset exists.");
                return false;
            }
            if (this.patchMaterial == null)
            {
                Debug.LogError("[GpuTerrainRenderer] patchMaterial is null and could not be " +
                    "auto-resolved from Assets/WorldPainter/Materials/TerrainPatch.mat. " +
                    "Assign it manually or ensure the asset exists.");
                return false;
            }
            return true;
        }

        private void BuildOneTile(int tileIndex)
        {
            var tile = this.tiles[tileIndex];
            if (tile == null) return;
            if (!tile.IsHeightValid)
            {
                Debug.LogWarning($"[GpuTerrainRenderer] Tile[{tileIndex}] '{tile.name}' " +
                    "heightData invalid — skipping.");
                return;
            }

            var gpu = new TerrainTileGpuResources();
            gpu.Upload(tile);

            var engine = new GpuTerrainEngine(this.cullCompute!, this.patchMaterial!);
            engine.Build(tile, gpu, this.lodRangesM);

            bool ok = engine.SelfTest(out string msg);
            Debug.Log($"[GpuTerrainRenderer] Tile[{tileIndex}] {tile.tileCoord}: {msg}");
            if (!ok)
            {
                Debug.LogError($"[GpuTerrainRenderer] Tile[{tileIndex}] SelfTest failed — skipped.");
                engine.Dispose();
                gpu.Dispose();
                return;
            }

            // H2: reject duplicate coords — would silently overwrite the coord→index entry,
            // making the earlier tile unreachable via the sculpt seam.
            if (this.coordToIndex.ContainsKey(tile.tileCoord))
            {
                Debug.LogError($"[GpuTerrainRenderer] Duplicate tileCoord {tile.tileCoord} " +
                    $"on Tile[{tileIndex}] — skipping to avoid coord map collision.");
                engine.Dispose();
                gpu.Dispose();
                return;
            }

            int idx = this.engines.Count;
            this.engines.Add(engine);
            this.gpuResources.Add(gpu);
            this.coordToIndex[tile.tileCoord] = idx;
            // H1: assert lists stay in lockstep after every successful append.
            Debug.Assert(this.engines.Count == this.gpuResources.Count,
                "[GpuTerrainRenderer] engines/gpuResources list desync after BuildOneTile append.");
        }

        private void DisposeEngines()
        {
            Debug.Assert(this.engines.Count == this.gpuResources.Count,
                "[GpuTerrainRenderer] engines/gpuResources list desync in DisposeEngines.");
            for (int i = 0; i < this.engines.Count; i++)
            {
                this.engines[i].Dispose();
                this.gpuResources[i].Dispose();
            }
            this.engines.Clear();
            this.gpuResources.Clear();
            this.coordToIndex.Clear();
            this.isBuilt = false;
        }

        // ── Submit ────────────────────────────────────────────────────────────

        private void SubmitForCamera(Camera? cam)
        {
            Vector3 camPos = cam != null ? cam.transform.position :
                             (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            foreach (var e in this.engines)
                e.Submit(cam, camPos);
        }

        // ── Context menu (editor rebuild) ─────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Rebuild")]
        private void Rebuild() => this.TryBuild();
#endif
    }
}
