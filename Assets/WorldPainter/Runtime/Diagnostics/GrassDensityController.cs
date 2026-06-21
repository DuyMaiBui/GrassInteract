#nullable enable
using UnityEngine;

namespace WorldPainter.Diagnostics
{
    /// <summary>
    /// Phase 3 — Adaptive grass density controller.
    ///
    /// Reads the smoothed frame-time signal from <see cref="PerformanceConsole.SmoothedFrameMs"/>
    /// (the same SSOT used by <see cref="RenderScaleGovernor"/>) and drives a per-frame
    /// density scalar into every active <see cref="GrassGpuEngine"/> via
    /// <see cref="GrassGpuEngine.SetDensity"/>.
    ///
    /// Coordination with the resolution governor (density-first discipline):
    ///   Density acts as the FIRST / cheapest knob. The governor only steps render-scale when
    ///   the frame is overloaded AND density is already at its floor. This is implemented by
    ///   reading <see cref="RenderScaleGovernor.DensityScalar"/>: when the governor drives scale
    ///   down, it also emits a DensityScalar &lt; 1, which we COMBINE with our own frame-time
    ///   signal. This way both controllers reinforce rather than fight each other.
    ///
    /// Hysteresis: separate raise / lower thresholds (TARGET_MS ± HYST_MS) prevent rapid
    /// oscillation between density levels. A step cooldown further suppresses pumping.
    ///
    /// Density floor: encoded in <see cref="GrassGpuEngine.SetDensity"/> — normalized 0 maps to
    /// BladeCull threshold 160/256 ≈ 62%, never going bald regardless of load.
    ///
    /// Auto-boots via <see cref="RuntimeInitializeOnLoadMethod"/>; no scene setup required.
    /// Disabled by the same <c>WP_PERF_CONSOLE_OFF</c> define as PerformanceConsole.
    /// </summary>
    public sealed class GrassDensityController : MonoBehaviour
    {
        // ── Config constants ──────────────────────────────────────────────────
        /// <summary>Target frame time (ms) — ~60 FPS with a small safety margin.</summary>
        private const float TARGET_MS = 15.5f;

        /// <summary>
        /// Hysteresis half-band (ms). Density only decreases when avgMs &gt; TARGET_MS + HYST_MS,
        /// and only increases when avgMs &lt; TARGET_MS - HYST_MS. Prevents rapid oscillation.
        /// </summary>
        private const float HYST_MS = 1.2f;

        /// <summary>Minimum frames between density adjustments (~0.3 s at 60 FPS).</summary>
        private const int COOLDOWN_FRAMES = 18;

        /// <summary>Density step size per adjustment (normalized 0..1).</summary>
        private const float DENSITY_STEP = 0.125f; // 8 steps across the [0,1] range

        /// <summary>
        /// Maximum density contribution from frame-time (normalized 0..1). The final density
        /// passed to SetDensity is min(frameDensity, governorDensity) so the resolution governor
        /// can suppress density independently without the frame-time controller fighting back.
        /// </summary>
        private const float DENSITY_FULL = 1.0f;

        // ── Bootstrap ─────────────────────────────────────────────────────────
        private static GrassDensityController? instance;

#if !WP_PERF_CONSOLE_OFF
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("[GrassDensityController]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GrassDensityController>();
        }
#endif

        // ── Controller state ──────────────────────────────────────────────────
        // Current frame-time-derived density, separate from governor contribution.
        private float frameDensity = DENSITY_FULL;
        private int cooldownFrames;

        // ── Scene scanning ────────────────────────────────────────────────────
        // GrassGpuEngine instances are internal; we reach them through WorldPainter →
        // ScatterField. Poll for WorldPainter objects at low frequency to handle scene changes.
        private WorldPainter? worldPainter;
        private float nextFindTime;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void LateUpdate()
        {
            this.PollWorldPainter();
            this.UpdateDensity();
        }

        // ── Private: WorldPainter discovery ──────────────────────────────────
        private void PollWorldPainter()
        {
            if (this.worldPainter != null) return;
            if (Time.unscaledTime < this.nextFindTime) return;
            this.nextFindTime = Time.unscaledTime + 1f;
#if UNITY_2023_1_OR_NEWER
            this.worldPainter = Object.FindFirstObjectByType<WorldPainter>();
#else
            this.worldPainter = Object.FindObjectOfType<WorldPainter>();
#endif
        }

        // ── Private: density PI / step controller ────────────────────────────
        private void UpdateDensity()
        {
            float smoothedMs = PerformanceConsole.SmoothedFrameMs;

            // Wait for a valid signal from the console ring buffer.
            if (smoothedMs < 0.001f) return;

            // Hysteresis lower / raise thresholds.
            float lowerThreshold = TARGET_MS + HYST_MS; // overloaded → reduce density
            float raiseThreshold = TARGET_MS - HYST_MS; // headroom  → restore density

            if (this.cooldownFrames > 0)
            {
                this.cooldownFrames--;
            }
            else if (smoothedMs > lowerThreshold && this.frameDensity > 0f)
            {
                // Over budget → step density down.
                this.frameDensity = Mathf.Clamp01(this.frameDensity - DENSITY_STEP);
                this.cooldownFrames = COOLDOWN_FRAMES;
            }
            else if (smoothedMs < raiseThreshold && this.frameDensity < DENSITY_FULL)
            {
                // Under budget → step density up toward full.
                this.frameDensity = Mathf.Clamp01(this.frameDensity + DENSITY_STEP);
                this.cooldownFrames = COOLDOWN_FRAMES;
            }

            // Combine with resolution governor's density scalar:
            // use the MINIMUM so both controllers cooperate rather than fight.
            // DensityScalar == 1 when the governor is idle (scale at ceil) → frame-time wins.
            // DensityScalar < 1 when the governor is reducing scale → combined drops further.
            float combinedDensity = Mathf.Min(this.frameDensity, RenderScaleGovernor.DensityScalar);

            // Push to all active GrassGpuEngines via WorldPainter → ScatterField.
            this.PushDensityToEngines(combinedDensity);

            // Report to PerformanceConsole overlay.
            PerformanceConsole.Report("dens",
                $"{combinedDensity:0.00}  (ft={this.frameDensity:0.00} gov={RenderScaleGovernor.DensityScalar:0.00})");
        }

        // ── Private: fan-out to GrassGpuEngine instances ─────────────────────
        private void PushDensityToEngines(float normalizedDensity)
        {
            if (this.worldPainter == null) return;

            // WorldPainter.SurfaceLayers exposes SetAllGrassDensity (internal, same assembly)
            // which fans out to every active GrassGpuEngine via the ScatterPreBuiltEngineWrapper.
            // GrassCpuEngine instances are silently skipped inside that method.
            this.worldPainter.SetAllGrassDensity(normalizedDensity);
        }
    }
}
