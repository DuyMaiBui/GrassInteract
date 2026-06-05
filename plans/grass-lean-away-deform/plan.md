# Plan — Grass lean-away trample deform

Status: READY · Created 2026-06-02 · Source design: `plans/reports/brainstorm-grass-lean-away-deform-20260602.md`
Project: GrassInteract — URP 17.3, Unity 6, Mono, GPU-instanced grass, **mobile mandatory**.
Cook handoff: `/t1k:cook plans/grass-lean-away-deform/plan.md`

## Goal

Make grass visibly **lean away** from a moving `GrassInteractor` (it currently shows zero bend
after a clean editor restart — the stale-cache theory is disproven). Direction comes from the
**negative 4-tap gradient of the existing scalar RHalf `_GrassTrampleMap`**, keeping the URP +
instanced + RT + N-interactor + mobile architecture unchanged. Reference repo
`_ref_AlexGrass` (AlexMerzlikin/Roystan) is a **math oracle only** — not ported (its
geometry/tessellation grass is mobile-incompatible).

## Scope

| In scope | Out of scope |
|---|---|
| `Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl` (deform rewrite) | RT format / C# data-path change (unless Phase 0 proves gradient noisy) |
| `Assets/GrassInteract/Shaders/GrassInteractInstanced.shader` (3 passes, `_GRASS_DEBUG` keyword) | `GrassTrampleMap`, `GrassInteractor`, splat shader |
| Live verification via Unity MCP | Geometry/tessellation port; reference C# |

## Phases

| Phase | Name | Effort | Gate |
|---|---|---|---|
| 0 | Verify & root-cause "still nothing" | S (~0.5d) | Sample-hot + include-vs-inline verdict before any new math |
| 1 | Lean-away deform (gradient), cache-safe, 3 passes | M (~1.5d) | Visible lateral lean trailing a moving interactor; shadow/depth match; no FPS regression |

Phase 0 → Phase 1 is strict: Phase 0's evidence decides the Phase 1 delivery form (single
include vs inline-into-3-passes) and confirms the data path before we change deform math.

## Success criteria (whole plan)

1. `_GRASS_DEBUG` keyword renders the per-vertex trample sample as color → confirms hot under a
   moving interactor (data path proven on the live editor).
2. A moving `GrassInteractor` produces a **visible lateral lean** trailing it, recovering after
   it passes — not a faint downward squash.
3. Forward + ShadowCaster + DepthOnly silhouettes agree (identical deform in all three).
4. `rendering_stats`: no material FPS regression vs current at the demo's instance count.
5. Zero per-frame GC; no new shader compile warnings; loose globals stay OUTSIDE
   `UnityPerMaterial`.

## Cross-cutting gotchas (honor in every phase)

- `read_console` is **unreliable** for runtime logs on this Unity MCP bridge → verify via
  `execute_code` return values + GPU `ReadPixels` + a shader-debug-viz.
- Never use `RenderTextureFormat.R8` for a sampled RT (samples as 0 on this stack). RT is
  already RHalf — keep it.
- Loose globals `_GrassFieldRect`, `_GrassWind*` MUST stay outside the `UnityPerMaterial`
  CBUFFER (they're set via `Shader.SetGlobalX`).
- **NEVER** kill/restart the editor or call `Assets/Reimport All` (per
  `unity-forbidden-operations`). To force a shader recompile use targeted reimport of the
  shader asset or touch a `.cs`; clear `Library/ShaderCache/` folder if needed — do NOT nuke.
- Apply the IDENTICAL deform in forward/shadow/depth or silhouettes desync.
- `execute_code` is C# 6 (CodeDom) — no escaped quotes inside interpolated strings.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Include-mechanism genuinely unreliable (inline≠include persists post-restart) | 3 | 4 | 12 | Phase 0 A/B decides; Phase 1 inlines deform into 3 passes with SSOT comment if confirmed |
| Gradient too noisy → jittery lean direction | 2 | 3 | 6 | Fallback to direction-encoding RGHalf RT (Option B); or smooth via larger tap offset |
| 4 taps/vertex too costly at 50k blades on mobile | 2 | 3 | 6 | Profile `rendering_stats`; drop to 2-tap forward diff or precompute gradient in splat |
| Degenerate (zero) gradient at hot peak → no push at center | 3 | 1 | 3 | Acceptable — push magnitude ≈0 at center anyway; guard `length>1e-4` |
| Loose-global / SRP-batcher interaction silently breaks deform | 2 | 4 | 8 | Phase 0 confirms sample hot through include; keep globals outside UnityPerMaterial |

### Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 0: Verify & root-cause | S (~0.5d) | Must complete before any math change |
| Phase 1: Lean-away deform | M (~1.5d) | Delivery form gated on Phase 0 evidence |
| Total | ~2d | Critical path: 0 → 1 (strict) |

## Notes
- Not a git repo (`git: false`) — per-phase commits are N/A; deliver code + live verification.
- Phases: `phase-0.md`, `phase-1.md`.
