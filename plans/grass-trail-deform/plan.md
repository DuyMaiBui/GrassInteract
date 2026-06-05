# Plan: GrassInteract — Grass Trail Deform (Persistent Capsule Trail Bending)

Generated 2026-06-04. Source: `plans/reports/brainstorm-grass-trail-deform-20260604.md` (LOCKED design).
Project: GrassInteract. Unity 6, URP 17.3, Mono — NO DOTS/Burst. git: false.

## Goal

Add a moving interactor that leaves a **persistent bent trail** behind it. The trail is a TrailRenderer-style FIFO of samples connected by capsule segments; each segment fades over `trailDuration`; the bend profile is a configurable plateau (inner `centerZonePercent` of capsule width = full `maxBendDegrees` up to 90°, smooth falloff to 0 at the edge). Supports **stroke breaks** so the trail does NOT bridge across jump arcs ("foot off ground → no bent grass mid-air").

GPU tier only. Coexists with the existing instant-circle `GrassInteractor`. Byte-stable to the existing scatter/cull pipeline (5 harnesses pass unchanged).

## Architecture (additive — bolted onto the GPU tier)

```
   Scene
   └─ GrassTrailInteractor (NEW MonoBehaviour, sibling of GrassInteractor)
        - per-instance FIFO List<TrailSample>{posWS, age, strokeStart}
        - LateUpdate: tick ages → evict → emit (if Emitting) → mark strokeStart on resume
        - Static Active registry (mirrors GrassInteractor.Active)

   GrassGpuEngine.Step(dt)  (MODIFIED — append one pass)
        - existing: upload GrassInteractor.Active → _Interactors
        - NEW:      flatten Active GrassTrailInteractor samples → segments[]
                    skip pairs where samples[i].strokeStart == true
                    SetData → _TrailSegments
                    Shader.SetGlobalInteger(_TrailSegmentCount, segments.Count)

   GrassInteractIndirect.shader  (MODIFIED — append VS loop + lift MAX_LEAN)
        - existing interactor loop unchanged
        - NEW: for each _TrailSegments[i], 2D capsule distance, plateau profile,
               fade alpha, accumulate perpendicular-away lean
        - MAX_LEAN literal 80° → 90° (single line)
```

CPU tier (`GrassBendSimulator` / `GrassCpuEngine`) = documented no-op for trails. One-time runtime warn when any `GrassTrailInteractor` exists and a field is CPU-tier.

## Phase index

| Phase | Name | Scope (owned files) | Effort |
|---|---|---|---|
| 1 | `GrassTrailInteractor` + sampler + stroke breaks + gizmo | Component, FIFO sampler with TrailRenderer semantics, `Emitting` toggle with stroke-break logic, gizmo polyline with stroke-start ticks. NO GPU yet. | M |
| 2 | `GrassTrailBuffer` + GPU upload | New `TrailSegmentGpu` struct + GraphicsBuffer wrapper, segment flattening + upload in `GrassGpuEngine.Step`, cap at 128 + warn-once overflow. NO shader read yet. | S |
| 3 | Shader VS extension + MAX_LEAN 80→90° lift | Capsule distance + plateau profile + fade alpha + perpendicular-away lean in `GrassInteractIndirect.shader`. Single-line `MAX_LEAN` constant bump. | M |
| 4 | Demo wiring + visual + harness verification | Wire a moving cube + `GrassTrailInteractor` into the demo scene. Visual gates (bent trail, fade, stroke gap). Re-run all 5 existing harnesses for regression. | S |

Critical path: 1 → 2 → 3 → 4. Strictly sequential — Phase 2 needs Phase 1's segment iterator surface; Phase 3 needs Phase 2's globals; Phase 4 needs all of 1–3 live. One agent per phase with approval gates between phases (consistent with prior GrassInteract cooks per project status).

## Feasibility

- **Reuse:** `GrassInteractorBuffer` is the structural template for `GrassTrailBuffer` (same lifecycle, same warn-once cap pattern, same SetGlobalInteger discipline). `GrassGpuEngine.Step` already has the "gather Active → upload → set count" loop the trail upload mirrors. Existing interactor VS deform code in `GrassInteractIndirect.shader` is the template for the new capsule VS loop. `GrassInteractor.OnEnable/OnDisable` registry pattern is the template for `GrassTrailInteractor`.
- **Complexity:** moderate. New component + new GPU buffer + ~30-line VS addition. No new architectural concept — capsule-vs-point math is standard, plateau profile is one smoothstep, fade alpha is one multiply.
- **No new packages.** No new asmdef. No new MCP tooling. All in `Assets/GrassInteract/Runtime/` + `Assets/GrassInteract/Shaders/`.
- **allowUnsafeCode: false** stays. `TrailSegmentGpu` is blittable; `GraphicsBuffer.SetData(T[])` works without unsafe.

