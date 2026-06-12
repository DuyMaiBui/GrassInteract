#nullable enable
using UnityEngine;
using WorldPainter;

namespace WorldPainter
{
    /// <summary>
    /// Runtime impostor/billboard far-LOD selection for dense prop instances.
    ///
    /// Beyond <see cref="ImpostorDistanceM"/> from the camera, instances switch to an
    /// impostor/billboard mesh (LOD last entry in the layer's <see cref="ScatterRenderConfig"/>).
    /// Inside the threshold, the full LOD0 mesh is used.
    ///
    /// Ships in the runtime (WorldPainter) assembly. MUST NOT reference any UnityEditor types
    /// outside <c>#if UNITY_EDITOR</c> blocks (enforced by this file's runtime asmdef).
    ///
    /// Thread-safety: main-thread only (called from LateUpdate / cull path).
    ///
    /// Phase 4 task 6.
    /// </summary>
    public sealed class WorldPainterImpostorLod
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Distance in metres beyond which instances use the billboard LOD.</summary>
        public const float DEFAULT_IMPOSTOR_DISTANCE_M = 60f;

        // ── Config ────────────────────────────────────────────────────────────

        /// <summary>Distance threshold in world-space metres.</summary>
        public float ImpostorDistanceM { get; set; } = DEFAULT_IMPOSTOR_DISTANCE_M;

        /// <summary>Squared threshold (cached for per-instance comparisons).</summary>
        private float impostorDistanceSq = DEFAULT_IMPOSTOR_DISTANCE_M * DEFAULT_IMPOSTOR_DISTANCE_M;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>Updates the cached squared distance when the threshold changes.</summary>
        public void SetImpostorDistance(float distanceM)
        {
            this.ImpostorDistanceM  = Mathf.Max(0f, distanceM);
            this.impostorDistanceSq = this.ImpostorDistanceM * this.ImpostorDistanceM;
        }

        // ── LOD selection ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the LOD index to use for an instance at <paramref name="instancePos"/>
        /// given the camera at <paramref name="cameraPos"/>.
        ///
        /// Returns 0  (full mesh) when inside the impostor distance threshold.
        /// Returns the last LOD index when beyond the threshold (billboard/impostor).
        /// </summary>
        public int SelectLod(
            Vector3           instancePos,
            Vector3           cameraPos,
            InstanceScatterLayer layer)
        {
            int lodCount = layer.Render.Lods.Length;
            if (lodCount <= 1) return 0;

            float dx = instancePos.x - cameraPos.x;
            float dz = instancePos.z - cameraPos.z;
            // Y ignored for distance check (XZ planar distance).
            float distSq = dx * dx + dz * dz;

            return distSq >= this.impostorDistanceSq ? lodCount - 1 : 0;
        }

        /// <summary>
        /// Batch LOD selection: fills <paramref name="lodIndicesOut"/> (must be pre-allocated)
        /// with the LOD index for each instance position.
        /// Returns the number of instances using the impostor LOD.
        /// </summary>
        public int SelectLodBatch(
            Vector3[]          instancePositions,
            int                count,
            Vector3            cameraPos,
            InstanceScatterLayer layer,
            int[]              lodIndicesOut)
        {
            int lodCount    = layer.Render.Lods.Length;
            int lastLod     = lodCount - 1;
            int impostorCnt = 0;

            for (int i = 0; i < count; ++i)
            {
                Vector3 pos = instancePositions[i];
                float dx = pos.x - cameraPos.x;
                float dz = pos.z - cameraPos.z;
                float distSq = dx * dx + dz * dz;

                if (lodCount <= 1 || distSq < this.impostorDistanceSq)
                {
                    lodIndicesOut[i] = 0;
                }
                else
                {
                    lodIndicesOut[i] = lastLod;
                    ++impostorCnt;
                }
            }

            return impostorCnt;
        }

        /// <summary>
        /// Returns true when the instance at <paramref name="instancePos"/> should use the
        /// impostor/billboard LOD (i.e. is beyond the distance threshold from the camera).
        /// </summary>
        public bool IsImpostor(Vector3 instancePos, Vector3 cameraPos)
        {
            float dx = instancePos.x - cameraPos.x;
            float dz = instancePos.z - cameraPos.z;
            return dx * dx + dz * dz >= this.impostorDistanceSq;
        }
    }
}
