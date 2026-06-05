# Phase 4 — Demo wiring + visual + harness verification

Effort: S. Depends on: Phases 1, 2, 3. Blocks: nothing (terminal phase).
Goal: wire a moving cube + `GrassTrailInteractor` into the demo scene, capture the four mandatory screenshot gates (before / mid-sweep / mid-stroke-gap / 6 s post-sweep), re-run all 5 existing harnesses for byte-stable regression, and confirm perf budget on the dev machine.

## Scope — file ownership

NEW (test-only, isolated):
- `Assets/GrassInteract/Tests/Editor/GrassTrailDemoBuilder.cs` (optional editor utility): menu item `Tools/GrassInteract/Add Demo Trail Interactor` that adds a moving cube + `GrassTrailInteractor` + a small MonoBehaviour `LinearSweeper` (drives the cube) + a `StrokeBreakTester` (toggles `Emitting` on a timer). Mirrors `ScatterPropMeshBuilder` pattern from Terrain-Scatter Phase 3.

MODIFIED (Inspector edits, no script):
- Demo scene (`Assets/GrassInteract/Demo/GrassInteractDemo.unity` or whichever scene the project status names) — add the trail interactor cube via the menu item above. No code change to the scene asset; the wiring is one menu click.

UNCHANGED: every Runtime/ file, every Shader, every other Editor/.

## Demo wiring

```csharp
// Tools/GrassInteract/Add Demo Trail Interactor
//   - creates "TrailInteractor_DemoCube" GameObject under the scene root
//   - attaches BoxCollider-less cube mesh (visual only, scale ~0.5)
//   - attaches LinearSweeper { from=(-10,0.2,0), to=(10,0.2,0), durationSeconds=4, loop=true }
//   - attaches GrassTrailInteractor { trailDuration=5, minVertexDistance=0.25,
//                                     worldRadius=2, maxBendDegrees=90,
//                                     centerZonePercent=0.4, strength=1 }
//   - attaches StrokeBreakTester { breakStart=2.0, breakDuration=0.5 }
//     (sets Emitting=false from t=2s for 0.5s on the first sweep, then leaves it true)
```

`LinearSweeper` + `StrokeBreakTester` are tiny `[ExecuteAlways]` test scripts (~20 lines each). They live in Tests/Editor (or Demo/) — NOT in Runtime/. Stripped from player builds.

## Verification gate (live-editor evidence)

### Screenshot gates (4 mandatory)

1. `set_active_instance GrassInteract` FIRST. Force GPU tier on demo (`forceTier = ForceGpu`). Enter Play mode.
2. **Gate G1 — Before sweep**: t=0 s. Trail interactor at start position, samples empty, grass fully upright. Top-down screenshot. (Regression check vs Phase-2 baseline.)
3. **Gate G2 — Mid-sweep**: t=2.0 s (just before stroke break). Visible bent trail behind the cube. Centre flat, edges feathered (plateau profile).
4. **Gate G3 — Stroke gap**: t=2.4 s (mid-break window, `Emitting=false`). Trail behind takeoff is still bent + ageing; no new trail emitted at the cube's current position. After t=2.5 s (`Emitting` back true), a new stroke-start sample lands at the current position. By t=2.8 s a small bent patch is visible at the post-landing location. NO bent grass between the takeoff and landing positions.
5. **Gate G4 — Full recovery**: stop the sweep at t=4 s (LinearSweeper completes one cycle and pauses). Wait 5 s. At t=9 s, ALL grass upright (every sample evicted, `_GrassTrailSegmentCount == 0`).

### Harness regression gates (5 mandatory)

Re-run via `execute_code`:

1. `GrassInteract.EditorTools.GrassChunkBakeVerify.Run(16)` — `ChunkBake=17469`, byte-stable vs project-status baseline.
2. `GrassInteract.EditorTools.GrassCullHarness.Run()` — PASS.
3. `GrassInteract.EditorTools.GrassBladeCullHarness.Run()` — PASS incl. margin regression.
4. `GrassInteract.EditorTools.ScatterInstanceCullHarness.Run()` — PASS.
5. `GrassInteract.EditorTools.GrassScatterSamplerVerify.Run()` — PASS.

All 5 must report identical results to the project-status baseline. Any drift = regression; halt and root-cause before sign-off.

### Perf gate

`Time.smoothDeltaTime`-based, NOT capture-frame:
- 20 000 blades + 1 `GrassTrailInteractor` + ~50 segments active → ≥ 60 fps on dev machine.
- 64 000 blades + same trail → ≥ 30 fps (mobile-target proxy).
- Profiler check: `GrassGpuEngine.Step` time delta from pre-feature baseline < +0.3 ms (segment flatten budget).

If perf fails: profile the VS loop iteration count per blade in the Frame Debugger. Document a v2 follow-up phase for chunk-prefilter; do NOT block ship unless mobile-target proxy fails.

### Coexistence gate

Existing demo orbit effector (`GrassInteractor` instant-circle) + the new `GrassTrailInteractor` both active:
- Top-down screenshot: orbit footprint visible AND trail visible. No flicker, no occlusion.
- Move orbit through trail — both leans accumulate then clamp to 90°. No NaN, no ground-clip.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Demo scene edited and saved with trail interactor in it (pollutes baseline) | 3 | 2 | 6 | DO NOT save the demo scene with the trail cube. Add it via menu, capture screenshots, exit Play, undo or do not save. Document in Phase 4 report. |
| Harness regression caused by Phase 2/3 (not Phase 4) but caught here | 2 | 4 | 8 | If a harness fails: rollback Phase 3 first (cheapest); re-run; if still fails rollback Phase 2; if still fails Phase 1. The phase-3 block delimiters make this surgical. |
| Perf gate fails on the 64k-blade mobile proxy | 3 | 3 | 9 | Profile + document. Ship Phases 1-4 anyway IF 20k case passes; file v2 follow-up phase for chunk-prefilter. Do NOT bundle perf work into this plan. |
| Capture-frame inflation (screenshot capture ≠ steady-state time) | 3 | 1 | 3 | Per project status (Phase 5 GPU plan): measure perf via Time.smoothDeltaTime, NOT the capture frame. |
| StrokeBreakTester timing flaky in editor (frame-rate dependent) | 2 | 2 | 4 | Use unscaled time for the toggle; document the exact frame target. Re-run the gate twice for consistency. |

## Rollback

- Editor utility: delete `Assets/GrassInteract/Tests/Editor/GrassTrailDemoBuilder.cs` and friends.
- Demo scene: undo the menu-added GameObject (or just don't save).
- All Runtime/Shader code from Phases 1-3 stays — rollback of those is the per-phase rollback in their respective phase docs.

## Cook closeout

After all gates pass:
1. Commit if git is enabled (project status notes git: false; skip).
2. Write the Phase 4 verification report under `plans/grass-trail-deform/phase-4-report.md` summarizing G1-G4 screenshots, harness results, perf numbers.
3. Update memory `grassinteract-project-status.md` with a 1-paragraph "Trail-Deform plan, /t1k-cook P4 of 4 — DONE + LIVE-VERIFIED" entry.
4. Notify user: feature shipped, suggest `/t1k:cook --review` for an adversarial pass before declaring final ship.