## Dependencies (cross-phase)

- **Phase 1** blocks all others. Blocked by: nothing.
- **Phase 2** blocked by 1 (needs `GrassTrailInteractor.Samples` accessor + `Active` registry). Blocks 3 (shader VS reads the buffer Phase 2 uploads).
- **Phase 3** blocked by 2 (buffer must exist + be populated to verify the shader read). Blocks 4 (visual gate needs end-to-end render).
- **Phase 4** blocked by 1 + 2 + 3.

## Cross-phase Risk Assessment

| # | Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|------|:---:|:---:|:---:|------------|
| R1 | Lifting `MAX_LEAN` 80°→90° regresses existing single-interactor lean (visible change in the Phase-6/GrassBendSimulator baseline) | 2 | 3 | 6 | Phase 3 gate: pre-/post-screenshot of the existing orbit effector at strength=1 — must look identical (existing math caps below the literal, so the lift should be a no-op for it). Document the math proof in Phase 3 report. |
| R2 | New `_TrailSegments` / `_TrailSegmentCount` shader globals collide with another system's globals | 2 | 4 | 8 | Namespace via `_GrassTrail` prefix (`_GrassTrailSegments`, `_GrassTrailSegmentCount`). Grep the codebase for existing globals in Phase 2 before locking names. |
| R3 | VS loop cost over 128 segments × 64k blades pushes target devices below 60 fps | 2 | 3 | 6 | Early-out `if (d > radius) continue;` rejects most blade/segment pairs cheaply (one dot + one length). Phase 4 perf gate: 20k blades + 50-seg trail on dev machine ≥ 60 fps. If short, add a chunk-prefilter (out of scope for v1; documented as Phase 5 follow-up). |
| R4 | Segment-flattening allocates / GC-pressures the main thread each frame | 3 | 3 | 9 | Pre-allocate a `TrailSegmentGpu[MAX_TRAIL_SEGMENTS]` staging array in `GrassGpuEngine` ctor; reuse every frame; never `new T[]` in the hot path. Phase 2 gate: profiler shows 0 GC alloc on Step. |
| R5 | Stroke-break logic misses an edge case: rapid Emitting toggle (true→false→true within one frame) | 2 | 2 | 4 | Edge detection via `wasEmittingLastFrame` cached after each LateUpdate; double-toggle in one frame is collapsed to "stayed at current value." Phase 1 unit-test covers true→false→true and false→true→false within one tick. |
| R6 | Existing scatter/cull harnesses fail because they share globals or runtime state with trail upload | 1 | 4 | 4 | Trail buffer is purely additive to `GrassGpuEngine` and only writes its own globals. Harnesses construct their own engines or run synthetic scatter — they don't read `_GrassTrail*`. Phase 4 gate: all 5 harnesses GREEN. |
| R7 | TrailRenderer-`linkedTrailRenderer` field tempts users to think it drives the deform | 2 | 2 | 4 | XML doc on the field: "Reset()-time defaults copy only. Runtime drive is independent." Phase 1 reviewer signs off the doc text. |
| R8 | Y-height changes (interactor flying / sliding underground) put samples vertically far from grass roots | 2 | 2 | 4 | XZ-only distance metric in shader (capsule in XZ plane). Documented limitation in `GrassTrailInteractor` XML doc. Out-of-scope for v1 to handle vertical projection. |
| R9 | CPU tier user runs trails and sees nothing happen → confusion | 3 | 2 | 6 | Phase 1 runtime check: one-time `Debug.LogWarning` when `GrassTrailInteractor` activates and any field in scene is CPU-tier. Mirrors existing `GrassInteractor.Update` self-diagnosis. |

All scores < 15. No high-risk mitigation gates beyond per-phase verification.

## Backwards compatibility

