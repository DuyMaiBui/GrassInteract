# Phase 6 - Verification Harness (MCP)

Route: both | Effort: L | Blocked by: Phase 4 | Integration gate for the whole plan

## Objective

Prove, with live Unity MCP evidence, that the corrected deform produces the right shape across the full matrix:
{static, moving} interactor x {worldRadius 0.5, 2.5} x {edit mode, play mode}. Capture top-down ORTHOGRAPHIC
and cross-check with the diagnostic radial trample/(vector|gradient) readback. NO source edits in this phase.

## Pass criteria (ALL four, every matrix cell)

- (a) Core MATS DOWN - the most-trampled blades press toward the ground; NO upright spike, crater, or volcano.
- (b) Lean does NOT overshoot the footprint - bent tips stay within ~worldRadius of the interactor; no radial
  explosion past the footprint disc.
- (c) The bent region TRACKS the moving object as a coherent shape - a clean comet/trail following the mover,
  not a smeared or fragmented artifact.
- (d) Shadow + depth MATCH the forward pass - the trample shadow silhouette and the depth-buffer footprint line
  up with the visible bent blades.

## The matrix (8 capture cells + diagnostics)

| Interactor | worldRadius | Mode | Notes |
|------------|-------------|------|-------|
| static | 0.5 | edit | exposes DEFECT 1 (small footprint, no masking) |
| static | 0.5 | play | |
| static | 2.5 | edit | large footprint (old masking case) |
| static | 2.5 | play | |
| moving | 0.5 | play | requires orbit (effector is play-only) |
| moving | 2.5 | play | requires orbit |
| moving | 0.5 | edit | move interactor MANUALLY (effector inert in edit) - see preconditions |
| moving | 2.5 | edit | move interactor MANUALLY |

## Preconditions and TRAPS (read before capturing)

1. MOVING in edit mode: GrassInteractDemoEffector has NO [ExecuteAlways], so it ONLY orbits in PLAY mode. In
   EDIT mode the interactor does NOT auto-move. To test a moving interactor in edit mode, MANUALLY translate the
   GrassInteractor transform between captures (set transform.position via MCP) and capture a short before/after
   sequence; the grass field + trample map DO update live in edit mode (GrassTrampleMap/GrassInteractField run
   via EditorApplication.update). For play-mode moving cases, the effector orbits automatically.
2. PLAY-STATE RECONFIRM (HIGH-RISK mitigation): entering Play can auto-exit on a domain reload. Immediately
   BEFORE each MOVING play-mode capture, re-confirm Application.isPlaying == true (query editor_state /
   manage_editor); if it exited, re-enter Play and re-confirm. A moving capture that silently ran in edit mode
   is a static capture in disguise - it would falsely pass criterion (c).
3. ORTHOGRAPHIC camera ONLY (HIGH-value trap): a perspective top-down camera injects radial parallax streaks
   that LOOK like the DEFECT-2 explosion even on a correct fix. Use a TOP-DOWN ORTHOGRAPHIC camera for every
   shape capture. Document this in the capture notes. The diagnostic radial readback (below) is the objective
   tie-breaker when a screenshot is ambiguous.
4. NEVER kill/quit the Editor; NEVER Reimport All. If MCP times out mid-reload, diagnose per
   unity-forbidden-operations.md (busy != disconnected) and WAIT; do not touch the process.

## Diagnostic radial readback (re-run the original diagnostic)

Reproduce the proven diagnostic: sample the trample RT (and, under Route B, the unpacked direction+magnitude;
under Route A, the value) along a radial line outward from the interactor center. Confirm:
- At r=0: under Route B a NON-ZERO, well-defined lean direction (the DEFECT-1 fix); under Route A a non-zero
  flatten value driving straight-down press. NOT the old gradMag=0 upright core.
- Magnitude/trample falls to ~0 by r = worldRadius (the DEFECT-2 bound).
Record the radial profile numbers alongside the screenshots so the verdict is data-backed, not eyeballed.

## Concrete steps

1. Open the demo scene; ensure a single enabled GrassInteractField + GrassTrampleMap + a GrassInteractor.
2. Place a top-down ORTHOGRAPHIC capture camera framing the interactor footprint.
3. For each matrix cell: set worldRadius, set mode (and reconfirm play state for moving-play cells), position or
   orbit the interactor, capture the top-down screenshot, and run the radial readback.
4. For criterion (d): capture/inspect the shadow and depth (e.g. enable shadow casting on the config or inspect
   the depth/shadow via a debug view) and confirm they track the visible bent blades.
5. Aggregate into a single pass/fail tally per cell against (a)-(d) + the radial numbers.
6. If ANY cell fails, return to Phase 4 (shape) or Phase 2/3 (data) with the specific failing criterion.

## Success criteria

- All 8 cells pass (a)-(d).
- Radial readback confirms non-zero core lean/flatten and footprint-bounded magnitude in every cell.
- Captures are top-down orthographic; moving-play cells are confirmed to have run with isPlaying==true.

## Verify

- MCP screenshots + radial-readback numbers recorded for all 8 cells.
- read_console clean throughout (no shader/runtime errors during captures).

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All. On MCP timeout, diagnose-then-wait; never touch the process.
