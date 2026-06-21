# Plan: WorldPainter Mobile Render Optimization — 45→60 FPS on Adreno 730 (Approach C)

**Date:** 2026-06-20 · **Scope:** C — full whole-pipeline overhaul · **Target:** sustained 60 FPS on Adreno 730 (Snapdragon 8 Gen 1 class, mid-tier)
**Source of truth:** `plans/reports/mobile-render-60fps-overhaul-brainstorm.md` (verified via 30-agent adversarial workflow `woeehsl28`). This plan operationalizes that brainstorm; it does NOT re-derive findings.

---

## The honest reframe (read this before anything else)

The frame is **FRAGMENT-BOUND on a TBDR GPU** (Adreno 730 = tile-based deferred rendering). Adversarial verification against the *serialized config* (not assumptions) established:

- **The 60 FPS goal is won primarily by the render-scale governor (Phase 1).** Render scale is the one lever that scales fragment cost across *all* passes (0.8× scale = 0.64× fragments). Everything else in this plan is **stability, regression-insurance, native-resolution quality, or future-proofing** — NOT the path to 60. Do not imply the micro-opts get you there; on a fragment-bound TBDR frame each verified to ~0–1.5 fps.
- **Three "classic" suspects are already neutralized — do NOT re-attempt:**
  - Grass is **solid-opaque** (`useAlphaclip:0`). The "alpha-test defeats HSR" penalty is NOT occurring. **Guardrail: never add alpha-blend / dithered coverage** — it disables ZWrite/HSR for −5–15 fps.
  - Grass **casts no shadows** (`ShadowCastingMode.Off`). No win available.
  - **No depth prepass on Medium** (`RequireDepthTexture:0`). Grass DepthOnly pass is dormant on device.
- **Editor LIES about device cost.** Editor runs **Ultra** (depth-texture ON); device ships **Medium** (depth OFF). In-editor profiling overstates depth-pass cost and misrepresents the whole frame. **EVERY phase verification gate in this plan is ON-DEVICE, never editor-only.**

## Success metrics (global)

| Metric | Target |
|---|---|
| Sustained FPS (grass-heavy ground-level framing) | ≥ 60 on Adreno 730 |
| Sustained FPS (terrain-heavy vista framing) | ≥ 60 on Adreno 730 |
| 1% low | ≥ 55 FPS |
| Governor idle render scale in light views | ≈ 1.0× (no visible softening) |
| Multi-frame hitches on grass field load / density swap | 0 |
| Build validator | fails the build on any tier / HDR / MSAA / ForceCpu violation |
| Visual quality | no visible softening in light scenes; under-load softening preferred over frame drops |

## Phases

- **Phase 0 — Measure-first de-risk** (S) — on-device PerformanceConsole `Scale`-button test; prove the ~80% fragment fraction BEFORE building the governor.
- **Phase 1 — Render-scale governor + FSR** (M) — **P0, the headline win.** Frame-time PI controller, FSR upscale + override-sharpness, floor 0.65 / ceil 1.0.
- **Phase 2 — Build validator** (M) — P1 insurance. `IPreprocessBuildWithReport`; block ForceCpu + tier/HDR/MSAA/shadowmap/AlwaysIncludedShaders regressions.
- **Phase 3 — Adaptive grass density controller** (M) — P2. Second frame-time knob in `BladeCull` (stable-hash skip + dynamic cull distance), density floor.
- **Phase 4 — Grass scatter bake → `BakedGrassData` blob** (L) — P2. Mirror `AuthoredInstancesData` V3 byte-blob; runtime Build = 3× SetData. Enables Phase 3 swaps as buffer-reload.
- **Phase 5 — Native-res quality bundle** (L) — P2. LOD2 far-field falloff, terrain normal bake, ASTC+mips alphamaps, layer-count variants, frag interpolator strip.
- **Phase 6 — C extras / deferred insurance** (M) — variant stripping + warmup (1% lows); prop impostor atlas (DEFERRED — 0 props today); per-tile streamed bake (only if world > streaming radius).

## Cross-phase dependencies

- **Phase 0 gates Phase 1.** If the on-device scale test shows a low fragment fraction, the Phase-1 headline is weaker and the Phase-5 quality bundle carries more weight. Do not build the governor before Phase 0 confirms the lever.
- **Phase 4 enables Phase 3 swaps.** Phase 3 density swaps are cheap buffer-reloads *only once Phase 4's baked blob exists* — until then a density swap forces a main-thread re-scatter spike. Phase 3 ships first (it works against the live path), but its hitch-free guarantee depends on Phase 4. Sequence note carried in both phase files.
- **Phases 1 and 3 share one signal** — the smoothed `avgMs` from the PerformanceConsole ring buffer. The density knob (3) lets the resolution knob (1) swing less far.
- Phases 2, 5, 6 are largely independent and parallel-safe with each other once 0/1 land.

