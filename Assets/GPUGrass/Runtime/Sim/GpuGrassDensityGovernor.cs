#nullable enable
using UnityEngine;

namespace GPUGrass
{
    /// <summary>
    /// Frame-time-driven adaptive density: smooths the per-frame time, compares it to a target frame
    /// budget, and walks a density fraction (0..1) DOWN when the device is over budget (thinning grass to
    /// recover FPS / reduce heat) and back UP when there's headroom. A configurable floor keeps the field
    /// from ever going bald. The output maps to the cull compute's <c>densityThreshold</c> (GPU-side skip),
    /// so thinning costs nothing extra — it just draws fewer blades.
    ///
    /// Self-contained (no WorldPainter / PerformanceConsole dependency): the controller feeds it
    /// <c>Time.unscaledDeltaTime</c> each play-mode frame. Pure logic → unit-testable.
    /// </summary>
    internal sealed class GpuGrassDensityGovernor
    {
        // EMA window for the frame-time signal (seconds). Long enough to ignore single-frame spikes.
        private const float SMOOTH_WINDOW_SEC = 0.5f;
        // Density change rate (fraction per second). Full 1→floor traversal takes ~ (1-floor)/RATE seconds.
        private const float ADAPT_RATE_PER_SEC = 0.5f;
        // Hysteresis: thin only above target×HI, restore only below target×LO (dead-band prevents hunting).
        private const float OVER_BUDGET_FACTOR  = 1.02f;
        private const float HEADROOM_FACTOR      = 0.90f;

        private float smoothedMs;
        private float density = 1f;

        /// <summary>Current density fraction (0..1). 1 = full.</summary>
        public float Density => this.density;

        /// <summary>Smoothed frame time in ms (diagnostics).</summary>
        public float SmoothedMs => this.smoothedMs;

        /// <summary>Resets to full density (call on (re)build).</summary>
        public void Reset()
        {
            this.smoothedMs = 0f;
            this.density = 1f;
        }

        /// <summary>
        /// Advances the governor one frame and returns the new density fraction (clamped to
        /// [<paramref name="minDensity"/>, 1]). <paramref name="dtSeconds"/> is unscaled frame time.
        /// </summary>
        public float Tick(float dtSeconds, float targetFps, float minDensity)
        {
            dtSeconds = Mathf.Max(dtSeconds, 1e-5f);
            float dtMs = dtSeconds * 1000f;

            // EMA toward the latest frame time (weight grows with dt so it's framerate-independent).
            float a = Mathf.Clamp01(dtSeconds / SMOOTH_WINDOW_SEC);
            this.smoothedMs = this.smoothedMs <= 0f ? dtMs : Mathf.Lerp(this.smoothedMs, dtMs, a);

            float targetMs = 1000f / Mathf.Max(targetFps, 1f);
            float floor    = Mathf.Clamp01(minDensity);

            if (this.smoothedMs > targetMs * OVER_BUDGET_FACTOR)
                this.density -= ADAPT_RATE_PER_SEC * dtSeconds;          // over budget → thin
            else if (this.smoothedMs < targetMs * HEADROOM_FACTOR)
                this.density += ADAPT_RATE_PER_SEC * dtSeconds;          // headroom → restore

            this.density = Mathf.Clamp(this.density, floor, 1f);
            return this.density;
        }
    }
}
