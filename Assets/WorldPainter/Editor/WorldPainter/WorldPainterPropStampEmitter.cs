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
        /// Emits jittered <see cref="InstanceRecord"/>s into <paramref name="layer"/> at
        /// <paramref name="stampPos"/> with the given brush radius.
        ///
        /// Applies the layer's anchor config (ground-snap / align-to-normal / pivot offset).
        /// Writes each record into the per-tile bucket of the tile it lands in.
        /// On Shift (<paramref name="deleteMode"/>=true) removes records under the brush.
        /// </summary>
        public void Emit(
            InstanceScatterLayer     layer,
            Vector3                  stampPos,
            float                    brushRadius,
            bool                     deleteMode,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            var authored = layer.AuthoredInstances;
            if (authored == null) return;

            if (deleteMode)
                this.DeleteUnderBrush(authored, stampPos, brushRadius);
            else
                this.AddInstances(layer, authored, stampPos, brushRadius, surfaceSampler);

            // Rebuild tile buckets to guarantee per-tile range accuracy after this stamp.
            // (Multi-tile stamps interleave records; contiguous-range buckets need a full rebuild.)
            RebuildTileBuckets(authored);
            authored.PackBlob();
            EditorUtility.SetDirty(authored);
        }

        /// <summary>Returns the density-per-stamp label string for display in the card.</summary>
        public static string GetDensityLabel(InstanceScatterLayer layer)
        {
            // Density-per-stamp is a tool-side config, not on the layer asset.
            return $"{DEFAULT_DENSITY_PER_STAMP} per stamp";
        }

        // ── Add instances ─────────────────────────────────────────────────────

        private void AddInstances(
            InstanceScatterLayer     layer,
            AuthoredInstancesData    authored,
            Vector3                  stampPos,
            float                    brushRadius,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            var scaleRange = layer.ScaleRange;
            float midScale = (scaleRange.x + scaleRange.y) * 0.5f;
            if (midScale <= 0f) midScale = 1f;

            // Read slope range from the layer's placement config.
            float maxSlopeDeg = 45f;
            var slopeRange = GetSlopeMaskDeg(layer);
            if (slopeRange.y > 0f) maxSlopeDeg = slopeRange.y;

            // Anchor config from the layer.
            bool groundSnap    = layer.PropGroundSnap;
            bool alignToNormal = layer.PropAlignToNormal;
            Vector3 pivotOffset = layer.PropPivotOffset;

            for (int i = 0; i < this.DensityPerStamp; ++i)
            {
                // Random offset within the brush disc (uniform distribution).
                float angle  = Random.value * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(Random.value) * brushRadius;
                var offset   = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var pos      = stampPos + offset;

                // Determine base yaw rotation (full random around Y).
                var rot = Quaternion.Euler(0f, Random.value * 360f, 0f);

                float slopeDeg = 0f;

                // Ground-snap + normal-align via surface sampler.
                if (groundSnap && surfaceSampler != null &&
                    surfaceSampler.TrySample(pos.x, pos.z, out var hit))
                {
                    pos.y    = hit.Y;
                    slopeDeg = hit.SlopeDeg;

                    if (alignToNormal && hit.Normal.sqrMagnitude > 0.01f)
                    {
                        // Align instance up-axis to surface normal, preserving yaw.
                        var normalRot = Quaternion.FromToRotation(Vector3.up, hit.Normal);
                        rot = normalRot * rot;
                    }
                }

                if (slopeDeg > maxSlopeDeg) continue;

                // Apply pivot offset (local → world: rotate by base orientation).
                pos += rot * pivotOffset;

                // Scale jitter.
                float scale = midScale * (1f + (Random.value * 2f - 1f) * this.ScaleJitter);
                scale = Mathf.Max(0.01f, scale);

                // Append to the flat working list; RebuildTileBuckets (called after the loop)
                // will assign correct per-tile bucket ranges for all appended records.
                authored.AddRecord(new InstanceRecord
                {
                    position     = pos,
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

        /// <summary>Returns the layer's slope range from placement config (degrees).</summary>
        private static Vector2 GetSlopeMaskDeg(InstanceScatterLayer layer)
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

            // Rebuild: group working-list indices by tile coord and register contiguous ranges.
            // Because swap-pop can interleave records from different tiles, we must do a
            // full pass rather than assuming contiguity.
            var list = authored.WorkingList;

            // Collect index lists per tile.
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
    }
}
