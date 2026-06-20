#nullable enable
using System.Collections.Generic;
using UnityEngine;
using WorldPainter;

namespace WorldPainter
{
    /// <summary>
    /// Runtime component for the WorldPainter authoring tool.
    /// Owns the Tier-A inline schema (see <see cref="WorldPainter"/> partial in WorldPainter.Data.cs)
    /// and drives a per-tile LateUpdate submit scheduler guarded by residency/visibility early-outs.
    ///
    /// Render engine logic lives in WorldPainter.Render.cs (partial).
    /// Scatter orchestration lives in WorldPainter.Scatter.cs (partial).
    /// Surface layer orchestration lives in WorldPainter.SurfaceLayers.cs (partial).
    /// All brush/preview/authoring code lives in <c>WorldPainter.Authoring.cs</c> under
    /// <c>#if UNITY_EDITOR</c> — this file SHIPS in player builds.
    ///
    /// P5 wiring: LateUpdate → SubmitTerrain + StepSurfaceLayers + SubmitSurfaceLayers +
    /// DriveSurfaceProps. Surface engines are built from the unified SurfaceLayers system;
    /// no legacy ScatterField or GpuTerrainScatterGround component is needed.
    ///
    /// P5 wiring: per-prop-layer <see cref="WorldPainterImpostorLod"/> + chunk-cull early-out.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("WorldPainter/World Painter")]
    public sealed partial class WorldPainter : MonoBehaviour
    {
        // ── LateUpdate submit scheduler ───────────────────────────────────────

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (!this.IsBuilt) this.TryBuild();

            // Build surface (grass/prop) engines once on play start, then submit each frame.
            if (this.IsBuilt && !this.playScatterBuilt)
            {
                this.RebuildSurfaceLayers();
                this.playScatterBuilt = true;
            }

            // Skip the entire per-frame submit chain until at least one tile is resident (and during
            // teardown / streaming gaps) — avoids the Camera.main resolve, root-binder work, and the
            // empty submit loops when there is nothing to draw.
            if (!this.IsResidencyReady()) return;

            this.SubmitTerrain(null);
            this.StepSurfaceLayers(Time.deltaTime);
            this.SubmitSurfaceLayers(null);
            this.DriveSurfaceProps();
        }

        /// <summary>
        /// Per-prop-layer driving hook for the unified <see cref="PropLayer"/> path.
        ///
        /// Currently a no-op: prop collider / tilt / indirect draws are owned by
        /// <see cref="InstancedPropEngine.Submit"/> (fed through the adapter), so rendering is
        /// correct without per-frame work here. The previous body computed a per-instance
        /// impostor count that was then discarded — a per-frame <c>Camera.main</c> lookup plus an
        /// O(instances) scan with no observable effect — and was removed. Re-add real impostor /
        /// tilt-interactor driving here when the unified path needs it.
        /// </summary>
        private void DriveSurfaceProps()
        {
            // Intentionally empty — see summary. InstancedPropEngine.Submit owns prop driving.
        }

        // ── Residency / visibility early-outs ─────────────────────────────────

        /// <summary>
        /// Returns true when at least one tile is resident and ready to submit.
        /// Checks the map container (P2) first, then the legacy inline tiles list.
        /// </summary>
        private bool IsResidencyReady()
        {
            if (this.map != null) return this.map.TileCount > 0;
            return this.tiles != null && this.tiles.Count > 0;
        }
    }
}
