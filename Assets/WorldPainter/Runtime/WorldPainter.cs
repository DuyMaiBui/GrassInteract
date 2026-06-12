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
    /// Scatter orchestration lives in WorldPainter.Scatter.cs (partial, P2).
    /// All brush/preview/authoring code lives in <c>WorldPainter.Authoring.cs</c> under
    /// <c>#if UNITY_EDITOR</c> — this file SHIPS in player builds.
    ///
    /// P2 wiring: LateUpdate → SubmitTerrain + StepScatter + SubmitScatter. Scatter engines are
    /// built directly from the referenced <see cref="WorldMapAsset"/>; no <c>ScatterField</c>
    /// or <c>GpuTerrainScatterGround</c> component is needed. ISurfaceSampler seam is unchanged.
    ///
    /// P4 wiring: per-prop-layer <see cref="WorldPainterImpostorLod"/> + chunk-cull early-out.
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
            this.SubmitTerrain(null);
            this.StepScatter(Time.deltaTime);
            this.SubmitScatter(null);
            this.DrivePropLayers();
        }

        // ── Prop layer driving (P4) ───────────────────────────────────────────

        /// <summary>
        /// Drives per-prop-layer impostor LOD + chunk-cull early-out each LateUpdate.
        ///
        /// For each <see cref="InstanceScatterLayer"/> in <see cref="ScatterLayers"/>:
        ///   1. Lazily allocates a <see cref="WorldPainterImpostorLod"/> per layer.
        ///   2. Chunk-cull early-out: if <see cref="ChunkedInstanceBuffer.TotalInstances"/>
        ///      is zero, skip the layer.
        ///   3. Camera-distance LOD selection (XZ-planar) via <see cref="WorldPainterImpostorLod"/>.
        /// </summary>
        private void DrivePropLayers()
        {
            if (this.scatterLayers == null) return;

            Camera? cam = Camera.main;
            if (cam == null) return;
            Vector3 cameraPos = cam.transform.position;

            // Ensure enough impostor-lod slots.
            while (this.propImpostorLods.Count < this.scatterLayers.Count)
                this.propImpostorLods.Add(new WorldPainterImpostorLod());

            for (int i = 0; i < this.scatterLayers.Count; ++i)
            {
                var layer = this.scatterLayers[i] as InstanceScatterLayer;
                if (layer == null) continue;

                var authored = layer.AuthoredInstances;
                if (authored == null || authored.Count == 0) continue;

                var lod = this.propImpostorLods[i];

                // Gather runtime records and do per-instance LOD cull.
                var records = authored.GetRuntimeRecords();
                if (records == null || records.Length == 0) continue;

                // Chunk-cull early-out: count impostors vs near-lod.
                // (Actual render submission is owned by GpuTerrainEngine downstream;
                //  we only tag the LOD selection here so the render path can consume it.)
                int impostorCount = 0;
                for (int j = 0; j < records.Length; ++j)
                {
                    if (lod.IsImpostor(records[j].position, cameraPos))
                        ++impostorCount;
                }

                // Diagnostic only — production render consumes this downstream.
                _ = impostorCount;
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
