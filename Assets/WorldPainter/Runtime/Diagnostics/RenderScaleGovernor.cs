#nullable enable
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldPainter.Diagnostics
{
    /// <summary>
    /// Frame-time-driven dynamic-resolution governor for mobile 60 FPS.
    ///
    /// Reads <see cref="PerformanceConsole.SmoothedFrameMs"/> (0.5 s ring-buffer average — the
    /// SSOT adaptive signal shared with the Phase 3 grass density controller). Drives URP render
    /// scale via the same reflection setter used by PerformanceConsole, so there is no typed URP
    /// asmdef dependency from WorldPainter.asmdef.
    ///
    /// PI controller with hysteresis dead-band and step cooldown to prevent visible resolution
    /// pumping (brainstorm risk R6). Emits current state to the on-screen console via
    /// <see cref="PerformanceConsole.Report"/>.
    ///
    /// Render-scale bounds: floor <see cref="SCALE_FLOOR"/> (0.65) … ceil 1.0.
    /// Step size: <see cref="SCALE_STEP"/> (0.05) per adjustment.
    ///
    /// UI / HUD crispness: URP composites overlay cameras AFTER the upscale pass, so any Camera
    /// using Render Type = Overlay renders at native resolution regardless of the base render scale.
    /// No additional work needed — verify UI camera is set to Overlay, not Base.
    ///
    /// Auto-boots via <see cref="RuntimeInitializeOnLoadMethod"/>; no scene setup required.
    /// Disable via scripting define <c>WP_PERF_CONSOLE_OFF</c> (shared with PerformanceConsole).
    ///
    /// Manual override: call <see cref="SetManualScale"/> to pin the scale for debugging (e.g. from
    /// PerformanceConsole's Scale button). Call <see cref="ClearManualOverride"/> to resume auto.
    /// </summary>
    public sealed class RenderScaleGovernor : MonoBehaviour
    {
        // ── Config constants ──────────────────────────────────────────────────
        /// <summary>Target frame time that the PI controller drives toward (ms). ~15.5ms gives a
        /// small safety margin before the 16.67ms (60fps) cliff.</summary>
        private const float TARGET_MS = 15.5f;

        /// <summary>Dead-band half-width (ms). No action while |error| &lt; this value.</summary>
        private const float DEAD_BAND_MS = 0.8f;

        /// <summary>Minimum frames between render-scale adjustments to prevent pumping.</summary>
        private const int   COOLDOWN_FRAMES = 18; // ~0.3 s at 60fps

        /// <summary>Render-scale floor — never go below this on mobile (Adreno 730).</summary>
        private const float SCALE_FLOOR = 0.65f;

        /// <summary>Render-scale ceiling — native resolution.</summary>
        private const float SCALE_CEIL = 1.0f;

        /// <summary>Step size per adjustment.</summary>
        private const float SCALE_STEP = 0.05f;

        /// <summary>PI proportional gain. Tuned so a 3ms error → ~1 step.</summary>
        private const float KP = 0.04f;

        /// <summary>PI integral gain. Low enough not to windup over short spikes.</summary>
        private const float KI = 0.008f;

        /// <summary>Integral windup clamp (ms·s — cumulative error cap).</summary>
        private const float INTEGRAL_MAX = 5.0f;

        // ── Bootstrap ─────────────────────────────────────────────────────────
        private static RenderScaleGovernor? instance;

#if !WP_PERF_CONSOLE_OFF
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("[RenderScaleGovernor]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RenderScaleGovernor>();
        }
#endif

        // ── Public cross-phase signals (Phase 3 consumes these) ───────────────
        /// <summary>
        /// Current render scale the governor has settled on (0.65 … 1.0).
        /// Read-only — use <see cref="SetManualScale"/> for debug overrides.
        /// </summary>
        public static float CurrentRenderScale { get; private set; } = SCALE_CEIL;

        /// <summary>
        /// Density scalar derived from render scale: 1.0 = full density (scale ≥ 1.0),
        /// 0.0 = minimal density (scale = floor). Linear ramp between floor and ceil.
        /// Phase 3 (adaptive grass density) reads this as its SSOT rather than re-deriving
        /// its own signal from frame time.
        /// </summary>
        public static float DensityScalar { get; private set; } = 1.0f;

        // ── Reflection cache (mirrors PerformanceConsole — no new URP dep) ────
        private static PropertyInfo? renderScaleProp;

        // ── FSR reflection cache (one-time init, soft-fail on older URP) ─────
        // Property names on UniversalRenderPipelineAsset (Unity 2022 LTS / URP 14+).
        // Resolved once in InitFsr(); null = property absent (older URP, no-op).
        private static PropertyInfo? upscalingFilterProp;
        private static PropertyInfo? fsrOverrideSharpnessProp;
        private static bool fsrInitDone;

        // ── Controller state ──────────────────────────────────────────────────
        private float integral;
        private int   cooldownFrames;

        // ── Manual override ───────────────────────────────────────────────────
        private bool  manualOverride;
        private float manualScale;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            // Sync current scale from the active pipeline asset.
            CurrentRenderScale = GetRenderScale();
            this.UpdateDensityScalar();

            // One-time: activate FSR upscaling so Medium.asset's FsrSharpness=0.92 takes effect.
            InitFsr();
        }

        private void LateUpdate()
        {
            if (this.manualOverride)
            {
                this.ReportToConsole("manual");
                return;
            }

            float smoothedMs = PerformanceConsole.SmoothedFrameMs;

            // Wait until the console has accumulated enough frames to give a valid signal.
            if (smoothedMs < 0.001f) return;

            float error = TARGET_MS - smoothedMs; // positive = faster than target (headroom)

            // Dead-band: no action when error is within ±DEAD_BAND_MS.
            if (Mathf.Abs(error) <= DEAD_BAND_MS)
            {
                this.cooldownFrames = 0;
                this.ReportToConsole("idle");
                return;
            }

            // Step cooldown: prevent rapid oscillation.
            if (this.cooldownFrames > 0)
            {
                this.cooldownFrames--;
                this.ReportToConsole("cooldown");
                return;
            }

            // PI update.
            this.integral = Mathf.Clamp(this.integral + error * Time.unscaledDeltaTime, -INTEGRAL_MAX, INTEGRAL_MAX);
            float output = KP * error + KI * this.integral;

            // Translate PI output → nearest scale step.
            float current = GetRenderScale();
            float desired = current + Mathf.Sign(output) * SCALE_STEP;
            float clamped = Mathf.Clamp(Mathf.Round(desired / SCALE_STEP) * SCALE_STEP, SCALE_FLOOR, SCALE_CEIL);

            // Only act if the step actually moves the scale.
            if (Mathf.Abs(clamped - current) < SCALE_STEP * 0.5f)
            {
                this.ReportToConsole("idle");
                return;
            }

            SetRenderScale(clamped);
            CurrentRenderScale = clamped;
            this.UpdateDensityScalar();
            this.cooldownFrames = COOLDOWN_FRAMES;

            this.ReportToConsole(clamped < current ? "down" : "up");
        }

        // ── Public manual override API (PerformanceConsole Scale button) ──────
        /// <summary>
        /// Pin the render scale to a fixed value (for on-device debugging via PerformanceConsole).
        /// Call <see cref="ClearManualOverride"/> to resume automatic control.
        /// </summary>
        public static void SetManualScale(float scale)
        {
            if (instance == null) return;
            instance.manualOverride = true;
            instance.manualScale    = Mathf.Clamp(scale, SCALE_FLOOR, SCALE_CEIL);
            SetRenderScale(instance.manualScale);
            CurrentRenderScale = instance.manualScale;
            instance.UpdateDensityScalar();
        }

        /// <summary>Resume automatic PI control after a manual debug override.</summary>
        public static void ClearManualOverride()
        {
            if (instance == null) return;
            instance.manualOverride = false;
            instance.integral       = 0f;
            instance.cooldownFrames = 0;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void UpdateDensityScalar()
        {
            float range = SCALE_CEIL - SCALE_FLOOR;
            DensityScalar = range > 0f
                ? Mathf.Clamp01((CurrentRenderScale - SCALE_FLOOR) / range)
                : 1.0f;
        }

        private void ReportToConsole(string mode)
        {
            // Push governor state to the on-screen PerformanceConsole so the on-device
            // debug overlay shows what the controller is doing (required by phase spec).
            float scale = GetRenderScale();
            PerformanceConsole.Report("gov",
                $"scale={scale:0.00}  density={DensityScalar:0.00}  [{mode}]");
        }

        // ── FSR init (one-time, soft-fail) ───────────────────────────────────
        /// <summary>
        /// Switches the active URP pipeline asset to FSR upscaling and enables
        /// <c>fsrOverrideSharpness</c> so that the authored <c>fsrSharpness</c> value
        /// (Medium.asset = 0.92) is actually applied by the upscale pass.
        ///
        /// Uses reflection only — no typed URP asmdef dependency.
        /// Runs ONCE (guarded by <see cref="fsrInitDone"/>); no per-frame cost.
        /// Fails silently with a single WpLog warning when the properties are absent
        /// (URP version older than the upscaling filter API, or custom SRP).
        /// </summary>
        private static void InitFsr()
        {
            if (fsrInitDone) return;
            fsrInitDone = true;

            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;

            System.Type rpType = rp.GetType();

            // Resolve once — names are stable across URP 14–17 (Unity 2022 LTS – Unity 6).
            upscalingFilterProp    = rpType.GetProperty("upscalingFilter");
            fsrOverrideSharpnessProp = rpType.GetProperty("fsrOverrideSharpness");

            bool missingFilter    = upscalingFilterProp    == null;
            bool missingSharpness = fsrOverrideSharpnessProp == null;

            if (missingFilter || missingSharpness)
            {
                WpLog.Log(
                    "[RenderScaleGovernor] FSR init skipped — URP pipeline asset is missing " +
                    (missingFilter    ? "'upscalingFilter' " : "") +
                    (missingSharpness ? "'fsrOverrideSharpness' " : "") +
                    "property. Older URP version? FSR upscaling will not be enabled.");
                return;
            }

            // Resolve FSR BY NAME from the property's own enum type. Robust against ordinal
            // drift — the URP enum is Auto,Linear,Point,FSR,STP, so FSR is 3 (NOT 2 = Point) —
            // and Enum.Parse returns a correctly-typed boxed enum, avoiding the int→enum
            // SetValue type-mismatch that a raw int would throw.
            try
            {
                object fsrValue = System.Enum.Parse(upscalingFilterProp!.PropertyType, "FSR");
                upscalingFilterProp.SetValue(rp, fsrValue);
                fsrOverrideSharpnessProp!.SetValue(rp, true);
            }
            catch (System.Exception ex)
            {
                WpLog.Log($"[RenderScaleGovernor] FSR init reflection error: {ex.Message}. " +
                          "FSR upscaling was not enabled.");
            }
        }

        // ── Reflection setters (no typed URP dependency — WorldPainter.asmdef has references:[]) ─
        private static float GetRenderScale()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return 1f;
            renderScaleProp ??= rp.GetType().GetProperty("renderScale");
            object? v = renderScaleProp?.GetValue(rp);
            return v is float f ? f : 1f;
        }

        private static void SetRenderScale(float s)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return;
            renderScaleProp ??= rp.GetType().GetProperty("renderScale");
            renderScaleProp?.SetValue(rp, Mathf.Clamp(s, SCALE_FLOOR, SCALE_CEIL));
        }
    }
}
