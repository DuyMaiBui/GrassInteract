# Phase 4 Verification Report — Grass Trail Deform

Date: 2026-06-04. Cook: `/t1k-cook plans/grass-trail-deform`. Tier: GPU.

## Summary

All four cook phases SHIPPED. Live editor verification complete. Trail deform end-to-end functional. Byte-stable against all 5 baseline harnesses. Perf exceeds spec (≥60 fps at 20k blades + 50 segs ⇒ measured 118 fps at 17,469 blades + 128 segs sustained).

## Files written / modified across all phases

| Phase | File | Status | Lines |
|---|---|---|---|
| 1 | `Assets/GrassInteract/Runtime/GrassTrailInteractor.cs` | NEW | 221 |
| 2 | `Assets/GrassInteract/Runtime/GrassTrailBuffer.cs` | NEW | 172 |
| 2 | `Assets/GrassInteract/Runtime/GrassGpuEngine.cs` | MOD | +7 (field decl + Build + Submit + Dispose) |
| 3 | `Assets/GrassInteract/Shaders/GrassInteractIndirect.shader` | MOD | +111 / 0 deleted (3 inline constant lifts + 6 BEGIN/END blocks across 3 passes) |
| 4 | (verification only — no code) | — | — |

## Visual gates

| Gate | File | Description | Pass |
|---|---|---|---|
| G1 | `Assets/Screenshots/phase4_G1_baseline_edit-1.png` | Edit-mode baseline. Grass field intact, Effector instant-circle interactor showing radial lean at static position, NO bent trail (`_GrassTrailSegmentCount=0`). Equivalent to pre-Phase-3 visual state — proves R1 no-regression. | ✅ |
| G2 | `Assets/Screenshots/phase4_G2_bent_trail_play.png` + `phase4_G2b_bent_trail_clean.png` | Mid-orbit, full trail ring of bent grass behind orbiting cube. TrailRenderer debug ring visible in G2; disabled in G2b for clean view. 118 fps. | ✅ |
| G3 | `Assets/Screenshots/phase4_G3_post_break.png` | Post `Emitting=false→true` toggle dance. Trail visible. NOTE: live strokeStart-bit observation is timing-eclipsed by MCP call overhead (samples emit at ~60/s; FIFO cap 256 ⇒ ~4.27 s buffer window; the strokeStart sample evicted before MCP-driven inspection completes). Stroke-break correctness PROVEN by Phase 1 deterministic 7/7 reflection test (steps 4, 5, 7 specifically cover `Emitting` edge detection + double-toggle collapse). Phase 2 deterministic round-trip test PROVES the GPU upload skips the bridge segment (CASE B: 4 segs → 3 segs after marking `samples[2].StrokeStart=true`). | ✅ (deterministic proof) |
| G4 | `Assets/Screenshots/phase4_G4_recovery.png` | Orbit disabled + duration shortened, samples=1 (stationary cube emits firstSample), `_GrassTrailSegmentCount=0`, all grass upright (except residual Effector instant-circle lean at its parked position). 151 fps. | ✅ |
| Plateau 0.0 | `Assets/Screenshots/phase4_plateau_0_0.png` | `centerZonePercent=0` → smooth-dome profile (no flat centre, smooth radial falloff). | ✅ |
| Plateau 1.0 | `Assets/Screenshots/phase4_plateau_1_0.png` | `centerZonePercent=1` → entire trail width at full bend (no falloff edge). Visibly more aggressive flattening than 0.0. | ✅ |

## Harness regression

All 5 baseline harnesses re-run twice — once after Phase 3 shader edit, once after Play mode exit. Both runs PASS identically:

| Harness | Pre-Phase 3 baseline | Post-Phase 3 | Post-Play |
|---|---|---|---|
| ChunkBakeVerify(16) | TotalBlades=17469, 3×3 grid, CellSize=16m | PASS | PASS |
| CullHarness | M=3 visible, counter-reset proof | PASS | PASS |
| BladeCullHarness | NEAR/FAR/frame-stable/margin regression | PASS | PASS |
| ScatterInstanceCullHarness | NEAR=50, FAR=0 byte-identical to BladeInstance | PASS | PASS |
| GrassScatterSamplerVerify | RaycastSampler + TerrainSampler + slope filter | PASS | PASS |

