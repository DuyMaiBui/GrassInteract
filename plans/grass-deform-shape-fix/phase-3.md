# Phase 3 - RT Format Switch + Dead Gradient-Global Removal (ROUTE B)

Route: B | Effort: M | Blocked by: Phase 1 PASS | Runs BEFORE Phase 2 (format must exist before the bake writes it)
No-op under Route A.

## Objective

Switch the trample RenderTexture from single-channel RHalf to the multi-channel format VERIFIED in Phase 1, so
the Phase-2 bake has channels to write into. Then cleanly remove the now-dead gradient infrastructure
(_GrassTrampleTexelDensity + its binding path), because Route B reads the vector directly and never taps a
central-difference gradient again.

## File ownership

- Assets/GrassInteract/Runtime/GrassTrampleMap.cs - CreateRT: RenderTextureFormat.RHalf -> the Phase-1 verified
  format (e.g. RGHalf or ARGBHalf). Update the format gotcha comment to record the NEW verified format and WHY
  (extend, do not delete, the existing R8-samples-zero note). Remove the BindTrampleTexelDensity call from
  Allocate (line ~130) since the gradient tap offset is dead.
- Assets/GrassInteract/Runtime/GrassFieldSpace.cs - remove TexelDensityId field (line 25) and the
  BindTrampleTexelDensity method (lines 64-80) and its doc comment. Keep FieldRectId + BindGlobals + WorldToUv
  + UvToWorld (all still live).
- Assets/GrassInteract/Runtime/GrassInteractField.cs - remove the stale doc reference to
  _GrassTrampleTexelDensity in the BindDeformGlobals comment (line ~132). No code change there (it never bound
  the texel density).

## Pre-delete reference check (MANDATORY per development-principles.md)

Already mapped - the global _GrassTrampleTexelDensity has exactly these live references; ALL must go:
- GrassFieldSpace.cs:25  (TexelDensityId field)
- GrassFieldSpace.cs:68,77,79  (doc + BindTrampleTexelDensity method body)
- GrassInteractField.cs:132  (doc comment only)
- GrassTrampleMap.cs:130  (BindTrampleTexelDensity call)
- GrassInteractDeform.hlsl:38,59  (declaration + gradient tap usage) - REMOVED in Phase 4, noted here for SSOT

Re-run the grep before deleting to catch anything added since planning:
grep -rn "_GrassTrampleTexelDensity\|TexelDensity\|BindTrampleTexelDensity" Assets/GrassInteract/
Expected after Phase 3+4: ZERO hits.

## Concrete steps

1. CreateRT: change the format enum to the Phase-1 verified one. Keep wrapMode=Clamp, filterMode=Bilinear,
   useMipMap=false. Update the gotcha comment.
2. Update ClearRT semantics if needed: Color.clear is fine (0,0,0,0) = zero direction + zero magnitude = upright.
3. Remove the BindTrampleTexelDensity call in Allocate and the whole method + field in GrassFieldSpace.
4. Update the GrassInteractField doc comment (drop the dead _GrassTrampleTexelDensity sentence).
5. Compile-check; the shader-side declaration/usage removal is Phase 4 - until Phase 4 lands, the unused HLSL
   global is harmless (it just never gets bound), but DO land Phase 3+4 together before any verification so the
   grep returns zero.

## Success criteria

- Trample RT allocates in the verified multi-channel format; project compiles clean.
- _GrassTrampleTexelDensity has ZERO references across runtime + shaders after Phase 3+4.
- The existing R8 gotcha note is preserved and EXTENDED (not replaced) with the new format rationale.

## Verify

- grep returns zero hits for the dead global after Phase 3+4.
- refresh_unity + read_console clean.
- Debug overlay still renders the trample RT (now multi-channel) without errors; the off-field warning path is
  untouched and still fires correctly.

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All. refresh_unity + read_console after the format change.
