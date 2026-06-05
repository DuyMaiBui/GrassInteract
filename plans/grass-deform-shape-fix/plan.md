# Plan: GrassInteract Trample/Lean Deform Shape Fix

Created: 2026-06-02 14:26 | Slug: grass-deform-shape-fix | Owner: Tech Lead handoff to cook
Status: READY (no unresolved items)

---

## Problem (root cause ALREADY PROVEN, do NOT re-diagnose)

When a GameObject carrying GrassInteractor moves, the grass bend is the wrong shape. The bug is in the deform
RECONSTRUCTION (GrassInteract_ApplyDeform in GrassInteractDeform.hlsl), NOT the trample map, NOT worldRadius,
NOT RT resolution, trail dashing, or anisotropy (all ruled out with live evidence).

Two structural defects in the current gradient-lean-away model:

- DEFECT 1 (upright core). push = -normalize(grad). At the footprint center the scalar trample mask is a local
  maximum, so grad ~ 0 -> push = 0 -> the MOST-trampled blades get ZERO lateral lean (crater/volcano with an
  upright spike). Overlapping max() splats saturate into flat plateaus -> zero gradient -> whole upright patches
  inside the trampled area.
- DEFECT 2 (size-independent overshoot). push is normalized, so lean magnitude = trample * bendStrength,
  INDEPENDENT of footprint size. With bendStrength (1.5-3.5 m) much greater than worldRadius (0.5 m), slope
  blades fling tips ~1.18 m outward, well past the footprint -> radial explosion, not a contained mat-down.

## Fix route (DECIDED, do NOT re-ask)

Per library-quality-mandate.md (reusable grass lib, zero tech debt): PRIMARY = ROUTE B, FALLBACK = ROUTE A,
gated on an early de-risk spike.

- ROUTE B (primary). Bake a push VECTOR + magnitude into the trample map. TrampleUpdate.shader writes
  float4(pushDir.xy, magnitude, 0); direction = away-from-interactor in world XZ (optionally blended with the
  interactor motion heading), magnitude = s * saturate(1 - d/r) accumulated. GrassTrampleMap.CreateRT switches
  RHalf to a verified multi-channel format. GrassInteractDeform.hlsl reads the vector directly: no gradient, no
  zero-core singularity, magnitude bounded -> no overshoot. _GrassFlatten stays as the height-loss term so the
  core mats DOWN.
- ROUTE A (fallback, scalar map kept, edits ONLY GrassInteractDeform.hlsl). Drive a straight-DOWN flatten from
  the trample VALUE (no gradient -> saturated core/plateaus mat flat), and scale/clamp the lateral lean so it
  cannot exceed the footprint. No motion-direction lean. Taken ONLY if the Phase 1 spike fails.

## Phases

| # | Name | Scope (files owned) | Effort | Route |
|---|------|---------------------|--------|-------|
| 1 | De-risk spike: multi-channel RT in-shader read | scratch shader/script (no key-file edits) | S | gate |
| 2 | Trample-map vector bake | TrampleUpdate.shader, GrassInteractor.cs (read), GrassTrampleMap.cs | M | B |
| 3 | RT format switch + dead-gradient-global removal | GrassTrampleMap.cs (CreateRT), GrassFieldSpace.cs, GrassInteractField.cs | M | B |
| 4 | Deform reconstruction rewrite (3-pass SSOT) | GrassInteractDeform.hlsl (+ verify GrassInteractInstanced.shader) | M | B / A |
| 5 | Cull-safety: chunk AABB / headroom review | GrassLODConfig.cs, ChunkGrid.cs (review; change only if warranted) | S | B / A |
| 6 | Verification harness (MCP) | no source edits; MCP-driven capture | L | both |
| 7 | Re-tune defaults for corrected model | GrassInteractDemoConfig.asset, GrassLODConfig.cs (defaults) | S | both |
| 8 | Docs sync: library skill + reference notes | .claude/skills (note gap), in-code SSOT/gotcha comments | S | both |

Route-A fallback: if Phase 1 fails, Phases 2/3/5 collapse to no-ops, Phase 4 implements the scalar Route A
straight-down-flatten + clamped-lean variant, and Phases 6/7/8 proceed unchanged. Stated explicitly in
phase-1.md (decision gate) and phase-4.md (both code variants documented).

## Feasibility

- Reuse check: all edits land in EXISTING files. NO new runtime systems. NO new files except a throwaway spike
  scratch asset (deleted in Phase 1) and possibly one new library-skill doc (Phase 8). Reuse-first satisfied.
- Complexity: moderate. Risk concentrates in Phase 1 (GPU format-sample correctness) and Phase 4 (3-pass SSOT
  identical-behavior constraint). Everything else is mechanical.

## Dependencies

- Blocks: Phase 4 blocks Phase 6 (cannot verify shape until reconstruction is fixed). Phase 6 blocks Phase 7
  (cannot re-tune until corrected model renders). Phase 7 blocks final sign-off.
- Blocked by: Phase 1 gates Phases 2-5 (Route B vs A). Phase 3 lands the multi-channel format; Phase 2 lands the
  bake that writes those channels -> run 3 before 2 (format first), or merge them (disjoint regions of
  GrassTrampleMap.cs except CreateRT). Phase 4 (shader read) is blocked by both 2 and 3. Phase 5 is
  parallel-safe with 2-4 (read-only review until a change is justified).

