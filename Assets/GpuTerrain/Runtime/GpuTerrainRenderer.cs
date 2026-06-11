#nullable enable
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GpuTerrain
{
    /// <summary>
    /// [ExecuteAlways] MonoBehaviour: owns GpuTerrainEngine, submits from the player loop.
    ///
    /// Submit discipline (mirrors GrassRenderer + GrassGpuEngine):
    ///  - Play mode: LateUpdate submits with null camera (renders in all cameras).
    ///  - Edit mode: EditorApplication.update + beginCameraRendering fires per-camera
    ///    so Scene and Game views each get exactly one draw (no N× overdraw).
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("GpuTerrain/Gpu Terrain Renderer")]
    public sealed class GpuTerrainRenderer : MonoBehaviour
    {
        // ── Inspector fields ──────────────────────────────────────────────────
        [SerializeField] private TerrainTileAsset?  tileAsset      = null;
        [SerializeField] private ComputeShader?     cullCompute    = null;
        [SerializeField] private Material?          patchMaterial  = null;
        [SerializeField] [Tooltip("LOD range distances in metres, index 0 = finest.")]
        private float[] lodRangesM = new float[] { 32f, 64f, 128f, 256f };

        // ── Runtime state ─────────────────────────────────────────────────────
        private GpuTerrainEngine? engine;
        private TerrainTileGpuResources? gpuResources;
        private bool isBuilt;

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
            this.DisposeEngine();
        }

        private void LateUpdate()
        {
            // Play mode: submit with null camera (all cameras, player loop).
            if (!Application.isPlaying) return;
            if (!this.isBuilt) this.TryBuild();
            this.engine?.Step(Time.deltaTime);
            this.SubmitForCamera(null);
        }

#if UNITY_EDITOR
        private void OnBeginCameraRenderingEdit(ScriptableRenderContext ctx, Camera cam)
        {
            // Edit mode: per-camera submit to avoid N× overdraw across Scene + Game views.
            if (Application.isPlaying) return;
            if (!this.isBuilt) this.TryBuild();
            this.SubmitForCamera(cam);
        }
#endif

        // ── Build / Dispose ───────────────────────────────────────────────────

        private void TryBuild()
        {
            this.DisposeEngine();

            if (this.tileAsset == null || this.cullCompute == null || this.patchMaterial == null)
            {
                Debug.LogWarning("[GpuTerrainRenderer] Missing tileAsset, cullCompute, or patchMaterial. Not building.");
                return;
            }
            if (!this.tileAsset.IsHeightValid)
            {
                Debug.LogWarning($"[GpuTerrainRenderer] Tile '{this.tileAsset.name}' heightData invalid. Not building.");
                return;
            }

            this.gpuResources = new TerrainTileGpuResources();
            this.gpuResources.Upload(this.tileAsset);

            this.engine = new GpuTerrainEngine(this.cullCompute, this.patchMaterial);
            this.engine.Build(this.tileAsset, this.gpuResources, this.lodRangesM);

            bool selfTestPass = this.engine.SelfTest(out string selfTestMsg);
            Debug.Log($"[GpuTerrainRenderer] {selfTestMsg}");
            if (!selfTestPass)
            {
                Debug.LogError("[GpuTerrainRenderer] SelfTest failed — disabling renderer.");
                this.DisposeEngine();
                return;
            }

            this.isBuilt = true;
            Debug.Log($"[GpuTerrainRenderer] Built tile {this.tileAsset.tileCoord}.");
        }

        private void DisposeEngine()
        {
            this.engine?.Dispose();
            this.engine = null;
            this.gpuResources?.Dispose();
            this.gpuResources = null;
            this.isBuilt = false;
        }

        // ── Submit ────────────────────────────────────────────────────────────

        private void SubmitForCamera(Camera? cam)
        {
            if (this.engine == null) return;
            Vector3 camPos = cam != null ? cam.transform.position :
                             (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            this.engine.Submit(cam, camPos);
        }

        // ── Context menu (editor rebuild) ─────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Rebuild")]
        private void Rebuild() => this.TryBuild();
#endif
    }
}
