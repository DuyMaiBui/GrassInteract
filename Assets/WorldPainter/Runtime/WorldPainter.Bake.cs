#nullable enable
using UnityEngine;

namespace WorldPainter
{
    /// <summary>
    /// WorldPainter partial — P8 bake manifest hook.
    ///
    /// When a <see cref="TileBakeManifest"/> is assigned via <see cref="BakeManifest"/>:
    ///   - Runtime (player build / Play mode with manifest): tiles are handed to
    ///     <see cref="TerrainStreamingManager"/> for per-tile proximity streaming.
    ///   - Editor Play (no manifest assigned): reads the <see cref="WorldMapAsset"/>
    ///     container directly — fast iteration path, no prior bake required.
    ///
    /// This partial does NOT modify any fields or methods in the frozen
    /// <c>WorldPainter.cs</c>, <c>WorldPainter.Data.cs</c>, <c>WorldPainter.Render.cs</c>,
    /// or <c>WorldPainter.Scatter.cs</c> partials.
    /// </summary>
    public sealed partial class WorldPainter
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Bake Manifest (P8)")]
        [Tooltip("Assign a TileBakeManifest produced by WorldMapBaker.BakeAll to switch the " +
                 "runtime from container-direct to per-tile streaming via TerrainStreamingManager. " +
                 "Leave null for Editor Play (container-direct fast-iteration path).")]
        [SerializeField] private TileBakeManifest? bakeManifest;

        [Tooltip("TerrainStreamingManager to register baked tiles into. " +
                 "Only used when BakeManifest is assigned.")]
        [SerializeField] private TerrainStreamingManager? streamingManager;

        /// <summary>
        /// The assigned bake manifest. When non-null, <see cref="OnBakeManifestAssigned"/> hands
        /// the baked tiles to <see cref="streamingManager"/>. When null, the container-direct
        /// editor-Play path is used instead.
        /// </summary>
        public TileBakeManifest? BakeManifest
        {
            get => this.bakeManifest;
            set
            {
                this.bakeManifest = value;
                this.bakeManifestRegistered = false; // force re-registration on next TryApplyBakeManifest
            }
        }

        // ── Runtime state ──────────────────────────────────────────────────────

        // Guard: register tiles only once per manifest assignment so repeated
        // LateUpdate calls don't re-add already-registered tiles.
        private bool bakeManifestRegistered;

        // ── Hook called from TryBuild (Render partial) ────────────────────────

        // NOTE: TryBuild() lives in WorldPainter.Render.cs. We cannot modify that file.
        // Instead, the bake-manifest path is checked in a separate method called from
        // OnEnable (which IS in WorldPainter.Render.cs via partial). To avoid modifying
        // the frozen partials, we hook via a second OnEnable-equivalent using
        // [RuntimeInitializeOnLoadMethod] is not available for MonoBehaviours.
        //
        // Architectural decision: expose a public method TryApplyBakeManifest() that callers
        // (e.g. SceneBootstrapper or a manager component) can invoke after Start, OR rely on
        // the LateUpdate path which checks it lazily every frame until registration completes.
        // The LateUpdate approach requires no external wiring and is self-contained.

        // ── LateUpdate integration (checked lazily) ───────────────────────────

        // The frozen WorldPainter.cs LateUpdate calls: TryBuild → SubmitTerrain → StepScatter →
        // SubmitScatter → DrivePropLayers. We add our bake hook as a lazy check in a separate
        // private method invoked from a second partial LateUpdate is NOT possible (only one
        // LateUpdate allowed per class — it lives in WorldPainter.cs which is frozen).
        //
        // Therefore we expose a public entry point and rely on the TerrainStreamingManager
        // (which is a separate MonoBehaviour) to drive loading. The WorldPainter.Bake partial
        // is responsible for:
        //   1. Registering baked tile REFERENCES into TerrainStreamingManager at Start/OnEnable.
        //   2. Providing the fallback check: if no manifest, editor-Play reads map directly.

        private void Start()
        {
            this.TryApplyBakeManifest();
        }

        /// <summary>
        /// Hands all tiles from the assigned <see cref="BakeManifest"/> to the
        /// <see cref="TerrainStreamingManager"/> so they are available for proximity streaming.
        ///
        /// Call once after the manifest is assigned; subsequent calls on the same manifest
        /// are no-ops (guard flag <c>bakeManifestRegistered</c>).
        ///
        /// If no manifest is assigned this is a no-op — the container-direct path in
        /// <c>WorldPainter.Render.cs</c> TryBuild handles editor-Play.
        /// </summary>
        public void TryApplyBakeManifest()
        {
            if (this.bakeManifestRegistered) return;
            if (this.bakeManifest == null)   return;
            if (this.streamingManager == null)
            {
                WpLog.Warning("[WorldPainter] BakeManifest assigned but no " +
                    "TerrainStreamingManager referenced. Assign it in the Inspector.");
                return;
            }

            this.streamingManager.RegisterFromManifest(this.bakeManifest);
            this.bakeManifestRegistered = true;

            WpLog.Log($"[WorldPainter] Bake manifest applied: " +
                $"{this.bakeManifest.Count} tile(s) registered to TerrainStreamingManager.");
        }

        /// <summary>
        /// Returns true if the runtime should use baked-tile streaming rather than
        /// reading the container directly.
        /// True when a manifest is assigned AND at least one entry exists.
        /// </summary>
        public bool IsBakeManifestActive =>
            this.bakeManifest != null && this.bakeManifest.Count > 0;
    }
}
