#nullable enable
using UnityEngine;
using GrassInteract;

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
    ///
    /// P3a wiring: LateUpdate drives <see cref="ScatterField"/> per scatter layer and wires
    /// <see cref="HeightmapSurfaceSampler"/> grounding internally (replacing the external
    /// <c>GpuTerrainScatterGround</c> bridge). Interface <see cref="ISurfaceSampler"/> is
    /// UNCHANGED; <c>HeightmapSurfaceSamplerTests</c> remain green.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("GpuTerrain/World Painter")]
    public sealed partial class WorldPainter : MonoBehaviour
    {
        // ── Scatter grounding ─────────────────────────────────────────────────

        // Cached sampler wired into any co-located ScatterField (replaces GpuTerrainScatterGround).
        private HeightmapSurfaceSampler? heightmapSampler;
        private ScatterField? cachedScatterField;

        // ── LateUpdate submit scheduler ───────────────────────────────────────

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            if (!this.IsBuilt) this.TryBuild();
            this.SubmitTerrain(null);
            this.DriveScatterField();
        }

        // ── Scatter field wiring ──────────────────────────────────────────────

        /// <summary>
        /// Drives the co-located <see cref="ScatterField"/> (if any) per scatter layer and
        /// wires <see cref="HeightmapSurfaceSampler"/> grounding internally.
        /// Replaces the external <c>GpuTerrainScatterGround</c> bridge component; the
        /// <see cref="ISurfaceSampler"/> seam and <c>HeightmapSurfaceSamplerTests</c> are unchanged.
        /// </summary>
        private void DriveScatterField()
        {
            // Resolve a co-located ScatterField (same GameObject or children).
            if (this.cachedScatterField == null)
                this.cachedScatterField = this.GetComponentInChildren<ScatterField>();

            ScatterField? field = this.cachedScatterField;
            if (field == null) return;

            // Build HeightmapSurfaceSampler from the first resident tile (single-tile / demo mode).
            if (this.heightmapSampler == null && this.tiles != null)
            {
                TerrainTileAsset? firstTile = null;
                for (int i = 0; i < this.tiles.Count; i++)
                {
                    if (this.tiles[i].tileAsset != null && this.tiles[i].tileAsset!.IsHeightValid)
                    {
                        firstTile = this.tiles[i].tileAsset;
                        break;
                    }
                }

                if (firstTile != null)
                {
                    this.heightmapSampler = new HeightmapSurfaceSampler(firstTile);
                    // Wire into ScatterField — only if no other external sampler is set
                    // so we don't trample a user-supplied override.
                    if (field.ExternalSampler == null)
                    {
                        field.ExternalSampler = this.heightmapSampler;
                        field.Rebuild();
                    }
                }
            }
        }

        // ── Residency / visibility early-outs ─────────────────────────────────

        /// <summary>
        /// Returns true when at least one tile is resident and ready to submit.
        /// </summary>
        private bool IsResidencyReady() =>
            this.tiles != null && this.tiles.Count > 0;
    }
}
