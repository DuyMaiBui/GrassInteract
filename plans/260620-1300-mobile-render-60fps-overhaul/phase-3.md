# Phase 3 — Adaptive grass density controller (M)

**Priority:** P2. **Effort:** M. **Shares Phase 1's frame-time signal.** Hitch-free density swap depends on Phase 4 (see dependency note).

## Objective

Add a second adaptive knob: a runtime grass-density multiplier consumed inside `GrassCull.compute` `BladeCull` as a **stable-hash skip** (`(blade.hash % 256) >= threshold` → skip the blade), plus a dynamic cull distance. Driven by the SAME smoothed frame-time signal as Phase 1, with a **density floor** so the field never goes bald. Giving the governor a second independent lever means it rarely has to swing render resolution far — softening is shared between resolution and density.

## Design (operationalizes brainstorm §"Adaptive grass density controller")

- **Stable-hash skip in `BladeCull`:** each `BladeInstance` already carries a deterministic `hash` (uint, set at bake — see `ChunkedBladeBuffer` `slot2`/XorShift32). Add a `densityThreshold` uint uniform (0..256). In `BladeCull`, before the LOD bucket append, skip when `(b.hash % 256u) >= densityThreshold`. Threshold 256 = full density; lower = thinned. Because the test is on the stable per-blade hash, the SAME blades are kept/dropped frame-to-frame → no shimmer, deterministic thinning.
- **Density floor:** clamp threshold to a floor (e.g. ≥ 160/256 ≈ 62%) so even under max load the field stays visually full.
- **Dynamic cull distance:** drive `maxCullSqrDistance` (already a `BladeCull` uniform) down under load and back up with headroom — pulls in the far field where density loss is least noticeable.
- **Shared signal:** the controller reads `PerformanceConsole.SmoothedFrameMs` (the accessor added in Phase 1) — ONE signal feeds both the resolution governor and this density knob. Coordinate so they don't fight: density acts as the first/cheap knob; resolution as the second. Keep both behind the same hysteresis discipline.
- **Where the uniform is set:** the controller computes `densityThreshold` + dynamic cull distance from frame time and writes them into the compute dispatch via the GPU engine's per-frame uniform push.

## File ownership

- **Edit:** `Assets/WorldPainter/Shaders/GrassCull.compute` — add `uint densityThreshold;` binding; in `BladeCull`, after reading `BladeInstance b`, add `if ((b.hash % 256u) >= densityThreshold) continue;` BEFORE the LOD bucket. Document the contract (256 = full, floor enforced on CPU side).
- **Edit:** `Assets/WorldPainter/Runtime/Scatter/GrassGpuEngine.cs` — per-frame: set the `densityThreshold` and dynamic `maxCullSqrDistance` compute uniforms before dispatch. Add a `SetDensity(float normalized01)` API consumed by the controller (clamped to the floor).
- **Create:** `Assets/WorldPainter/Runtime/Diagnostics/GrassDensityController.cs` — new `#nullable enable` controller reading `PerformanceConsole.SmoothedFrameMs`, applying hysteresis + floor, calling `GrassGpuEngine.SetDensity`. Coordinates with `RenderScaleGovernor` (density as first knob). `this.` prefix, camelCase fields, UPPER_SNAKE_CASE consts.
- **Extend (tests):** `Assets/WorldPainter/Tests/Editor/ScatterLodCullTests.cs` — add a case asserting the hash-skip math (a blade with `hash % 256 == 200` is culled at threshold 160, kept at 256).

## Step-by-step tasks

1. Add `densityThreshold` uniform + stable-hash skip to `BladeCull` (before LOD bucketing, after the distance reject for cheapest early-out).
2. Add `SetDensity` + per-frame uniform push (threshold + dynamic cull distance) in `GrassGpuEngine`.
3. Author `GrassDensityController`: frame-time → threshold mapping, floor clamp, hysteresis, coordination with the resolution governor.
4. Extend `ScatterLodCullTests` to cover the skip math (deterministic in/out at threshold boundaries).
5. On device: drive density up/down under load; confirm no shimmer and the field never goes bald.

## Dependency note (Phase 4)

Until **Phase 4** (baked blob) lands, a density change that is implemented as a *re-scatter* would cause a main-thread hitch. **This phase's density knob is a pure GPU-uniform change (no re-scatter)** — so it is hitch-free on its own. The Phase-4 dependency is only relevant if a density change ever needs to rebuild buffers; with the stable-hash-skip approach it does NOT. Keep the implementation uniform-only so Phase 3 is hitch-free independent of Phase 4. (Phase 4 still helps the broader startup hitch and any full reload.)

## On-device verification gate (PASS criteria)

- [ ] Under sustained load the controller thins grass (visible density drop in far field first) and the frame holds **≥ 60 FPS**; in light views density returns to full.
- [ ] **No shimmer / popping** as density changes (stable-hash keeps the same blades) — verified on device.
- [ ] Field never goes below the density floor (always visually full).
- [ ] Density-swap causes **zero multi-frame hitch** (uniform-only change; no re-scatter) — confirmed via PerformanceConsole 1%-low readout on device.
- [ ] With both Phase 1 + Phase 3 active, the render-scale governor idles closer to 1.0× than with Phase 1 alone (density absorbs load first).

EditMode coverage note: the test-runner was wedged last session (`Cannot access a disposed object`). Run `ScatterLodCullTests` manually via `Window ▸ General ▸ Test Runner` after a Unity restart if MCP run_tests fails.

## Risk note

R8 (wedged test runner) is the only material risk; mitigated by manual test-runner run. The hash-skip is deterministic and cheap (one modulo + compare per blade) — negligible compute cost on a fragment-bound frame. Do not let the floor drop low enough to expose bald ground (visual regression).
