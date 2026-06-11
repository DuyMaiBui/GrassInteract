#nullable enable
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// Runtime component for the WorldPainter authoring tool.
    /// Owns the Tier-A inline schema (see <see cref="WorldPainter"/> partial in WorldPainter.Data.cs)
    /// and drives a per-system LateUpdate submit scheduler guarded by residency/visibility early-outs.
    ///
    /// All brush/preview/authoring code lives in <c>WorldPainter.Authoring.cs</c> under
    /// <c>#if UNITY_EDITOR</c> — this file SHIPS in player builds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class WorldPainter : MonoBehaviour
    {
        // ── Runtime engine refs (optional — null = engine not present in scene) ──

        [Tooltip("Optional: GPU terrain engine to drive. Auto-located if null.")]
        [SerializeField] private GpuTerrainEngine? terrainEngine;

        // ── LateUpdate submit scheduler ───────────────────────────────────────

        private void LateUpdate()
        {
            if (!this.IsResidencyReady())
            {
                return;
            }

            this.SubmitTerrain();
        }

        // ── Residency / visibility early-outs ─────────────────────────────────

        /// <summary>
        /// Returns true when at least one tile is resident and the engine is ready
        /// to accept a submit. Override or extend in Authoring partial for editor checks.
        /// </summary>
        private bool IsResidencyReady()
        {
            if (this.tiles == null || this.tiles.Count == 0)
            {
                return false;
            }

            if (this.terrainEngine == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Submit this frame's data to the terrain engine.
        /// Placeholder — actual submit wiring added once engine API is confirmed.
        /// </summary>
        private void SubmitTerrain()
        {
            // TODO: drive terrainEngine.Submit per-tile based on residency set.
            // Deferred to P1 Batch 2 (sculpt end-to-end wiring).
        }
    }
}
