#nullable enable
using Unity.Collections;
using UnityEngine;

namespace GrassInteract
{
    /// <summary>
    /// Per-frame frustum + distance culler for <see cref="InstanceColliderPool"/>.
    /// Runs in <c>LateUpdate</c> to acquire/release pooled colliders based on camera visibility.
    ///
    /// Throttled to run every <see cref="TEST_INTERVAL_FRAMES"/> frames OR when the camera moves
    /// more than <see cref="CAMERA_MOVE_THRESHOLD"/> metres, to avoid per-frame overhead on large layers.
    ///
    /// Falls back to <c>Camera.main</c> when the injected camera is null. If even Camera.main is null
    /// (e.g. headless test) culling is skipped gracefully.
    /// </summary>
    internal sealed class InstanceFrustumCuller : MonoBehaviour
    {
        // ── Throttle constants ────────────────────────────────────────────────

        private const int   TEST_INTERVAL_FRAMES  = 4;
        private const float CAMERA_MOVE_THRESHOLD = 0.5f;

        // ── Config (set via Init) ─────────────────────────────────────────────

        private Camera?              targetCamera;
        private float                cullDistance;
        private InstanceColliderPool? pool;

        // ── Record snapshot (set via SetRecords) ─────────────────────────────

        // Parallel arrays; index matches the authored record index.
        private Vector3[]     recordPositions  = System.Array.Empty<Vector3>();
        private Quaternion[]  recordRotations  = System.Array.Empty<Quaternion>();
        private float[]       recordScales     = System.Array.Empty<float>();
        private Mesh?[]       recordMeshes     = System.Array.Empty<Mesh?>();
        private bool[]        recordConvex     = System.Array.Empty<bool>();
        private bool[]        recordWants      = System.Array.Empty<bool>(); // generateCollider flag

        // ── Frame-throttle state ──────────────────────────────────────────────

        private int     frameCounter;
        private Vector3 lastCamPos;
        private bool    initialized;

        // ── Scratch (no alloc per frame) ──────────────────────────────────────

        private readonly Plane[] planeScratch = new Plane[6];

        // ── Initialization ────────────────────────────────────────────────────

        /// <summary>Configures the culler. Call once after component creation.</summary>
        public void Init(Camera? cam, float distance, InstanceColliderPool pool)
        {
            this.targetCamera  = cam;
            this.cullDistance  = Mathf.Max(0f, distance);
            this.pool          = pool;
            this.initialized   = true;
        }

        /// <summary>
        /// Provides the record snapshot arrays. Caller retains ownership of the mesh references.
        /// Arrays must all be the same length.
        /// </summary>
        public void SetRecords(
            Vector3[]    positions,
            Quaternion[] rotations,
            float[]      scales,
            Mesh?[]      meshes,
            bool[]       convex,
            bool[]       wantsCollider)
        {
            this.recordPositions = positions;
            this.recordRotations = rotations;
            this.recordScales    = scales;
            this.recordMeshes    = meshes;
            this.recordConvex    = convex;
            this.recordWants     = wantsCollider;
        }

        // ── MonoBehaviour ─────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!this.initialized || this.pool == null) return;

            Camera? cam = this.targetCamera != null ? this.targetCamera : Camera.main;
            if (cam == null) return; // headless / no camera — skip gracefully

            Vector3 camPos = cam.transform.position;

            // Throttle: skip unless interval elapsed OR camera moved significantly.
            this.frameCounter++;
            float moveSqr = (camPos - this.lastCamPos).sqrMagnitude;
            bool intervalElapsed = (this.frameCounter % TEST_INTERVAL_FRAMES) == 0;
            bool cameraMoved     = moveSqr > CAMERA_MOVE_THRESHOLD * CAMERA_MOVE_THRESHOLD;

            if (!intervalElapsed && !cameraMoved) return;

            this.lastCamPos = camPos;
            this.RunCull(cam, camPos);
        }

        private void OnDestroy()
        {
            this.pool?.ReleaseAll();
        }

        // ── Cull pass ─────────────────────────────────────────────────────────

        private void RunCull(Camera cam, Vector3 camPos)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, this.planeScratch);

            float cullSqr = this.cullDistance * this.cullDistance;
            int count = this.recordWants.Length;

            for (int i = 0; i < count; ++i)
            {
                if (!this.recordWants[i]) continue;

                Vector3 pos = this.recordPositions[i];

                // Distance test.
                float distSqr = (pos - camPos).sqrMagnitude;
                bool inRange = distSqr <= cullSqr;

                // Frustum test (only when in range — avoids unnecessary plane math for distant records).
                bool visible = inRange && this.IsInFrustum(pos);

                if (visible)
                {
                    this.pool!.Acquire(
                        i,
                        pos,
                        this.recordRotations[i],
                        this.recordScales[i],
                        this.recordMeshes[i],
                        this.recordConvex[i]);
                }
                else
                {
                    this.pool!.Release(i);
                }
            }
        }

        private bool IsInFrustum(Vector3 pos)
        {
            // Point-in-frustum test: inside when on the positive side of all 6 planes.
            for (int p = 0; p < 6; ++p)
            {
                Plane plane = this.planeScratch[p];
                if (plane.GetDistanceToPoint(pos) < 0f)
                    return false;
            }
            return true;
        }
    }
}
