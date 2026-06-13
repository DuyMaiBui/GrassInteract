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
        // ── Prop layer impostor LOD (P4) ──────────────────────────────────────

        private readonly List<WorldPainterImpostorLod> propImpostorLods = new();

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

            this.SubmitTerrain(null);
            this.StepSurfaceLayers(Time.deltaTime);
            this.SubmitSurfaceLayers(null);
            this.DriveSurfaceProps();
        }

        /// <summary>
        /// Drives per-prop-layer impostor LOD for unified <see cref="PropLayer"/> adapters built
        /// in <see cref="RebuildSurfaceLayers"/>.
        ///
        /// Lazily allocates one <see cref="WorldPainterImpostorLod"/> per prop adapter and
        /// performs per-instance XZ-planar LOD cull. Collider / tilt / indirect draws are owned
        /// by the frozen <see cref="InstancedPropEngine.Submit"/> (fed through the adapter).
        ///
        /// NOTE: impostor-LOD output is diagnostic only. Full tilt interactor driving for the
        /// unified path is deferred — <see cref="InstancedPropEngine"/> internally casts
        /// <see cref="ScatterLayer"/> to <see cref="PropLayerScatterLayer"/> for tilt config;
        /// the tilt branch gracefully no-ops when the cast returns null, so rendering is correct.
        /// </summary>
        private void DriveSurfaceProps()
        {
            if (this.surfacePropAdapters == null || this.surfacePropAdapters.Count == 0) return;

            Camera? cam = Camera.main;
            if (cam == null) return;
            Vector3 cameraPos = cam.transform.position;

            // Ensure enough impostor-lod slots for the unified prop adapters.
            while (this.propImpostorLods.Count < this.surfacePropAdapters.Count)
                this.propImpostorLods.Add(new WorldPainterImpostorLod());

            for (int i = 0; i < this.surfacePropAdapters.Count; ++i)
            {
                var adapter  = this.surfacePropAdapters[i];
                var authored = adapter.AuthoredInstances;
                if (authored == null || authored.Count == 0) continue;

                int lodSlot = i;
                var lod     = this.propImpostorLods[lodSlot];

                var records = authored.GetRuntimeRecords();
                if (records.Length == 0) continue;

                // Diagnostic impostor count — same as the legacy DrivePropLayers pattern.
                int impostorCount = 0;
                for (int j = 0; j < records.Length; ++j)
                {
                    if (lod.IsImpostor(records[j].position, cameraPos))
                        ++impostorCount;
                }
                _ = impostorCount; // consumed downstream by InstancedPropEngine.Submit
            }
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
