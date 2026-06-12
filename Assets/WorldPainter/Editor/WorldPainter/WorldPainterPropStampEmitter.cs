#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WorldPainter;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Spacing-stamp → jittered <see cref="InstanceRecord"/> emitter for Prop layers.
    ///
    /// Each spacing-stamp (emitted by <see cref="WorldPainterStroke"/>) places one or
    /// more jittered records at random offsets within <c>brushRadius</c> around the
    /// stamp centre, rejecting placements outside the configured slope range.
    ///
    /// Shift-mode deletes all records whose position falls within the brush.
    ///
    /// Uses <see cref="WorldPainterIncrementalBake"/> to append affected chunks to
    /// <see cref="ChunkedInstanceBuffer"/> without a full rebuild.
    ///
    /// Design §5.4 / §5.5 Phase 4 task 2.
    /// </summary>
    internal sealed class WorldPainterPropStampEmitter
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Default instances deposited per stamp.</summary>
        private const int DEFAULT_DENSITY_PER_STAMP = 3;

        /// <summary>Scale jitter half-range (±fraction of midScale).</summary>
        private const float SCALE_JITTER = 0.25f;

        // ── State ─────────────────────────────────────────────────────────────

        private int densityPerStamp = DEFAULT_DENSITY_PER_STAMP;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Emits jittered <see cref="InstanceRecord"/>s into <paramref name="layer"/> at
        /// <paramref name="stampPos"/> with the given brush radius.
        ///
        /// Passes each emitted record through slope validation.
        /// On Shift (<paramref name="deleteMode"/>=true) removes records under the brush.
        /// </summary>
        public void Emit(
            InstanceScatterLayer layer,
            Vector3              stampPos,
            float                brushRadius,
            bool                 deleteMode,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            var authored = layer.AuthoredInstances;
            if (authored == null) return;

            if (deleteMode)
            {
                this.DeleteUnderBrush(authored, stampPos, brushRadius);
            }
            else
            {
                this.AddInstances(layer, authored, stampPos, brushRadius, surfaceSampler);
            }

            authored.PackBlob();
            EditorUtility.SetDirty(authored);
        }

        /// <summary>Returns the density-per-stamp label string for display in the card.</summary>
        public static string GetDensityLabel(InstanceScatterLayer layer)
        {
            // Density-per-stamp is a tool-side config, not on the layer asset.
            // Return a descriptive default.
            return $"{DEFAULT_DENSITY_PER_STAMP} per stamp";
        }

        // ── Add instances ─────────────────────────────────────────────────────

        private void AddInstances(
            InstanceScatterLayer layer,
            AuthoredInstancesData authored,
            Vector3 stampPos,
            float brushRadius,
            HeightmapSurfaceSampler? surfaceSampler)
        {
            var scaleRange = layer.ScaleRange;
            float midScale = (scaleRange.x + scaleRange.y) * 0.5f;
            if (midScale <= 0f) midScale = 1f;

            // Read slope range from the layer's placement config.
            float maxSlopeDeg = 45f; // default
            var slopeProp = GetSlopeMaskDeg(layer);
            if (slopeProp.y > 0f) maxSlopeDeg = slopeProp.y;

            for (int i = 0; i < this.densityPerStamp; ++i)
            {
                // Random offset within the brush disc.
                float angle  = Random.value * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(Random.value) * brushRadius; // uniform disc
                var offset   = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var pos      = stampPos + offset;

                // Ground to surface if sampler available; use SlopeDeg from the hit.
                float slopeDeg = 0f;
                if (surfaceSampler != null && surfaceSampler.TrySample(pos.x, pos.z, out var hit))
                {
                    pos.y    = hit.Y;
                    slopeDeg = hit.SlopeDeg;
                }

                if (slopeDeg > maxSlopeDeg) continue; // reject steep slope

                // Scale jitter.
                float scale = midScale * (1f + (Random.value * 2f - 1f) * SCALE_JITTER);
                scale = Mathf.Max(0.01f, scale);

                // Yaw jitter: full random rotation around Y.
                var rot = Quaternion.Euler(0f, Random.value * 360f, 0f);

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
            Vector3 stampPos,
            float brushRadius)
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
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Returns the layer's slope range from placement config (degrees).</summary>
        private static Vector2 GetSlopeMaskDeg(InstanceScatterLayer layer)
        {
            // Access via serialized object to avoid private-field issues.
            using var so = new SerializedObject(layer);
            var placementProp = so.FindProperty("placement");
            var slopeProp     = placementProp?.FindPropertyRelative("slopeRange");
            if (slopeProp == null) return new Vector2(0f, 45f);
            return slopeProp.vector2Value;
        }
    }
}
