# Phase 1: Dumb instanced shader rewrite

**Effort: M** | **Blocked by: Phase 0** | **Blocks: Phase 2**

## Goal

Rewrite `GrassInteractInstanced.shader` into a DUMB instanced URP shader: it draws the blade mesh at
the per-instance object-to-world matrix and colors by a height gradient (`uv.y`). It contains NO
deform include, NO wind, NO trample sampling, and NO `_Grass*` globals. Prove it compiles clean and
renders STATIC instanced grass in Game AND Scene view, edit AND play - independent of any C# motion
(which does not exist yet). This isolates and clears the historically expensive shader bug class
BEFORE any motion logic is layered on.

## File ownership

- `Assets/GrassInteract/Shaders/GrassInteractInstanced.shader` (REWRITE - sole owner this phase).
- Do NOT touch `GrassInteractDeform.hlsl` or `TrampleUpdate.shader` yet (deleted in Phase 4) - just
  stop including the former.

## Concrete steps

1. **Header comment.** Replace the existing top comment (about sampler names) with a short note: this
   is a dumb instanced renderer; all motion is baked into the per-instance matrix by
   `GrassBendSimulator` (C#); no deform/wind/trample lives here. Note the wind-in-shader ESCAPE HATCH
   as a documented-but-unimplemented option (a one-line `_Time` sway in `vert`), so a future mobile
   tune knows where it would go.
2. **Remove the deform include** in all three passes: delete
   `#include "Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl"`.
3. **UniversalForward pass.** Keep `#pragma multi_compile_instancing`, `#pragma target 4.5`,
   `Cull Off`, the `Core.hlsl` include, and the `UnityPerMaterial` cbuffer (`_BaseColor`, `_TipColor`,
   `_BaseMap_ST`). In `vert`: transform `positionOS` to clip space WITHOUT any deform -
   `TransformObjectToWorld` then `TransformWorldToHClip` (the per-instance matrix already carries
   bend+wind+yaw+scale once C# is live; with a plain matrix it is just static placement). Keep
   `heightT = saturate(input.uv.y)` and pass it through. Drop the `pivotWS` computation and the
   `GrassInteract_ApplyDeform` call. `frag` keeps the height-gradient lerp `_BaseColor`->`_TipColor`
   times the optional `_BaseMap` sample.
4. **ShadowCaster pass.** Keep it (shadow mode is still configurable via `GrassLODConfig`). Remove the
   deform include + the `GrassInteract_ApplyDeform` call; keep `ApplyShadowBias` + the reversed-Z
   clamp. The shadow silhouette now matches the matrix-placed blade (correct - motion is in the
   matrix, which the shadow pass also receives via `unity_ObjectToWorld`).
5. **DepthOnly pass.** Same: remove include + deform call; plain world->clip.
6. **Keep** `Fallback Off`, the `RenderType/RenderPipeline/Queue` tags, `LOD 100`, and the three
   `LightMode` pass tags. Do NOT add any `_Grass*` property or global.

## In-editor verification gate

1. After saving the shader, `read_console`: ZERO shader-compile errors/warnings; no magenta error
   shader. (One bad variant magentas everything - this is the gate that historically failed.)
2. Open the demo scene. With the EXISTING field path still using ChunkGrid (Phase 2 not done yet), the
   demo material now references the dumb shader: grass must render as STATIC blades (no motion - the
   old shader-driven wind/trample is gone, which is EXPECTED and correct for this phase) in BOTH Game
   and Scene view, in edit AND play mode.
3. Confirm height gradient is visible (base darker `_BaseColor`, tips lighter `_TipColor`).
4. Confirm no `_Grass*` global is referenced: grep the shader for `_GrassTrample|_GrassWind|_GrassBend|_GrassFlatten|_GrassFieldRect`
   returns ZERO hits.

## Rollback

Before editing, copy `GrassInteractInstanced.shader` to `GrassInteractInstanced.shader.bak` outside
the `Assets/` tree (e.g. `plans/grass-cpu-bend/_backup/`). If the rewrite magentas or fails to render,
restore from the copy and re-investigate. No other file changes, so revert is a single-file restore.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| A bad variant magentas all grass | 2 | 5 | 10 | Keep only `multi_compile_instancing` (+ shadow keyword in the shadow pass); no custom keywords; `read_console` is the hard gate before proceeding. Single-file backup enables instant restore. |
| Shadow/depth pass diverges from forward (mismatched silhouette) | 2 | 2 | 4 | All three passes use the SAME plain world->clip transform now; no per-pass deform to desync. |
| ShaderCache.db serves a stale compile | 2 | 3 | 6 | If a stale compile is suspected, touch the .shader (re-save) and `refresh_unity(mode=force, scope=scripts)`; do NOT Reimport All. The whole point of this refactor is to remove the cache-sensitive deform code. |
| Grass renders nothing (matProps/RenderGraph regression) | 1 | 4 | 4 | Render path (RenderMeshInstanced from player loop, rp.camera=null, no rp.matProps) is unchanged in Phase 1 - only the shader changed; the call-site discipline is preserved by Phase 2. |
