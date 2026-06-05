# Brainstorm — BoingKit-free Interactive Grass + Terrain Paint Tool

**Date:** 2026-06-01 · **Project:** GrassInteract (Unity 6, URP 17.3, Mono) · **Supersedes Boing dependency** of `brainstorm-grass-instanced-rendering-20260601.md` (rendering/LOD/chunk core retained).

## Problem statement
Existing system (`Assets/GrassInteract/`) renders 10k–50k instanced blades with CPU chunk frustum-cull + LOD, but: (a) bending is delegated to **Boing Kit** `BoingReactorField` — user wants it gone; (b) grass is **invisible in the Scene window** (debug blind spot); (c) placement is uniform-random — user wants to **paint grass on terrain with a brush** and **save a grass layer**.

## Requirements (user-confirmed decisions)
1. **No BoingKit** for grass interaction (Boing stays installed for other uses; grass fully decouples).
2. **Interaction = trample RenderTexture map** — persistent flattened trails that recover over time; interactor-count-independent cost.
3. **Placement = density-map texture** — brush paints density; instances generated deterministically at load.
4. **Brush paints on any collider** (Unity Terrain, mesh, plane) via SceneView raycast.
5. **Ambient wind** sway in-shader, layered under interaction.
6. **Debug display in Scene window** (and edit-mode rendering).
7. **Data layout:** separate `GrassLayer` asset (densityMap + placement) referencing a `GrassLODConfig` (render/LOD).

## Unifying architecture — two top-down maps over one field rect
All sampling keys off one world→UV mapping over the field XZ rect, centralized as `_GrassFieldRect = (originX, originZ, sizeX, sizeZ)` and a shared `GrassFieldSpace` helper (used by C#, brush, shaders) so the two maps never drift:

| Map | Type | Lifetime | Written by | Read by |
|---|---|---|---|---|
| Density map | `Texture2D` R8 (readable, uncompressed) | static / authoring | brush tool | `ChunkGrid` at load |
| Trample map | `RenderTexture` R8 | dynamic / runtime | interactors per frame | vertex shader |

## Approaches evaluated
- **Interaction:** global interactor-array uniform (rejected: transient only, no trail) · **trample RT (chosen)** · both-staged (rejected: user wants trail now).
- **Data model:** explicit per-blade instance list (rejected: 50k-entry asset, no re-tune) · **density-map texture (chosen)**.
- **Paint target:** Terrain-only (rejected: less reusable) · **any collider via raycast (chosen)**.
- **Asset layout:** fold into GrassLODConfig (rejected) · **separate GrassLayer (chosen)** — multiple layers can share one render config.

## Recommended solution (per subsystem)

### A. Decouple BoingKit
- Shader: remove 3 `#include ".../BoingKit.cginc"` + `GrassInteract_ApplyLean`; replace `GrassInteractBend.hlsl` → new `GrassInteractDeform.hlsl` (wind + trample). `#pragma target` may drop 4.5→3.5 (no StructuredBuffer).
- Runtime: `GrassInteractField` drops `BoingReactorField`; `GrassRenderer.Render` drops `field` param + `UpdateShaderConstants`; `GrassLODConfig` drops `positionSampleMultiplier`/`rotationSampleMultiplier`.

### B. Scene-window + edit-mode rendering (fixes invisibility)
- Root cause: `Graphics.DrawMeshInstanced(..., camera: gameCamera)` draws to one camera only.
- Subscribe `RenderPipelineManager.beginCameraRendering`; for each Game + SceneView camera, cull+LOD against that camera and submit via `Graphics.RenderMeshInstanced` (modern API; also removes `[Obsolete]` warning). `[ExecuteAlways]` for edit-mode; unsubscribe in `OnDisable`. Field-rect gizmo + existing chunk-AABB gizmos.

### C. Trample RT interaction
- `GrassTrampleMap` (MonoBehaviour): owns R8 RT over field rect. Per `LateUpdate`: (1) fade ping-pong blit `value *= recovery`; (2) `CommandBuffer` splat each interactor footprint additively at world-XZ→UV→NDC; (3) push `_GrassTrampleMap` + `_GrassFieldRect` globals.
- `GrassInteractor` (MonoBehaviour): registers world pos + radius + strength; attach to car/wheels/player. Demo effector becomes one.
- Shader: sample trample once per blade (pivot XZ), fold blade toward ground by `trample * heightT` + per-blade hashed splay. Magnitude-only (direction-agnostic). **Upgrade:** RG map for directional flow.

### D. Ambient wind
- `GrassInteractDeform.hlsl`: cheap hash/sin noise by pivot XZ (coherent per blade, no texture), sway ∝ heightT, pivot-anchored. Tunables `_GrassWindDir/Strength/Freq/NoiseScale`. All 3 passes; flatten layers on top.

### E. Density-map placement
- `GrassLayer` ScriptableObject: readable R8 `Texture2D densityMap` + `targetDensity` + `scaleRange` + `seed` + `groundSnapMask` + `GrassLODConfig renderConfig`.
- `ChunkGrid.Build` rewrite: seeded candidate XZ → keep w/ probability = sampled density → raycast down to snap Y onto ground collider (fallback field-plane Y). Preserves seeded-deterministic, chunk-bucketed, pooled flow.

### F. Editor brush tool
- `GrassPainterWindow` (EditorWindow) + `SceneView.duringSceneGui`: Paint/Erase, radius/strength/falloff sliders; mouse-drag → `GUIPointToWorldRay` → `Physics.Raycast` (any collider) → density pixels, soft circular stamp, throttled `Apply`; `Handles` disc gizmo; density overlay + preview-instances toggles; Save via `SetPixels`/`Apply`/`SetDirty`/`SaveAssets`.

## Implementation phases (each compiles clean + Scene-window verifiable)
0. Decouple Boing — static grass renders.
1. Scene-view/edit-mode render via `beginCameraRendering` + `RenderMeshInstanced`.
2. Ambient wind in shader.
3. Trample RT + `GrassInteractor` + fade-recover.
4. `GrassLayer` density placement (`ChunkGrid` rejection-sampling + ground snap).
5. `GrassPainterWindow` brush (paint/erase/save/overlay).
6. Demo rewire + README update + drop Boing config fields.

## Risks / mitigations
- Trample & density UV must match → centralized `GrassFieldSpace`.
- `beginCameraRendering` leak/double-draw → unsubscribe in `OnDisable`.
- Density map must stay readable + uncompressed (point/bilinear).
- Ground-snap needs colliders at build (Terrain has one); fallback plane Y.
- Magnitude-only trample = fold-down look; RG-direction = documented upgrade.
- URP RT splat: `CommandBuffer.SetRenderTarget` + `DrawMesh` (no camera), or `Blit` ping-pong.

## Naming (library charter — generic, no demo tokens, `GrassInteract` ns)
`GrassTrampleMap`, `GrassInteractor`, `GrassLayer`, `GrassPainterWindow`, `GrassFieldSpace`, `GrassInteractDeform.hlsl`.

## Success criteria
- Grass renders in Scene window in edit + play mode; no Boing include/reference remains in grass code.
- Painting on the terrain produces grass; layer saves + reloads deterministically.
- Car/effector leaves a flattened trail that recovers; idle field sways with wind.
- ≥10k blades, no per-frame GC, obsolete-API warning gone.

## Next step
Hand off to `/t1k:plan` to produce the phased, file-owned implementation plan with per-phase verification gates.
