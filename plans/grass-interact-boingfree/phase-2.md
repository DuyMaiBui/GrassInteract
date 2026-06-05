# Phase 2 - Ambient wind (in-shader)

Cross-ref: plan.md - brainstorm-grass-interact-boingfree-20260601.md (section D). Blocked by Phase 1. Parallel-safe with Phase 3.
Activate first: t1k-unity-base-code-conventions, unity-urp, unity-shader-graph (HLSL conventions), t1k-unity-base-mcp-skill.

## Objective

Give the idle field a living sway with a cheap in-shader hash/sin wind keyed off each blade pivot XZ (coherent per blade, no texture lookup), scaled by blade height factor (heightT) and anchored at the pivot so roots never slide. All tunables are data-driven from a config (no magic numbers) and bound as globals. Applied identically in all 3 shader passes so shadow + depth match the visible blade.

## Files owned

Modified:
- Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl - implement the wind portion of GrassInteract_ApplyDeform.
- Assets/GrassInteract/Runtime/GrassLODConfig.cs - add a Wind header with windDirection (Vector2, XZ), windStrength, windFrequency, windNoiseScale (all [SerializeField] private + accessors, sensible ranges, no magic numbers).
- Assets/GrassInteract/Runtime/GrassInteractField.cs - bind wind globals (Shader.SetGlobalVector/Float for _GrassWindDir, _GrassWindStrength, _GrassWindFreq, _GrassWindNoiseScale) from the config in OnEnable + a context-menu re-push; cheap enough to also re-push each frame if desired, but prefer on-change.

## Implementation steps

1. GrassLODConfig: add fields windDirection = (1,0), windStrength = 0.15, windFrequency = 1.2, windNoiseScale = 0.25 with [Range]/[Tooltip] and PascalCase accessors. Add a Validate check that windStrength >= 0 (optional).
2. GrassInteractDeform.hlsl: declare globals float4 _GrassWindDir (xy = dir), float _GrassWindStrength, float _GrassWindFreq, float _GrassWindNoiseScale. Implement a cheap hash (e.g. frac(sin(dot(pivotXZ, k)) * c)) + a sin phase advanced by _Time.y * _GrassWindFreq. Wind offset = normalize(windDir) * sin(phase + hashPhase) * _GrassWindStrength * heightT. heightT inside the deform fn must be derived from local height; pass heightT (saturate(uv.y)) into GrassInteract_ApplyDeform OR compute from (posWS.y - pivotWS.y)/bladeHeight. Recommend passing heightT as an explicit arg -> change signature to GrassInteract_ApplyDeform(inout float3 posWS, inout float3 nrmWS, float3 pivotWS, float heightT) and update all 3 passes (DepthOnly/ShadowCaster currently lack uv -> compute heightT from world height delta there, or add uv to their Attributes).
3. Apply wind as a horizontal displacement of posWS proportional to heightT, anchored at pivot (root heightT=0 -> no move). Adjust nrmWS lightly or leave (wind tilt normal effect is subtle; KISS - skip normal bend for wind).
4. GrassInteractField: push wind globals from config in OnEnable and the Rebuild context-menu. Use Shader.PropertyToID cached ids.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| DepthOnly/ShadowCaster passes lack uv -> heightT mismatch -> shadow/depth desync from visible blade | 3 | 3 | 9 | Add uv:TEXCOORD0 to those passes Attributes (mesh already has uv) OR derive heightT from (posWS.y-pivotWS.y)/maxBladeHeight consistently in all passes. Pick one and use everywhere. |
| _Time not available in all passes / animates in edit mode unexpectedly | 2 | 2 | 4 | _Time is a URP global in all passes. In edit mode ExecuteAlways + SceneView animates; acceptable (sway visible in Scene = the verification goal). |
| Wind too strong -> blades shear off pivot / clip ground | 2 | 2 | 4 | Default windStrength low (0.15); strength scales by heightT so root stays put. Tunable in config. |
| Signature change to ApplyDeform breaks Phase 3 expectations | 2 | 3 | 6 | Lock the final signature here (posWS, nrmWS, pivotWS, heightT); Phase 3 layers trample on top inside the same fn. |

## Effort

S

## Scene-window verification gate

1. read_console -> ZERO shader/script errors after refresh.
2. Open GrassInteractDemo.unity. In EDIT mode (no Play), the idle grass field SWAYS in the Scene view (the wind animates via _Time in the SceneView SRP loop).
3. Roots stay anchored (no horizontal slide at the base); tips move most.
4. Change windStrength/windDirection in the config -> sway responds (re-push on enable or context-menu rebuild).
5. Shadows (if shadowCastingMode On) and depth follow the swaying blade (no static shadow under a moving blade).

Done only when: idle field visibly sways in the Scene window in edit mode, roots anchored, tunables responsive, no console errors.
