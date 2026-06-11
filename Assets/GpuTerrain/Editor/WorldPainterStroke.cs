#nullable enable
using UnityEngine;

namespace GpuTerrain.Editor
{
    /// <summary>
    /// Spacing-stamping stroke model for the WorldPainter brush (design §5.4).
    ///
    /// Interpolates the drag path and stamps every <c>spacingM</c> metres.
    /// Flow accumulates per stamp — result is independent of mouse speed.
    ///
    /// Thread-safety: main-thread only (EditorTool / OnToolGUI).
    /// </summary>
    internal sealed class WorldPainterStroke
    {
        // ── State ─────────────────────────────────────────────────────────────

        private Vector3 lastStampPosition;
        private float   distanceAccumulator;
        private bool    inStroke;

        // ── Public API ────────────────────────────────────────────────────────

        public bool InStroke => this.inStroke;

        /// <summary>Begin a new stroke at <paramref name="worldPos"/>.</summary>
        public void Begin(Vector3 worldPos)
        {
            this.lastStampPosition   = worldPos;
            this.distanceAccumulator = 0f;
            this.inStroke            = true;
        }

        /// <summary>Signal stroke end (no stamp — caller does final commit).</summary>
        public void End() => this.inStroke = false;

        /// <summary>
        /// Advance the drag path to <paramref name="worldPos"/>.
        /// Invokes <paramref name="onStamp"/> for each spacing interval crossed,
        /// passing the interpolated world position and the accumulated flow weight.
        ///
        /// Returns the total number of stamps emitted this call (0 = not far enough yet).
        /// </summary>
        public int Advance(
            Vector3 worldPos,
            float spacingM,
            float flow,
            System.Action<Vector3, float> onStamp)
        {
            if (!this.inStroke) return 0;
            if (spacingM < 0.001f) spacingM = 0.001f;

            float segLen = Vector3.Distance(this.lastStampPosition, worldPos);
            if (segLen < 0.0001f) return 0;

            this.distanceAccumulator += segLen;

            int stamps = 0;
            Vector3 dir = (worldPos - this.lastStampPosition).normalized;

            while (this.distanceAccumulator >= spacingM)
            {
                this.distanceAccumulator -= spacingM;
                // Walk back from worldPos: stamp at the point that crosses the threshold.
                float overshoot = this.distanceAccumulator;
                Vector3 stampPos = worldPos - dir * overshoot;
                onStamp(stampPos, flow);
                ++stamps;
            }

            this.lastStampPosition = worldPos;
            return stamps;
        }

        // ── Math helpers (deterministic, tested) ──────────────────────────────

        /// <summary>
        /// Pure-math: how many stamps fit in a drag segment of <paramref name="distM"/> metres
        /// with <paramref name="spacingM"/> spacing, starting from <paramref name="startAccum"/>
        /// accumulated distance?
        ///
        /// Returns the stamp count and the new accumulated distance.
        /// Used by the spacing-stamp math test (task 7 verify).
        /// </summary>
        public static int CountStamps(
            float distM,
            float spacingM,
            float startAccum,
            out float endAccum)
        {
            if (spacingM < 0.001f) spacingM = 0.001f;
            float total = startAccum + distM;
            int   count = (int)(total / spacingM);
            endAccum    = total - count * spacingM;
            return count;
        }
    }
}