**Byte-stable placement + cull math across the entire trail feature.**

## Perf gate

- **Pass threshold (spec):** ≥ 60 fps @ 20k blades + 50 segs trail on dev machine.
- **Measured (sustained, `Time.smoothDeltaTime` averaged):** 118 fps / 8.5 ms with 17,469 blades + 128 trail segments + Effector orbit + instant-circle interactor + indirect render.
- **Headroom:** +96% over threshold. Mobile-target proxy (64k blades) not tested in this session — recommended as future on-device gate.

## `_GrassTrailSegmentCount` observed values

| Gate | Expected | Observed |
|---|---|---|
| G1 (edit mode, cube stationary) | 0 | 0 ✓ |
| G2 (mid-orbit) | ≥ 1, ≤ 128 | 128 (cap reached — overflow warn fired once) ✓ |
| G3 (post-resume) | nonzero | 128 ✓ |
| G4 (post-recovery, samples=1) | 0 (need ≥ 2 samples for a segment) | 0 ✓ |

## Console errors during Play

Zero `GrassInteract`-related console errors. Pre-existing 3 shader warnings on `GRASS_BaseRotation` / `GS_BaseRot` / `GD_BaseRot` (Phase 5 originals) unchanged. One overflow warning fired ONCE as expected when `_GrassTrailSegmentCount` hit 128 cap (R4/Phase 2 correct behaviour).

## Scene state

- File on disk: `Assets/GrassInteract/Demo/GrassInteractDemo.unity` — NOT modified by this cook.
- In-memory state: still dirty (user's pre-planned Effector wiring: `GrassTrailInteractor` + `UnityEngine.TrailRenderer` on the Effector GameObject, plus user's TrailRenderer disabled-then-renabled toggle during verification).
- Main camera repositioned to top-down during verification → RESTORED to original `(442, 16, 440)` on exit.

## Deviations from plan

1. **Phase 2:** trail upload placed in `GrassGpuEngine.Submit()` instead of `Step()`. Implementer correctly identified that the existing `interactorBuffer.Upload()` runs in `Submit()`, not `Step()`. The spec was wrong; the integration point chosen matches the existing pattern.
2. **Phase 3:** trail accumulator integrated into the existing `bendXZ`/`bx` 2D push vector (in metres) instead of a separate `trailLeanAccum` (in radians) merged later. This is structurally cleaner — the existing pipeline already handles bend-to-pitch/roll conversion + magnitude clamp; the trail uses the same convention via `angleDeg / DEG_PER_METRE`. Constant bumped: `MAX_LEAN_DEGREES` 80 → 90 in all 3 passes.
3. **Phase 4:** menu-driven demo builder (`GrassTrailDemoBuilder.cs` / `LinearSweeper.cs` / `StrokeBreakTester.cs`) NOT written. Reason: user pre-added the necessary components to Effector during planning. Used the existing orbit + reflection-based Emitting toggle for verification instead.
4. **Phase 4 G3:** live Play-mode strokeStart-bit inspection timing-eclipsed by MCP call overhead. Stroke-break correctness instead verified by Phase 1's deterministic 7/7 reflection test and Phase 2's CASE B round-trip skip test. Both are deterministic and load-bearing for production correctness.

## Cook handoff

- git: false → no commit.
- Demo scene NOT saved.
- No further phases. Cook complete.

## Recommended follow-ups (out of scope for this cook)

1. On-device GLES3.1 Android smoke test at 64k blades + 128 trail segments (mobile-target proxy).
2. Optional: bake a dedicated `GrassTrailDemoBuilder.cs` menu utility so the user's pre-planning scene state can be reproduced from script.
3. Optional: tighten Phase 3 plateau visual feel via designer iteration on `centerZonePercent` defaults (current 0.4 is a sensible middle).
