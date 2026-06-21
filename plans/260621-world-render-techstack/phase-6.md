# Phase 6 — Device Tiers + Performance Budgets + On-Device Validation

**Effort:** M · **Blocks:** none (closing phase) · **Blocked by:** 1, 2, 3, 4

## Goal

Author the 3-tier device-scaling presets over the render stack (URP assets + grass density + segment draw distance + prop density), fix the Low-tier URP asset HDR footgun, define draw-call / fill-rate / memory budgets, and validate the whole world-render stack on a real device per tier — with mandatory validation on a physical GLES3.0 (no-compute) device.

## File Ownership (real paths)

Create:
- `Assets/WorldPainter/Runtime/Render/DeviceTierPresets.cs` — maps `DeviceTierProbe` tier → render preset (URP asset selection, grass blade-count, segment draw distance from `SegmentRenderConfig`, prop density). `UPPER_SNAKE_CASE` consts; no inline literals.
- `Assets/WorldPainter/Runtime/Render/RenderBudget.cs` — target budgets per tier: max draw calls, max resident segment memory, max grass blades, target fill-rate. Consts only.

Edit:
- `Assets/WorldPainter/Editor/Build/MobileRenderConfigValidator.cs` — already asserts active URP HDR off / MSAA 1 / shadow res ≤1024 / shadow dist ≤25 / no `scatterForceTier == ForceCpu`. Extend: assert the Low-tier URP asset specifically has HDR OFF (report §5: "low URP asset currently has HDR on — fix").
- The Low-tier URP RenderPipelineAsset (find under `Assets/` Settings) — set `m_SupportsHDR = false`.
- `Assets/WorldPainter/Runtime/Diagnostics/RenderScaleGovernor.cs` / `GrassDensityController.cs` / `PerformanceConsole.cs` — wire to `DeviceTierPresets` so the on-device perf console reports tier + budget headroom.

Use: `DeviceTierProbe` (Phase 1) as the single tier source.

## Concrete Steps

1. Author `DeviceTierPresets` + `RenderBudget` consts (High/Mid/Low).
2. Fix the Low URP asset HDR; extend `MobileRenderConfigValidator` to fail the build if Low HDR is on.
3. Apply presets at startup from `DeviceTierProbe`: select URP asset, set grass blade-count, segment draw distance, prop density.
4. Wire the perf console to show tier, draw calls, resident segments, grass blade count, frame time vs budget.
5. Validate on one device per tier; record numbers vs `RenderBudget`.

## Verification

- **Compile:** `read_console` clean; `run_tests` EditMode green.
- **Build gate:** `MobileRenderConfigValidator` fails an Android build if Low URP HDR is on (confirm it trips, then fix, then passes).
- **On-device per tier:**
  - High (Vulkan/Metal): full grass, GPU prop path, full draw distance — meets High budget.
  - Mid (GLES3.1): reduced density — meets Mid budget.
  - **Low (GLES3.0 no-compute, MANDATORY physical device):** terrain + props + grass all VISIBLE (not blank, not pink), within Low budget, target frame rate held. This is the final proof that the silent-blank-render risk is closed — re-confirm by visual observation, since SelfTest cannot detect a RenderMeshIndirect no-op.
- Memory Profiler: resident segment memory ≤ `RenderBudget` per tier; bounded across a long flythrough.

## Success Criteria

- 3 tiers select correct URP asset + grass density + draw distance + prop density from `DeviceTierProbe`.
- Low URP HDR off; validator enforces it.
- World renders correctly and within budget on a physical device per tier, ESPECIALLY GLES3.0.
- Perf console reports tier + budget headroom on device.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Low-tier still over budget after presets (grass/draw calls too high) | 3 | 4 | 12 | RenderBudget consts tuned on device; grass blade-count presets (10k-50k is desktop); reduce segment draw distance on Low |
| Tier preset applied too late (after first frame renders wrong) | 2 | 3 | 6 | Apply DeviceTierPresets in bootstrap before first segment streams (FrameRateBootstrap timing) |
| No physical GLES3.0 device for final sign-off | 3 | 5 | 15 | Same as Phase 1 — procure/borrow device; forced-GLES3.0 desktop GL as interim only, never as final sign-off |
| HDR-off fix regresses High-tier look | 2 | 2 | 4 | HDR off only on Low URP asset; High keeps HDR; per-tier asset selection isolates the change |

Score ≥15 mitigated before start: row 3 (device availability — shared with Phase 1).
