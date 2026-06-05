#nullable enable
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Scene orchestrator for an interactive instanced grass field. Builds a FLAT instance set on enable
    /// (via <see cref="GrassScatter"/>), then submits the instanced grass draws every frame from the PLAYER
    /// LOOP — <c>LateUpdate</c> in play mode and <c>EditorApplication.update</c> in edit mode — so the field
    /// appears in BOTH the Game view and the Scene view. The draws use <c>rp.camera = null</c> (render in all
    /// cameras) under one field-wide bounds.
    ///
    /// The draws are deliberately NOT issued from <c>RenderPipelineManager.beginCameraRendering</c>: under
    /// URP's RenderGraph (Unity 6 default) immediate-mode instanced draws from that callback are silently
    /// dropped (the grass renders nothing despite thousands of live instances). See <see cref="GrassRenderer"/>.
    /// The per-frame path allocates nothing.
    ///
    /// In Phase 2 the field renders the STATIC base slabs (no motion). Bend + wind are added in Phase 3 by
    /// GrassBendSimulator, which consumes the base matrices/positions this builds.
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

        private GrassScatterResult? scatter;
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
            this.ReleaseScatter();
            this.pool?.Clear();
        }

        /// <summary>(Re)builds the flat scatter + renderer. Safe to call repeatedly.</summary>
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
            this.ReleaseScatter();
            this.scatter = GrassScatter.Build(this.grassLayer, this.transform.position, this.pool);
            this.grassRenderer = new GrassRenderer(this.config!, this.transform.position);

            WarnIfMultipleEnabledFields();
        }

        /// <summary>
        /// Phase 3's GrassBendSimulator keeps per-field state, so two enabled fields would fight over the
        /// shared simulator/interactor registry. Warn loudly if more than one is active (single-field-per-scene
        /// is the supported configuration).
        /// </summary>
        private static void WarnIfMultipleEnabledFields()
        {
            GrassInteractField[] all = FindObjectsByType<GrassInteractField>(FindObjectsSortMode.None);
            int enabledCount = 0;
            foreach (GrassInteractField f in all)
                if (f.isActiveAndEnabled)
                    ++enabledCount;

            if (enabledCount > 1)
                Debug.LogError($"[{nameof(GrassInteractField)}] {enabledCount} enabled fields found. The bend " +
                    "simulator + interactor registry are per-field — only ONE field per scene is supported; " +
                    "multiple will fight over the shared interaction state. Keep a single enabled field.");
        }

        private void ReleaseScatter()
        {
            if (this.scatter != null && this.pool != null)
                GrassScatter.ReturnSlabs(this.scatter, this.pool);
            this.scatter = null;
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

        /// <summary>Submits the static base slabs for instanced rendering in all cameras. LOD references the
        /// main camera. Phase 3 swaps the base slabs for the simulator's per-frame render slabs.</summary>
        private void RenderGrass()
        {
            if (this.scatter == null || this.grassRenderer == null)
                return;

            Camera main = Camera.main;
            Vector3 lodRef = main != null ? main.transform.position : this.transform.position;
            this.grassRenderer.Render(lodRef, this.scatter.BaseSlabs, this.scatter.SlabCounts,
                this.scatter.WorldBounds);
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
                // Field rect (XZ) at the field Y — the rect placement keys off.
                Vector2 bounds = this.grassLayer.FieldBounds;
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
                Gizmos.DrawWireCube(this.transform.position,
                    new Vector3(bounds.x, 0.05f, bounds.y));
            }

            if (this.scatter == null)
                return;

            // Single field-wide render bounds (includes blade height + bend headroom).
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.25f);
            Gizmos.DrawWireCube(this.scatter.WorldBounds.center, this.scatter.WorldBounds.size);
        }
#endif
    }
}
