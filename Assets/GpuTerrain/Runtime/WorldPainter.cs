#nullable enable
using UnityEngine;

namespace GpuTerrain
{
    /// <summary>
    /// Runtime component for the WorldPainter authoring tool.
    /// Owns the Tier-A inline schema (see <see cref="WorldPainter"/> partial in WorldPainter.Data.cs)
    /// and drives a per-tile LateUpdate submit scheduler guarded by residency/visibility early-outs.
    ///
    /// Render engine logic lives in WorldPainter.Render.cs (partial).
    /// All brush/preview/authoring code lives in <c>WorldPainter.Authoring.cs</c> under
    /// <c>#if UNITY_EDITOR</c> — this file SHIPS in player builds.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("GpuTerrain/World Painter")]
    public sealed partial class WorldPainter : MonoBehaviour
    {
        // ── LateUpdate submit scheduler ───────────────────────────────────────

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (!this.IsBuilt) this.TryBuild();
            this.SubmitTerrain(null);
        }

        // ── Residency / visibility early-outs ─────────────────────────────────

        /// <summary>
        /// Returns true when at least one tile is resident and ready to submit.
        /// </summary>
        private bool IsResidencyReady() =>
            this.tiles != null && this.tiles.Count > 0;
    }
}