- **Pure addition.** `GrassInteractor.cs` untouched. `ScatterField.cs` untouched. `GrassBendSimulator.cs` untouched. `GrassCpuEngine.cs` untouched.
- `GrassGpuEngine.cs` gets ONE new field (`GrassTrailBuffer trailBuffer`) + ONE new line in `Step()` + ONE new line in `Dispose()`. Existing behaviour for fields without any `GrassTrailInteractor` is byte-stable (segment count = 0 → shader loop iterates zero times).
- `GrassInteractIndirect.shader` gets ONE new struct decl + ONE new global decl + ONE new VS loop after the existing interactor loop + ONE constant value change. Fields without trail interactors compile and render identically.

## Rollback plan (per phase, no git)

Project is not a git repo — rollback = file-state revert.

- **Phase 1:** delete `GrassTrailInteractor.cs`. Nothing else references it; field rebuilds unchanged.
- **Phase 2:** delete `GrassTrailBuffer.cs`; remove the 3 added lines in `GrassGpuEngine` (ctor/Step/Dispose) and the staging-array field. Field rebuilds unchanged; Phase 1 component becomes a dead-but-harmless registry entry.
- **Phase 3:** revert `MAX_LEAN` 90°→80° and delete the new VS loop block (delimited by `// TRAIL DEFORM BEGIN` / `// TRAIL DEFORM END` comments). Shader reverts to pre-feature behaviour.
- **Phase 4:** revert the demo scene (Inspector edits only — no script changes from this phase).

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: sampler + stroke breaks + gizmo | M (~3 days) | Pure C# + Editor gizmo. No GPU. Independently testable via PlayMode (gizmo + state inspection). |
| Phase 2: GrassTrailBuffer + upload | S (~1 day) | Mirrors `GrassInteractorBuffer` exactly. Profiler gate: zero GC. |
| Phase 3: shader VS + MAX_LEAN lift | M (~3 days) | Highest art-direction risk (plateau curve shape feel) — iterate visually with the demo until designer-acceptable. |
| Phase 4: demo + verification | S (~1 day) | Wire 1 moving cube, screenshot 4 gates, run 5 harnesses. |
| **Total** | ~8 days | Critical path: 1 → 2 → 3 → 4 (strictly sequential). |

## Requirements traceability (from brainstorm § Requirements)

| Req | Phase |
|---|---|
| R1 persistent trail | P1 + P2 + P3 |
| R2 per-segment auto-fade by `trailDuration` | P1 (age tracking) + P3 (alpha → bend) |
| R3 plateau profile (`centerZonePercent` + `maxBendDegrees` ≤ 90°) | P3 |
| R4 capsule segments (no gaps) | P3 (capsule distance) |
| R5 GPU tier only | P1 (CPU warn) + P2 + P3 |
| R6 stroke breaks via `Emitting` | P1 (state machine) + P2 (skip-pair upload) |
| R7 coexists with `GrassInteractor` | All phases (additive design) |
| R8 90° lean cap | P3 (MAX_LEAN bump) |
| R9 byte-stable to scatter/cull pipeline | P4 (harness gate) |

## Verification matrix

| Phase | Compile gate | Functional gate | Visual gate | Regression gate |
|---|---|---|---|---|
| 1 | 0 C# errors | PlayMode: sample list grows/evicts as expected; `Emitting=false→true` marks one `strokeStart=true` | Gizmo polyline + radius discs + stroke-start ticks visible in Scene view | n/a (no render path touched) |
| 2 | 0 C# errors | execute_code: count `Active` interactors → expected segment count after stroke-break skip | n/a (no shader yet) | Profiler: 0 GC alloc on Step; existing GPU render unchanged (shader doesn't read `_GrassTrail*` yet) |
| 3 | 0 shader warnings | Top-down screenshot: bent trail visible behind moving interactor | Plateau profile visible (flat centre, smooth falloff edge); fade visible (old end recovers); single-interactor baseline unchanged (R1) | All 5 harnesses PASS |
| 4 | n/a | Stroke gap visible mid-trail when test script toggles `Emitting`; trail fully recovered 6 s after sweep ends | 4 screenshots: before / mid-sweep / mid-emit-toggle / 6 s post-sweep | All 5 harnesses PASS; perf ≥ 60 fps at 20k blades + 50-seg trail on dev machine |

## Cook handoff

```
/t1k:cook plans/grass-trail-deform
```

Sequential, one t1k-unity-developer sub-agent per phase, main-loop drives MCP verification + approval gate (consistent with the prior 9-phase GPU cook and the 4-phase Terrain-Scatter cook per project status).