## Critical path

**Phase 0 → Phase 1** is the only hard critical path to the 60 FPS goal. Phases 2–6 are insurance / quality / future-proofing layered after the lever is proven and built. Recommended order = 0 → 1 → 2 → 3 → 4 → 5 → 6 (front-loads value, gates risk).

---

## Risk Assessment (aggregate)

| # | Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|------|:---:|:---:|:---:|------------|
| R1 | Fragment fraction < assumed ~80% → governor under-delivers | 2 | 5 | **10** | Phase 0 measures it first; if low, escalate Phase 5 quality bundle weight. Governor still nets positive. |
| R2 | Scatter-bake buffer-layout mismatch corrupts blade rendering (struct stride / chunk-range drift) | 3 | 5 | **15** | Keep `ValidatePartition` as a CI gate; byte-exact mirror of `AuthoredInstancesData` V3; round-trip EditMode test (bake→load→compare); branch on `BakedGrass != null` so live path is the fallback. |
| R3 | ASTC palette albedo "import flag" assumption wrong — it is RGBA32-in-code (`TerrainPaletteBinder`) → silent no-op or binder regression | 3 | 4 | **12** | Scope as M not S; it is a binder rewrite (Texture2DArray format change in code), NOT an importer flag. Alphamap mips ARE an import flag and are the bigger win — land those first. |
| R4 | Frag interpolator strip references a stripped member (`normalize(i.normalWS)`) → shader compile break | 3 | 3 | **9** | Gate the `normalize(i.normalWS)` read under the SAME keyword as the interpolator declaration; on-device variant compile check. |
| R5 | Render-scale upscale softens UI/HUD text | 2 | 3 | **6** | Verify URP overlay-after-upscale renders UI at native; on-device readability check is a Phase 1 gate. |
| R6 | Governor oscillation (resolution pumping) is visible/annoying | 2 | 3 | **6** | Hysteresis band + step cooldown + smoothed avgMs (not instantaneous); tuned in Phase 0/1 on-device. |
| R7 | Build validator false-positive blocks a legitimate build | 2 | 2 | **4** | Assertions read serialized config the brainstorm already verified; clear `BuildFailedException` messages with the offending value + fix. |
| R8 | EditMode test-runner wedged (`Cannot access a disposed object`) blocks CI coverage for Phases 3/4/5 | 3 | 2 | **6** | Manual `Window ▸ General ▸ Test Runner` run after Unity restart; phases adding compute/buffer logic extend coverage where feasible but do not block on the wedged runner. |

**Score ≥ 15 (high risk, mandate mitigation before that phase starts):** R2 (scatter-bake buffer layout, Phase 4). R3 (ASTC binder scope, Phase 5) at 12 is flagged as a scope-correction risk — re-scope before starting, do not treat as S-effort.

---

## Timeline

| Phase | Effort | Notes / blocker |
|---|:---:|---|
| Phase 0 — Measure-first | S | Gates Phase 1. Pure on-device measurement, no code. |
| Phase 1 — Render-scale governor + FSR | M | **Critical path.** Blocked by Phase 0. Reuses PerformanceConsole plumbing. |
| Phase 2 — Build validator | M | Independent; parallel-safe after Phase 1. Insurance only. |
| Phase 3 — Adaptive density | M | Shares Phase 1 signal. Hitch-free guarantee depends on Phase 4. |
| Phase 4 — Scatter bake blob | L | **High-risk (R2).** Enables Phase 3 swap; refactor + editor tool + runtime branch. |
| Phase 5 — Native-res quality bundle | L | Independent; 5 sub-items. R3/R4 scope risks. |
| Phase 6 — C extras / deferred | M | Variant warmup active; prop impostor DEFERRED (0 props); per-tile bake conditional. |
| **Total** | **~L×2 + M×4 + S** | Critical path to 60 FPS: **Phase 0 → Phase 1**. Remainder is insurance/quality/future-proofing. |

---

## Per-phase files

- `phase-0.md` — Measure-first de-risk
- `phase-1.md` — Render-scale governor + FSR
- `phase-2.md` — Build validator
- `phase-3.md` — Adaptive grass density controller
- `phase-4.md` — Grass scatter bake → BakedGrassData blob
- `phase-5.md` — Native-res quality bundle
- `phase-6.md` — C extras / deferred insurance
