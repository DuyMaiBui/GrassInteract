#nullable enable
using UnityEngine;

namespace WorldPainter.Editor
{
    /// <summary>
    /// Sticky yaw + uniform-scale override for the prop <b>Place</b> tool's ghost preview, driven by
    /// on-canvas transform handles.
    ///
    /// While the artist holds <c>E</c> (rotate) or <c>R</c> (scale) in the Scene view, the ghost
    /// <b>freezes</b> at its current position (<see cref="FrozenPainting"/>) and
    /// <see cref="WorldPainterSculptTool"/> draws a rotation / scale <see cref="UnityEditor.Handles"/>
    /// gizmo on it. The mouse is then free to drag that handle — the resulting
    /// <see cref="GhostYawDeg"/> / <see cref="GhostScaleMul"/> feed BOTH the ghost preview
    /// (<see cref="PropGhostPreview.Draw"/>) and the placement
    /// (<see cref="WorldPainterPropStampEmitter.EmitExactlyOneAt"/>), so what the artist dials in IS
    /// what lands. Releasing the key un-freezes the ghost (it resumes following the cursor) while the
    /// dialled-in values persist — they are <b>sticky</b> across placements until <see cref="Reset"/>
    /// (Esc in Place mode).
    ///
    /// GPU constraint: props render yaw-only rotation + uniform scale (the 16+16-bit
    /// <c>packedYawScale</c> field). E shows a yaw disc; R shows a uniform-scale handle — pitch/roll
    /// and non-uniform scale are not representable in this path.
    /// </summary>
    internal static class PropPlaceGhostController
    {
        private const float MIN_SCALE_MUL = 0.01f;

        // ── Sticky dialled-in values ──────────────────────────────────────────

        /// <summary>Yaw (degrees, 0–360) applied to the placed prop about up. Sticky.</summary>
        public static float GhostYawDeg { get; private set; }

        /// <summary>Uniform scale multiplier on the layer's mid-scale. Sticky. Default 1.</summary>
        public static float GhostScaleMul { get; private set; } = 1f;

        // ── Held-key + freeze state ───────────────────────────────────────────

        /// <summary>True while the Rotate (E) key is held.</summary>
        public static bool RotateHeld { get; private set; }

        /// <summary>True while the Scale (R) key is held.</summary>
        public static bool ScaleHeld { get; private set; }

        /// <summary>True when either adjust key is held — the ghost is frozen and placement suppressed.</summary>
        public static bool IsAdjusting => RotateHeld || ScaleHeld;

        /// <summary>True once a frozen anchor has been captured for the current hold.</summary>
        public static bool HasFrozen { get; private set; }

        /// <summary>Painting-space anchor the ghost is pinned to while adjusting.</summary>
        public static Vector3 FrozenPainting { get; private set; }

        /// <summary>Last painting-space point the live (cursor-following) ghost occupied.</summary>
        public static Vector3 LastGhostPainting { get; private set; }

        // ── Held-key setters (driven by the sculpt tool's KeyDown / KeyUp) ────

        public static void SetRotateHeld(bool held) => RotateHeld = held;
        public static void SetScaleHeld(bool held)  => ScaleHeld  = held;

        // ── Freeze lifecycle ──────────────────────────────────────────────────

        /// <summary>Records where the live ghost currently sits, so the next freeze pins to it.</summary>
        public static void RecordGhostPoint(Vector3 painting) => LastGhostPainting = painting;

        /// <summary>
        /// Pins the ghost to <paramref name="painting"/> for the duration of the hold. No-op once a
        /// freeze is already active, so holding a second key (E then R) keeps the same anchor.
        /// </summary>
        public static void CaptureFrozen(Vector3 painting)
        {
            if (HasFrozen) return;
            FrozenPainting = painting;
            HasFrozen      = true;
        }

        /// <summary>Releases the frozen anchor — the ghost resumes following the cursor.</summary>
        public static void ClearFrozen() => HasFrozen = false;

        /// <summary>
        /// Drops both held flags and any frozen anchor. Called when the Scene view loses the mouse or
        /// the active tool leaves Place — a KeyUp delivered to another window would otherwise never
        /// reach the sculpt-tool handler, latching a channel ON and freezing the ghost forever.
        /// </summary>
        public static void ClearHeld()
        {
            RotateHeld = false;
            ScaleHeld  = false;
            HasFrozen  = false;
        }

        // ── Handle write-back ─────────────────────────────────────────────────

        /// <summary>Sets the sticky yaw from the rotation handle (wrapped to 0–360).</summary>
        public static void SetYaw(float deg) => GhostYawDeg = Mathf.Repeat(deg, 360f);

        /// <summary>Sets the sticky uniform-scale multiplier from the scale handle (clamped &gt; 0).</summary>
        public static void SetScaleMul(float mul) => GhostScaleMul = Mathf.Max(MIN_SCALE_MUL, mul);

        /// <summary>Resets the sticky yaw/scale back to identity (Esc in Place mode).</summary>
        public static void Reset()
        {
            GhostYawDeg   = 0f;
            GhostScaleMul = 1f;
        }
    }
}
