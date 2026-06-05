# Phase 5 - Cull-Safety: Chunk AABB / Headroom Review

Route: B / A | Effort: S | Parallel-safe with Phases 2-4 (read-only review until a change is justified)

## Objective

Confirm that the corrected, NOW-BOUNDED lean is still fully covered by the chunk AABBs so bent blades are never
frustum-culled - and note (do NOT silently change) whether the headroom can be reduced now that lean no longer
overshoots the footprint.

## File ownership

- Assets/GrassInteract/Runtime/GrassLODConfig.cs - bendHeadroom field (line 67). REVIEW; change the DEFAULT only
  if the bounded model makes the current 1 m demonstrably excessive AND Phase 6 confirms no culling at a smaller
  value. Any change is a config edit, surfaced in the report - not silent.
- Assets/GrassInteract/Runtime/ChunkGrid.cs - the bladeReachY + lateralPad AABB math (lines 99-136). REVIEW
  only; this math is generic and route-agnostic. Change ONLY if Phase 6 shows a cull at field edges.

## Background (grounded in the code)

ChunkGrid computes: bladeReachY = MaxBladeHeight * maxScale + BendHeadroom; lateralPad = maxScale + BendHeadroom.
The AABB Y span = snapped-terrain range + bladeReachY; XZ expanded by lateralPad. Under the OLD model, lean
could fling tips ~1.18 m+ past the footprint, so the 1 m headroom was already marginal. Under the corrected
model lean is bounded to the footprint, so the existing headroom is SUFFICIENT by construction - the question is
only whether it is now MORE than needed.

## Concrete steps

1. Compute the worst-case corrected lateral lean: under Route B it is bounded by mag(<=1) * _GrassBendStrength
   horizontally, scaled by heightT(<=1) - so worst-case horizontal tip displacement = _GrassBendStrength
   (post-retune value from Phase 7). Compare against lateralPad = maxScale + BendHeadroom.
2. If _GrassBendStrength (retuned) <= lateralPad, headroom is safe - record PASS, change nothing.
3. If the retuned bend could exceed lateralPad at field-edge chunks, RAISE bendHeadroom (do not let blades cull)
   and note it. NEVER shrink below the worst-case lean.
4. Defer the final number until Phase 7 sets the retuned _GrassBendStrength; this phase establishes the
   inequality, Phase 7 plugs in the value, Phase 6 confirms no culling empirically.

## Success criteria

- Documented inequality: worst-case corrected lean <= lateralPad (and <= bladeReachY contribution as applicable).
- No bent blade is frustum-culled at any field-edge chunk in Phase 6 captures.
- Any headroom change is explicit in the report with the before/after value and rationale.

## Verify

- Phase 6 edge-of-field capture (interactor near a field boundary at r2.5) shows no popping/culling of bent
  blades when the camera frames that chunk.
- If headroom changed: re-run the same capture to confirm the new value still covers the lean.

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All.
