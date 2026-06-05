# Phase 4 - Deform Reconstruction Rewrite (3-Pass SSOT)

Route: B (primary) or A (fallback) | Effort: M | Blocked by: Phases 2+3 (Route B) OR Phase 1 only (Route A)

## Objective

Rewrite GrassInteract_ApplyDeform so the blade lean is correct: the trampled CORE mats DOWN (no upright spike)
and the lateral lean is BOUNDED to the footprint (no overshoot). The function is called from THREE shader
passes (UniversalForward vert, ShadowCaster shadowVert, DepthOnly depthVert) - the change MUST be identical
across all three so shadow + depth silhouettes match the visible blades. Only the SHARED function body changes;
the per-pass vert functions are untouched.

## File ownership

- Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl - the GrassInteract_ApplyDeform body. Remove the dead
  gradient taps + the _GrassTrampleTexelDensity declaration (line 38) and the e/grad/push gradient block (lines
  57-72) under Route B. Keep the ambient-wind block and the _GrassFlatten height-loss term.
- Assets/GrassInteract/Shaders/GrassInteractInstanced.shader - VERIFY only (no per-pass edits): confirm all
  three passes still include GrassInteractDeform.hlsl and call the shared function with the same signature.
  Respect the existing SSOT fence comments (lines 1-4, 93, 156-158).

## ROUTE B implementation (if Phase 1 PASSED)

1. Sample the multi-channel trample RT once at the blade pivot UV with sampler_LinearClamp:
   float4 t = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_LinearClamp, uv, 0);
2. Unpack per the Phase-2 packing contract: float2 leanDir = (packing); float mag = (packing). Normalize leanDir
   defensively (guard length > 1e-5 -> else zero). mag is already bounded [0,1] from the bake.
3. Lateral lean BOUNDED to the footprint: posWS.xz += leanDir * mag * heightT * _GrassBendStrength. Because mag
   falls to 0 by the footprint edge (Phase 2), the lean is contained; _GrassBendStrength now scales a bounded
   quantity instead of a normalized unit vector (DEFECT 2 fixed). At the core mag is MAX and leanDir is
   well-defined (DEFECT 1 fixed - no zero-gradient singularity).
4. Height-loss (mats DOWN): keep posWS.y -= (posWS.y - pivotWS.y) * mag * heightT * _GrassFlatten. With mag
   maximal at the core, the core blades shorten/press down - the visible mat-down. Consider raising the default
   _GrassFlatten contribution in Phase 7 so the core reads as pressed, not merely leaned.
5. Signature UNCHANGED: void GrassInteract_ApplyDeform(inout float3 posWS, inout float3 nrmWS, float3 pivotWS,
   float heightT). Do NOT change the parameter list (all three passes depend on it).

## ROUTE A implementation (ONLY if Phase 1 FAILED - scalar RHalf map kept)

1. Sample the scalar trample value c at the pivot UV (as today).
2. Straight-DOWN flatten from the VALUE (no gradient): the saturated core/plateaus now mat flat because flatten
   is driven by c directly, not by a gradient that is zero there. posWS.y -= (posWS.y - pivotWS.y) * c * heightT
   * _GrassFlatten (with a larger effective flatten for a real press-down).
3. Lateral lean: keep the gradient DIRECTION (it still points outward on the falloff ring) BUT clamp the lean so
   it cannot exceed the footprint. Replace the normalized push with a magnitude that scales with c and is
   clamped to a footprint-relative cap (e.g. min(c * _GrassBendStrength, worldRadius-derived cap)). Since the
   shader does not know per-interactor radius, derive a global cap from a config field or clamp lean to a small
   multiple of the trample falloff so it stays inside the hot region. No motion-direction lean in Route A.
4. Remove nothing from GrassFieldSpace/GrassTrampleMap (the texel-density gradient global STAYS live for Route
   A); Phase 3 is a no-op under Route A.

## SSOT enforcement (both routes)

- Edit ONLY the shared function body. Do not fork logic into any per-pass vert.
- After editing, re-read GrassInteractInstanced.shader and confirm forward/shadow/depth all route through the
  single function. Update the SSOT fence comments if the include structure changed.

## Success criteria

- Core mats DOWN (no upright spike / crater).
- Lateral lean does NOT exceed the footprint at any radius/strength.
- Forward, ShadowCaster, DepthOnly produce identical deform (verified in Phase 6 criterion d).
- Under Route B: zero references to _GrassTrampleTexelDensity remain (closes the Phase-3 grep).

## Verify

- refresh_unity + read_console clean.
- Phase 6 captures pass criteria (a)-(d). This phase is NOT done until Phase 6 confirms the shape; if Phase 6
  fails, return here.

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All. refresh_unity + read_console after each shader edit.
