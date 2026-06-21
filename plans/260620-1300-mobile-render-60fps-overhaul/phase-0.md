# Phase 0 — Measure-first de-risk (S)

**Priority:** GATE for Phase 1. **Effort:** S. **Type:** on-device measurement, zero code.

## Objective

Prove on the real Adreno 730 that the frame is fragment-bound to the degree the Phase-1 headline assumes (~80% fragment fraction) **before** building the render-scale governor. Render scale is the entire 60-FPS thesis; this phase hardware-confirms the lever exists. Cheap insurance against building a governor for a frame that does not respond to it.

## Why this works without new code

`PerformanceConsole.cs` already ships in every build (auto-spawns via `RuntimeInitializeOnLoadMethod`) and already has the **`Scale`** button that cycles URP render scale `1.00 → 0.85 → 0.70 → 0.55` via reflection onto the active `Medium.asset` (`CycleRenderScale` → `SetRenderScale`). It also displays smoothed FPS + avg ms + the actual post-scale resolution. Everything Phase 0 needs is already on device.

## File ownership

- **Create / edit:** none (measurement only).
- **Read-only reference:** `Assets/WorldPainter/Runtime/Diagnostics/PerformanceConsole.cs` (the `Scale` cycle + FPS readout used to capture numbers).

## Step-by-step tasks

1. Build the WorldPainter demo for Android in **Medium** quality (the shipping tier) and deploy to the Adreno 730 handset. Confirm the on-screen console shows **"Grass tier: GPU"** in green — if it shows CPU, stop: that is the original CPU-tier bug, not a fragment-bound frame, and Phase 2's validator is the fix.
2. **Tap `Cap` → ∞ FIRST (critical for a valid measurement).** `FrameRateBootstrap` caps at 60 FPS (`TARGET_FPS=60`). At 0.70× scale a fragment-bound frame can rise *past* 60 — the 60-cap would clip the gain and make the lever look weaker than it is, under-estimating the fragment fraction. Set the cap to ∞ so render-scale headroom is fully visible. Confirm the console header reads `Cap ∞`.
3. Frame a **grass-heavy ground-level** view (worst-case fragment overdraw). Let FPS settle; record `fps` and `avg ms` at **Scale 1.00**.
4. Tap **Scale** once → 0.85. Record fps / avg ms. Tap again → 0.70. Record fps / avg ms.
5. Repeat steps 3–4 for a **terrain-heavy vista** framing (the second representative camera).
6. Compute the fragment fraction from the 1.00 vs 0.70 deltas (use **`avg ms`**, not fps, for the ratio — frame time is linear in fragment work, fps is not): at 0.70× scale fragments drop to ~0.49×. If `avg ms` drops substantially (frame time scales down toward the ~0.49× fragment portion), the frame is fragment-bound and the governor will deliver. If `avg ms` barely moves, it is vertex/CPU-bound and the governor headline is weak.

## On-device verification gate (PASS criteria)

- [ ] Console confirms **GPU** grass tier (not CPU) on device.
- [ ] Frame cap set to **∞** before measuring (header reads `Cap ∞`) so the 60-cap doesn't clip the 0.70× gain.
- [ ] Documented table exists: `fps` **and `avg ms`** at scale 1.00 / 0.85 / 0.70 for BOTH grass-heavy and terrain-heavy framings, captured on the Adreno 730 (NOT editor).
- [ ] Frame-time (`avg ms`) drop from 1.00→0.70 is computed and interpreted (fragment fraction estimate).

**Decision output of this gate:**
- Fragment fraction high (FPS jumps ~proportionally) → **proceed to Phase 1 as the headline win.**
- Fragment fraction low (FPS barely moves) → **contingency:** the governor still helps but the Phase-5 native-res quality bundle carries more weight; document this re-weighting in `plan.md` before continuing.

## Risk note

R1 (fragment fraction < assumed). This phase IS the mitigation for R1 — it converts an assumption into a measured fact before any governor code is written. No new risk introduced (no code). Only failure mode is mis-framing the camera (not worst-case overdraw) — frame the densest grass view to avoid under-estimating fragment cost.