## Critical path

Phase 1 -> Phase 3 -> Phase 2 -> Phase 4 -> Phase 6 -> Phase 7 -> sign-off
(Phase 5 runs alongside 2-4; Phase 8 runs alongside 6-7.)

## Risk Assessment (MANDATORY)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Chosen multi-channel RT format ALSO samples zero in-shader (same class as the R8 bug) | 3 | 5 | 15 HIGH | Phase 1 spike is FIRST and a hard gate: verify a real blade reads non-zero in-shader (NOT CPU readback). Fail -> Route A (keeps proven-good RHalf scalar). No Route-B code lands until the spike passes. |
| 3-pass SSOT drift: forward/shadow/depth deform diverge -> shadow/depth silhouette mismatch | 3 | 4 | 12 | All three call the SAME GrassInteract_ApplyDeform; change ONLY the shared body, never per-pass vert. Respect existing SSOT fence comments. Phase 6 criterion (d) diffs shadow+depth vs forward. |
| Bounded lean still clips chunk AABB at field edges -> bent blades frustum-culled | 2 | 3 | 6 | Phase 5 reviews bendHeadroom/ChunkGrid against the NOW-bounded lean; keep current headroom unless proven excessive; never silently shrink. |
| Perspective top-down capture injects radial parallax streaks -> false explosion reading | 4 | 3 | 12 | Phase 6 MANDATES a top-down ORTHOGRAPHIC camera (documented trap). Diagnostic radial readback is the objective tie-breaker, not eyeballed screenshots. |
| Moving-case capture silently runs in edit mode (effector has no ExecuteAlways so interactor never orbits) | 4 | 4 | 16 HIGH | Phase 6 re-confirms Application.isPlaying == true immediately before EACH moving capture; domain reload can auto-exit Play, so re-enter + re-confirm. Documented in phase-6.md preconditions. |
| Dead-global removal (_GrassTrampleTexelDensity) misses a reference -> compile error/stale binding | 2 | 2 | 4 | Phase 3 runs the pre-delete grep (already mapped: 6 references across 3 files + shader), removes atomically, compile-checks via read_console. |
| Default re-tune looks subjectively worse than the buggy baseline reviewers remember | 2 | 2 | 4 | Phase 7 re-tunes against Phase 6 objective criteria (mats-down, no overshoot, tracks mover), not nostalgia; capture before/after. |

Two HIGH risks (score >= 15): the format-sample risk (mitigated by the Phase 1 gate) and the
edit-mode-moving-capture risk (mitigated by the explicit play-state reconfirm in Phase 6). Both mitigations are
mandated BEFORE the phase proceeds.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| 1 De-risk spike | S | GATE: decides Route B vs A; blocks 2-5 |
| 2 Vector bake | M | blocked by 1; couples with 3 |
| 3 RT format + dead-global removal | M | blocked by 1; runs before 2 (format first) |
| 4 Deform rewrite (3-pass SSOT) | M | blocked by 2+3 (B) or by 1 only (A) |
| 5 Cull-safety review | S | parallel with 2-4 |
| 6 Verification harness | L | blocked by 4; static+moving x r0.5+r2.5 x edit+play |
| 7 Re-tune defaults | S | blocked by 6 |
| 8 Docs sync | S | parallel with 6-7 |
| Total | ~M-L | Critical path: 1 -> 3 -> 2 -> 4 -> 6 -> 7 |

## Library-quality mandate compliance (every phase honors)

- Naming: genre/perspective-neutral. No Demo/Survivor/Car tokens in library code. New shader fields follow the
  _GrassXxx convention.
- Data-driven: every tunable is a GrassLODConfig SerializeField (no magic numbers in shader/runtime). Any new
  tunable (e.g. a motion-heading blend weight) becomes a config field, NOT an inline literal.
- Skill update in the SAME change: Phase 8 updates the owning library skill. NOTE: a search of .claude/skills
  found NO grass / interact-deform skill -> Phase 8 records this gap and proposes the doc location (phase-8.md).
- SSOT: the deform function stays single-source across the 3 shader passes; the field rect stays SSOT in
  GrassFieldSpace.

## Rollback (per phase)

Every phase is a discrete, revertible edit set with no cascading damage. P1 spike artifacts are throwaway. P2,
P3, P4 revert the shader/script diffs; the proven-good RHalf-scalar path is the last-known-good baseline. P5 is
review-only unless a change lands. P7 is a YAML value revert. P8 is doc-only. Each phase commits independently
(agent-completion-discipline.md: commit before summary).

## Verification matrix

See each phase-N.md Verify block. Phase 6 is the integration gate with four objective pass criteria:
(a) core MATS DOWN (not upright), (b) lean does NOT overshoot the footprint, (c) bent region tracks the moving
object as a coherent shape, (d) shadow + depth match the forward pass; across static+moving x r0.5+r2.5 x
edit+play, captured top-down ORTHOGRAPHIC, cross-checked with the diagnostic radial readback.

## Unity safety

NEVER kill/quit the Unity Editor; NEVER call Reimport All (unity-forbidden-operations.md). After shader/script
edits use refresh_unity + read_console to confirm clean compile before proceeding.
