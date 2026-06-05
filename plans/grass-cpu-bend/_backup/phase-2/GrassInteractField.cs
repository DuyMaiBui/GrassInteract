#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Scene orchestrator for an interactive instanced grass field. Builds the chunk grid on enable, binds
    /// the shared <see cref="GrassFieldSpace"/> rect (SSOT for the density + trample maps), then submits the
    /// instanced grass draws every frame from the PLAYER LOOP — <c>LateUpdate</c> in play mode and
    /// <c>EditorApplication.update</c> in edit mode — so the field appears in BOTH the Game view and the
    /// Scene view. The draws use <c>rp.camera = null</c> (render in all cameras) with per-chunk GPU culling.
    ///
    /// The draws are deliberately NOT issued from <c>RenderPipelineManager.beginCameraRendering</c>: under
    /// URP's RenderGraph (Unity 6 default) immediate-mode instanced draws from that callback are silently
    /// dropped (the grass renders nothing despite thousands of live instances). See <see cref="GrassRenderer"/>.
    /// The per-frame path allocates nothing.
    ///
    /// Bending is delivered entirely by the grass shader (ambient wind + the trample RenderTexture written
    /// by GrassTrampleMap). This component owns no bend logic.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GrassInteractField : MonoBehaviour
    {
        [Tooltip("The painted grass layer (density map + placement). Its RenderConfig drives rendering.")]
        [SerializeField] private GrassLayer? grassLayer;

        [Min(0)]
        [Tooltip("Instancing slabs to pre-allocate at build. 0 = lazy. Set near the worst-case visible " +
                 "batch count to avoid a first-frame allocation hitch.")]
        [SerializeField] private int prewarmSlabs = 0;

        private GrassLODConfig? config; // derived from grassLayer.RenderConfig at build

        private static readonly int TrampleMapId = Shader.PropertyToID("_GrassTrampleMap");
        private static readonly int WindDirId = Shader.PropertyToID("_GrassWindDir");
        private static readonly int WindStrengthId = Shader.PropertyToID("_GrassWindStrength");
        private static readonly int WindFreqId = Shader.PropertyToID("_GrassWindFreq");
        private static readonly int WindNoiseScaleId = Shader.PropertyToID("_GrassWindNoiseScale");
        private static readonly int BendStrengthId = Shader.PropertyToID("_GrassBendStrength");
        private static readonly int FlattenId = Shader.PropertyToID("_GrassFlatten");

        private GrassChunk[]? chunks;
        private GrassRenderer? grassRenderer;
        private InstanceBatchPool? pool;

        private void OnEnable()
        {
            this.Rebuild();
#if UNITY_EDITOR
            // Edit-mode driver: submit grass draws every editor tick + repaint the scene views, so the field
            // renders live in edit mode without entering Play. Play mode uses LateUpdate (player loop).
            UnityEditor.EditorApplication.update -= this.EditorRenderTick;
            if (!Application.isPlaying)
                UnityEditor.EditorApplication.update += this.EditorRenderTick;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= this.EditorRenderTick;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= this.EditorRenderTick;
#endif
            this.ReleaseChunks();
            this.pool?.Clear();
        }

        /// <summary>(Re)builds the chunk grid + renderer and re-binds the field rect. Safe to call repeatedly.</summary>
        public void Rebuild()
        {
            if (this.grassLayer == null)
            {
                Debug.LogError($"[{nameof(GrassInteractField)}] No GrassLayer assigned.", this);
                return;
            }

            if (!this.grassLayer.Validate(out string error))
            {
                Debug.LogError($"[{nameof(GrassInteractField)}] Invalid GrassLayer: {error}", this);
                return;
            }

            this.config = this.grassLayer.RenderConfig; // validated non-null by GrassLayer.Validate

            this.pool ??= new InstanceBatchPool(this.prewarmSlabs);
            this.ReleaseChunks();
            this.chunks = ChunkGrid.Build(this.grassLayer, this.transform.position, this.pool);
            this.grassRenderer = new GrassRenderer(this.config!, this.transform.position);

            // SSOT: bind the one field rect every map (density + trample) keys off. This rect is a SHADER
            // GLOBAL, so exactly one enabled field per scene is supported — a second field would overwrite
            // it and silently mis-map the other field's density + trample. Fail loudly rather than render wrong.
            WarnIfMultipleEnabledFields();
            new GrassFieldSpace(this.transform.position, this.grassLayer.FieldBounds).BindGlobals();
            this.BindDeformGlobals();

            // Default the trample map to black so grass stays upright when no GrassTrampleMap is present.
            // A GrassTrampleMap in the scene overrides this global every frame.
            if (Shader.GetGlobalTexture(TrampleMapId) == null)
                Shader.SetGlobalTexture(TrampleMapId, Texture2D.blackTexture);
        }

        /// <summary>
        /// The field rect + wind + trample globals are shader-GLOBAL, so two enabled fields fight over them.
        /// Warn loudly if more than one is active (single-field-per-scene is the supported configuration).
        /// </summary>
        private static void WarnIfMultipleEnabledFields()
        {
            GrassInteractField[] all = FindObjectsByType<GrassInteractField>(FindObjectsSortMode.None);
            int enabledCount = 0;
            foreach (GrassInteractField f in all)
                if (f.isActiveAndEnabled)
                    ++enabledCount;

            if (enabledCount > 1)
                Debug.LogError($"[{nameof(GrassInteractField)}] {enabledCount} enabled fields found. The " +
                    "field rect / trample / wind are shader globals — only ONE field per scene is supported; " +
                    "multiple will mis-map each other's density + trample. Keep a single enabled field.");
        }

        /// <summary>
        /// Pushes the config's ambient-wind tunables AND the trample lean tunables as shader globals.
        /// Wind sway animates via _Time; the lean amplitude/flatten feed GrassInteractDeform.hlsl's
        /// vector-field trample lean. These are static per-config, so binding once per Rebuild is enough.
        /// </summary>
        private void BindDeformGlobals()
        {
            if (this.config == null)
                return;

            Vector2 dir = this.config.WindDirection;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : new Vector2(1f, 0f);
            Shader.SetGlobalVector(WindDirId, new Vector4(dir.x, dir.y, 0f, 0f));
            Shader.SetGlobalFloat(WindStrengthId, this.config.WindStrength);
            Shader.SetGlobalFloat(WindFreqId, this.config.WindFrequency);
            Shader.SetGlobalFloat(WindNoiseScaleId, this.config.WindNoiseScale);

            // Trample lean-away amplitude + optional height loss (GrassInteractDeform.hlsl).
            Shader.SetGlobalFloat(BendStrengthId, this.config.BendStrength);
            Shader.SetGlobalFloat(FlattenId, this.config.Flatten);
        }

        private void ReleaseChunks()
        {
            if (this.chunks != null && this.pool != null)
                ChunkGrid.ReturnSlabs(this.chunks, this.pool);
            this.chunks = null;
        }

        // Play-mode driver: LateUpdate is inside the player loop, so the instanced draws it submits DO render
        // under RenderGraph (unlike beginCameraRendering). Edit mode is driven from EditorRenderTick instead.
        private void LateUpdate()
        {
            if (Application.isPlaying)
                this.RenderGrass();
        }

#if UNITY_EDITOR
        private void EditorRenderTick()
        {
            if (Application.isPlaying)
                return;
            this.RenderGrass();
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        /// <summary>Submits the grass for instanced rendering in all cameras. LOD references the main camera.</summary>
        private void RenderGrass()
        {
            if (this.chunks == null || this.grassRenderer == null)
                return;

            Camera main = Camera.main;
            Vector3 lodRef = main != null ? main.transform.position : this.transform.position;
            this.grassRenderer.Render(lodRef, this.chunks);
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Grass")]
        private void RebuildFromMenu()
        {
            this.Rebuild();
        }

        private void OnDrawGizmosSelected()
        {
            if (this.grassLayer != null)
            {
                // Field rect (XZ) at the field Y — the rect every map keys off.
                Vector2 bounds = this.grassLayer.FieldBounds;
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
                Gizmos.DrawWireCube(this.transform.position,
                    new Vector3(bounds.x, 0.05f, bounds.y));
            }

            if (this.chunks == null)
                return;

            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.25f);
            foreach (GrassChunk chunk in this.chunks)
                Gizmos.DrawWireCube(chunk.Bounds.center, chunk.Bounds.size);
        }
#endif
    }
}
