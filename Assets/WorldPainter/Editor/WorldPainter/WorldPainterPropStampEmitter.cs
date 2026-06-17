#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Scatter-mode stamp emitter for Prop layers (P7).
    ///
    /// Each spacing-stamp (emitted by <see cref="WorldPainterStroke"/>) randomly places
    /// <see cref="DensityPerStamp"/> jittered <see cref="InstanceRecord"/>s within
    /// <c>brushRadius</c> around the stamp centre, applying the layer's anchor config
    /// (ground-snap / align-to-normal / pivot offset) and slope-range rejection.
    ///
    /// Records are written to the per-tile bucket of the overlapping tile so that P8
    /// bake/stream can address them by tile coordinate.
    ///
    /// Shift-mode deletes all records within the brush footprint.
    /// </summary>
    internal sealed class WorldPainterPropStampEmitter
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Default instances deposited per stamp.</summary>
        public const int DEFAULT_DENSITY_PER_STAMP = 3;

        /// <summary>Scale jitter half-range (±fraction of midScale).</summary>
        private const float DEFAULT_SCALE_JITTER = 0.25f;

        // ── Configurable fields ───────────────────────────────────────────────

        /// <summary>How many instances to attempt per stamp position.</summary>
        public int DensityPerStamp { get; set; } = DEFAULT_DENSITY_PER_STAMP;

        /// <summary>
        /// Scale jitter half-range as a fraction of the layer's mid-scale.
        /// 0 = no jitter; 0.5 = ±50%.
        /// </summary>
        public float ScaleJitter { get; set; } = DEFAULT_SCALE_JITTER;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Emits jittered <see cref="InstanceRecord"/>s into <paramref name="propLayer"/> at
        /// <paramref name="stampPos"/> with the given brush radius.
        ///
        /// Applies the layer's anchor config (ground-snap / align-to-normal / pivot offset).
        /// Writes each record into the per-tile bucket of the tile it lands in.
        /// On Shift (<paramref name="deleteMode"/>=true) removes records under the brush.
        /// </summary>
        public void Emit(
            PropLayer                propLayer,
            Vector3                  stampPos,
            float                    brushRadius,
            bool                     deleteMode,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            var authored = propLayer.AuthoredInstances;
            if (authored == null) return;

            if (deleteMode)
                this.DeleteUnderBrush(authored, stampPos, brushRadius);
            else
                this.AddInstancesFromPropLayer(propLayer, authored, stampPos, brushRadius, surfaceSampler);

            // Rebuild tile buckets to guarantee per-tile range accuracy after this stamp.
            // (Multi-tile stamps interleave records; contiguous-range buckets need a full rebuild.)
            RebuildTileBuckets(authored);
            authored.PackBlob();
            EditorUtility.SetDirty(authored);
        }

        /// <summary>
        /// Places exactly one instance whose mesh-local <see cref="PropLayer.AnchorOffsetLocal"/>
        /// point lands exactly on <paramref name="exactPos"/> — the "place-from-anchor" pattern
        /// proven in commit 35e2def's <c>InstancePlacementTool.BuildRecord</c>.
        ///
        /// Stored position formula (must match <see cref="PropGhostPreview"/>):
        ///   <c>pivot = exactPos − rot · (anchor · scale)</c>
        /// At runtime the shader does <c>vertex = pivot + rot · (localPos · scale)</c>; at the
        /// anchor local point this evaluates back to <c>exactPos</c>, so the anchor — and any
        /// associated mesh feature (base / roots / hand grip) — lands on the cursor.
        ///
        /// Yaw is deterministic (no Random.value) so the placed instance is byte-identical to the
        /// ghost. AlignToNormal also matches the ghost.
        /// </summary>
        public void EmitExactlyOneAt(
            PropLayer                propLayer,
            Vector3                  exactPos,
            HeightmapSurfaceSampler? surfaceSampler,
            float                    yawDeg   = 0f,
            float                    scaleMul = 1f)
        {
            var authored = propLayer.AuthoredInstances;
            if (authored == null) return;

            Vector2 scaleRange = propLayer.OverrideScaleRange
                ? propLayer.ScaleRangeOverride
                : new Vector2(1f, 1f);
            // Use the midpoint of the range — matches the ghost preview exactly. Scale jitter is
            // applied to ghost-and-placement together (via the same seed-less midpoint), so the
            // ghost the user saw IS the instance they get.
            float scale = (scaleRange.x + scaleRange.y) * 0.5f;
            if (scale <= 0f) scale = 1f;

            // ── BYTE-IDENTICAL apply block — keep in lockstep with PropGhostPreview.OnSceneGui. ──
            //    The sticky E/R-adjust scale multiplier + yaw and the align-to-normal compose order
            //    (alignRot * yawRot) MUST match the ghost exactly, or the placed instance drifts.
            scale *= scaleMul;

            // Deterministic rotation — AlignToNormal + the sticky ghost yaw. NO random yaw.
            // Random yaw caused ghost ≠ placement mismatch (each click rolled a new yaw).
            Quaternion alignRot = Quaternion.identity;
            if (propLayer.PropAlignToNormal && surfaceSampler != null &&
                surfaceSampler.TrySample(exactPos.x, exactPos.z, out var hit) &&
                hit.Normal.sqrMagnitude > 0.01f)
            {
                alignRot = Quaternion.FromToRotation(Vector3.up, hit.Normal);
            }
            Quaternion rot = alignRot * Quaternion.Euler(0f, yawDeg, 0f);

            // Canonical place-from-anchor formula (mirror of commit 35e2def line 252).
            Vector3 anchor = propLayer.AnchorOffsetLocal;
            Vector3 pivot  = exactPos - rot * (anchor * scale);

            authored.AddRecord(new InstanceRecord
            {
                position     = pivot,
                rotation     = rot,
                scale        = scale,
                overrideMask = InstanceOverrideMask.None,
            });

            RebuildTileBuckets(authored);
            authored.PackBlob();
            EditorUtility.SetDirty(authored);
        }

        // ── Add instances ─────────────────────────────────────────────────────

        /// <summary>
        /// Unified-path version of <see cref="AddInstances"/> for <see cref="PropLayer"/>.
        /// Adapts the <see cref="PropLayer"/> property surface (no <c>ScaleRange</c> on the
        /// layer directly — derived from <see cref="PropLayer.OverrideScaleRange"/> or defaulted
        /// to (1,1)) and reads slope range from the PropLayer's <c>placement</c> field the same
        /// way as the legacy path.
        /// </summary>
        private void AddInstancesFromPropLayer(
            PropLayer                propLayer,
            AuthoredInstancesData    authored,
            Vector3                  stampPos,
            float                    brushRadius,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            // ScaleRange: PropLayer uses OverrideScaleRange + ScaleRangeOverride; default (1,1).
            Vector2 scaleRange = propLayer.OverrideScaleRange
                ? propLayer.ScaleRangeOverride
                : new Vector2(1f, 1f);

            float midScale = (scaleRange.x + scaleRange.y) * 0.5f;
            if (midScale <= 0f) midScale = 1f;

            // ScatterPlacementConfig (consumed via GetSlopeMaskDegForPropLayer) doesn't carry a
            // real slopeRange field — the helper falls back to (0, 45). Plumbing the sampler in
            // Phase 5 caused slopeDeg to actually compute (it used to always be 0 because the
            // sampler was null), so ANY non-flat brush stamp now rejected ~all jittered records
            // except whichever happened to land on the flattest spot. That made InstancePlace
            // look like it placed exactly one instance. Default to 90° (never reject) so the
            // user sees every emit unless they explicitly tighten it.
            float maxSlopeDeg = 90f;
            var slopeRange = GetSlopeMaskDegForPropLayer(propLayer);
            if (slopeRange.y > 0f) maxSlopeDeg = slopeRange.y;

            bool groundSnap    = propLayer.PropGroundSnap;
            bool alignToNormal = propLayer.PropAlignToNormal;

            // Single source of truth for prop placement — same anchor formula as
            // EmitExactlyOneAt + PropGhostPreview. The mesh-local anchor lands at the
            // ground-snapped point, then the pivot is back-computed so the runtime shader
            // (vertex = pivot + rot·(localPos·scale)) places anchor exactly at `pos`.
            Vector3 anchor = propLayer.AnchorOffsetLocal;

            for (int i = 0; i < this.DensityPerStamp; ++i)
            {
                float angle  = Random.value * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(Random.value) * brushRadius;
                var offset   = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var pos      = stampPos + offset;

                var rot = Quaternion.Euler(0f, Random.value * 360f, 0f);
                float slopeDeg = 0f;

                if (groundSnap && surfaceSampler != null &&
                    surfaceSampler.TrySample(pos.x, pos.z, out var hit))
                {
                    pos.y    = hit.Y;
                    slopeDeg = hit.SlopeDeg;

                    if (alignToNormal && hit.Normal.sqrMagnitude > 0.01f)
                    {
                        var normalRot = Quaternion.FromToRotation(Vector3.up, hit.Normal);
                        rot = normalRot * rot;
                    }
                }

                if (slopeDeg > maxSlopeDeg) continue;

                float scale = midScale * (1f + (Random.value * 2f - 1f) * this.ScaleJitter);
                scale = Mathf.Max(0.01f, scale);

                // Place-from-anchor: anchor at `pos`, pivot computed so vertex sampling lands back at `pos`.
                Vector3 pivot = pos - rot * (anchor * scale);

                authored.AddRecord(new InstanceRecord
                {
                    position     = pivot,
                    rotation     = rot,
                    scale        = scale,
                    overrideMask = InstanceOverrideMask.None,
                });
            }
        }

        // ── Delete mode ───────────────────────────────────────────────────────

        private void DeleteUnderBrush(
            AuthoredInstancesData authored,
            Vector3               stampPos,
            float                 brushRadius)
        {
            float radiusSq = brushRadius * brushRadius;
            var list       = authored.WorkingList;
            float stampX   = stampPos.x;
            float stampZ   = stampPos.z;

            // Iterate backwards to safely remove via swap-pop.
            for (int i = list.Count - 1; i >= 0; --i)
            {
                Vector3 pos = list[i].position;
                float dx = pos.x - stampX;
                float dz = pos.z - stampZ;
                if (dx * dx + dz * dz <= radiusSq)
                    authored.RemoveRecordSwapPop(i);
            }
            // Bucket rebuild handled in Emit after this method returns.
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the <see cref="PropLayer"/>'s slope range from its placement config (degrees).
        /// </summary>
        private static Vector2 GetSlopeMaskDegForPropLayer(PropLayer layer)
        {
            using var so = new SerializedObject(layer);
            var placementProp = so.FindProperty("placement");
            var slopeProp     = placementProp?.FindPropertyRelative("slopeRange");
            if (slopeProp == null) return new Vector2(0f, 45f);
            return slopeProp.vector2Value;
        }

        /// <summary>
        /// Rebuilds all tile bucket ranges from the current working list after swap-pop deletions
        /// may have shuffled records across tile boundaries.
        /// </summary>
        private static void RebuildTileBuckets(AuthoredInstancesData authored)
        {
            // Clear existing buckets by unregistering each known coord.
            var coords = new List<Vector2Int>(authored.TileCoordKeys);
            foreach (var coord in coords)
                authored.UnregisterTileBucket(coord);

            // Sort by tile coord first — contiguous grouping is what makes (first, count) ranges valid.
            var list = authored.WorkingList;
            list.Sort(TileCoordComparer.Instance);

            // Rebuild: iterate sorted list; adjacent same-coord records form a contiguous range.
            var perTile = new Dictionary<Vector2Int, (int first, int count)>();
            for (int i = 0; i < list.Count; i++)
            {
                var coord = TerrainWorldGrid.WorldToTileCoord(list[i].position.x, list[i].position.z);
                if (perTile.TryGetValue(coord, out var range))
                    perTile[coord] = (range.first, range.count + 1);
                else
                    perTile[coord] = (i, 1);
            }

            foreach (var kv in perTile)
                authored.RegisterTileBucket(kv.Key, kv.Value.first, kv.Value.count);
        }

        /// <summary>Stable tile-coord comparator: sorts by X then Y so same-tile records are contiguous.</summary>
        private sealed class TileCoordComparer : IComparer<InstanceRecord>
        {
            public static readonly TileCoordComparer Instance = new();

            public int Compare(InstanceRecord a, InstanceRecord b)
            {
                var ca = TerrainWorldGrid.WorldToTileCoord(a.position.x, a.position.z);
                var cb = TerrainWorldGrid.WorldToTileCoord(b.position.x, b.position.z);
                int cmp = ca.x.CompareTo(cb.x);
                return cmp != 0 ? cmp : ca.y.CompareTo(cb.y);
            }
        }
    }
}
