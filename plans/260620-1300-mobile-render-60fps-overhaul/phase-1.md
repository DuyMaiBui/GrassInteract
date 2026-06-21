# Phase 1 — Render-scale governor + FSR (M)

**Priority:** P0 — the headline 60-FPS win. **Effort:** M. **Blocked by:** Phase 0 (lever must be hardware-confirmed first).

## Objective

Add a frame-time-driven dynamic-resolution governor that holds 60 FPS by lowering URP render scale under load and restoring it (up to native 1.0×) when there is headroom. Switch the upscaler to **FSR** with override-sharpness so the downscaled frame upsamples cleanly. This is the single lever that scales fragment cost across every pass on the TBDR frame.

## Design (operationalizes brainstorm §"P0")

- **Signal:** reuse the PerformanceConsole ring-buffer smoothed `avgMs` (already a 0.5s windowed average). Do NOT add a second timing source — share one signal with Phase 3. Expose `avgMs` from `PerformanceConsole` (add a `public static float SmoothedFrameMs` accessor reading the existing computed field) so the governor consumes it without re-implementing the ring buffer (DRY).
- **Controller:** PI controller on the error `(targetMs - avgMs)` where `targetMs = 1000/60 ≈ 16.67ms` with a small safety margin (target ~15.5ms so it pre-empts the drop). Proportional + integral terms; clamp integral windup.
- **Hysteresis + cooldown:** a dead-band around target (do not act inside ±~0.8ms) and a step-cooldown (min N frames between scale changes, e.g. ~0.3–0.5s) so resolution does not pump visibly (R6).
- **Bounds:** render-scale **floor 0.65, ceil 1.0**. Step size ~0.05 per adjustment.
- **Upscaler:** set `Medium.asset` `m_UpscalingFilter` to **FSR (1)** and `m_FsrOverrideSharpness: 1` so the existing `m_FsrSharpness: 0.92` actually applies (it is currently inert because override is 0).
- **Drive mechanism:** reuse the existing reflection setter path (`SetRenderScale` resolves `renderScale` on `GraphicsSettings.currentRenderPipeline` → the active `Medium.asset`). OPTIONALLY cache a typed `UniversalRenderPipelineAsset` reference to avoid per-adjust reflection cost — but the adjust cadence is ~2/sec so reflection is acceptable; prefer reuse over a new hard URP dependency unless profiling says otherwise.
- **UI crispness:** verify URP composites the **overlay/UI after upscale** so HUD + text render at native resolution (R5). If not, ensure the UI camera / overlay renders post-upscale.

## File ownership

- **Create:** `Assets/WorldPainter/Runtime/Diagnostics/RenderScaleGovernor.cs` — new `#nullable enable` MonoBehaviour (auto-boot via `RuntimeInitializeOnLoadMethod`, mirroring PerformanceConsole's bootstrap). `this.` prefix, camelCase private fields, constants UPPER_SNAKE_CASE. PI controller + hysteresis + cooldown; reads `PerformanceConsole.SmoothedFrameMs`; writes render scale via reflection (or cached typed ref).
- **Edit:** `Assets/WorldPainter/Runtime/Diagnostics/PerformanceConsole.cs` — add `public static float SmoothedFrameMs => instance != null ? instance.avgMs : 0f;` (expose existing smoothed value; do NOT duplicate ring buffer). Also surface governor state (current scale, controller mode) via `Report("gov", ...)` so the on-device console shows what the governor is doing.
- **Edit:** `Assets/URPDefaultResources/Medium.asset` — `m_UpscalingFilter: 1` (FSR), `m_FsrOverrideSharpness: 1` (so `m_FsrSharpness: 0.92` applies). Leave `m_RenderScale: 1` as the ceiling start value.
- **Edit (optional):** `Assets/WorldPainter/Runtime/FrameRateBootstrap.cs` — no change needed for the cap (already 60); only touch if the governor needs to coordinate the cap/vSync at boot.

## Step-by-step tasks

1. Expose `SmoothedFrameMs` from `PerformanceConsole` (single accessor; no logic duplication).
2. Author `RenderScaleGovernor`: PI controller, target ~15.5ms, dead-band ±0.8ms, step 0.05, floor 0.65 / ceil 1.0, cooldown ~0.4s, integral clamp. Auto-boot. Push current scale + mode to the console via `Report`.
3. Edit `Medium.asset`: set FSR filter + override-sharpness flag.
4. Verify UI/HUD renders at native (URP overlay-after-upscale). If the project composites UI in the same scaled pass, route the HUD camera as an overlay rendered post-upscale.
5. On device: confirm the governor lowers scale under load to hold 60 and restores to 1.0× in light views without visible pumping.

## On-device verification gate (PASS criteria)

- [ ] Grass-heavy ground-level framing holds **≥ 60 FPS** with the governor active; console shows scale dropping under load (e.g. ~0.8×) and avg ms ≤ ~16.7.
- [ ] Terrain-heavy vista framing holds **≥ 60 FPS**.
- [ ] In a light/empty view the governor returns to **1.0×** (no permanent softening).
- [ ] **1% low ≥ 55 FPS** during a sweep across both framings.
- [ ] No visible resolution **pumping** (hysteresis + cooldown effective) on device.
- [ ] HUD/text remains **crisp** at native resolution while the world is upscaled (R5).
- [ ] FSR sharpness visibly applied (compare vs bilinear) — confirms `FsrOverrideSharpness` took effect.

All gates measured **on the Adreno 730**, never editor (editor = Ultra = wrong frame).

## Risk note

R5 (UI softening) + R6 (oscillation) are the live risks here; both mitigated above (overlay-after-upscale; hysteresis + cooldown + smoothed signal). R1 carryover: if Phase 0 showed a low fragment fraction, the governor will still help but may not single-handedly clear 60 — lean on Phase 5. Do NOT touch grass opacity or add dither here (guardrail: −5–15 fps).
