# Phase 3 — Mesh Prop Kind (GPU-Instanced)

**Delivers:** "Props like grass via texture brush." `kind=Mesh` ScatterLayers render static mesh props (rocks/flowers/bushes) GPU-instanced with LOD + frustum/distance cull, through the existing cull pipeline, painted by the same brush.

## Scope

Generalize the grass GPU-indirect buffer to a generic instance buffer, add a static (no wind/bend) instanced shader, and a `MeshScatterEngine` that drives them — reusing `GrassCull.compute` UNCHANGED. The grass GPU tier must stay byte-stable.

## Files owned (this phase)

| File | Change |
|---|---|
| `Assets/GrassInteract/Runtime/ChunkedInstanceBuffer.cs` | NEW — generalized `ChunkedBladeBuffer`. Same blittable `InstanceData{ Vector3 posWS; uint packedYawScale; uint lodHash }` (20B, layout matches the compute struct), same counting-sort-into-chunks + per-chunk AABB (inflated by mesh bounds extent instead of blade reach) + ChunkRange. Bakes from a `GrassScatterResult` (mesh layers scatter via the SAME `GrassScatter.Build` + `ISurfaceSampler`). |
| `Assets/GrassInteract/Runtime/GrassGpuEngine.cs` OR `ChunkedBladeBuffer.cs` | DECISION: keep grass on `ChunkedBladeBuffer` UNCHANGED (lowest regression risk), and have `ChunkedInstanceBuffer` be a sibling for props. Do NOT force grass onto the new buffer this phase. (Shared-buffer unification is a future refactor, not required for the feature.) |
| `Assets/GrassInteract/Shaders/ScatterInstanced.shader` | NEW — static instanced shader: VS reads `_Instances` (global StructuredBuffer<InstanceData>) + `_VisibleIndices` (per-LOD, material.SetBuffer) via SV_InstanceID, unpacks yaw/scale, applies LOD-mesh + optional `alignToNormal` rotation. NO wind, NO bend, NO interactor loop. 3 passes (Forward/ShadowCaster/DepthOnly), `#pragma target 4.5`, `new RenderParams(material)` ctor (renderingLayerMask trap), `multi_compile_local` for any runtime keyword, prefix any custom HLSL const to avoid `TWO_PI`-style macro clashes. |
| `Assets/GrassInteract/Runtime/MeshScatterEngine.cs` | NEW — `IGrassEngine` impl for `kind=Mesh`. Build: scatter (via injected sampler) → `ChunkedInstanceBuffer.Bake` → per-LOD material clones bound to per-LOD visible-index buffers → InitLodArgs from `layer.meshLODs`. Submit: build frustum planes → `RecordFrameCommands` (REUSE the exact ChunkCull→WriteArgsB→BladeCull→CopyCount×3 sequence from GrassGpuEngine; bladeCullMargin = mesh bounds extent) → `RenderMeshIndirect` ×N LODs with `layer.material`/`layer.meshLODs`. Distance LOD from `layer.lodDistances`. |
| `Assets/GrassInteract/Runtime/ScatterField.cs` | MODIFY — `kind=Mesh` layers now build a `MeshScatterEngine` (was a logged no-op in Phase 2). Mesh layers respect the same `boundTerrain` sampler. |
| `Assets/GrassInteract/Editor/GrassBladeMeshBuilder.cs` (or NEW `ScatterPropMeshes.cs`) | OPTIONAL — placeholder prop meshes (a quad-cross flower, a low-poly rock) for the demo, per placeholder-visuals skill, if no art assets. |
| `Assets/GrassInteract/Editor/ScatterInstanceCullHarness.cs` | NEW — cull-parity harness for the mesh path (mirrors `GrassBladeCullHarness`): synthetic instances + chunks, asserts GPU LOD bucket counts == CPU brute-force, frame-stability, args instanceCount == count, plus the per-instance margin regression. |
| `Assets/GrassInteract/Demo/GrassInteractDemo.unity` (+ a prop material/asset) | MODIFY — add one `kind=Mesh` prop layer (e.g. rocks) to the demo's ScatterField, painted on the terrain, to prove the end-to-end path. |

## Out of scope

- Splat-mask painting, align-to-normal AUTHORING UX, per-layer slope ranges UI (Phase 4 — `alignToNormal` may be applied in-shader here but its painter UX is Phase 4).
- Shared grass+prop unified buffer (explicitly deferred — grass stays on `ChunkedBladeBuffer`).

## Approach notes

- **`GrassCull.compute` is REUSED UNCHANGED.** It culls instances by chunk AABB + distance + LOD using only `posWS` — agnostic to whether the instance is a blade or a prop. `MeshScatterEngine` binds its own buffers to the same kernels. This is the crux of the "generalize the pipeline" decision.
- **Grass byte-stability:** do NOT migrate grass onto `ChunkedInstanceBuffer` this phase. Props get the new buffer; grass keeps `ChunkedBladeBuffer`. Re-run all 3 grass harnesses + screenshot the demo grass to prove no regression.
- Per-instance cull margin = the prop mesh's bounds extent (analog of the grass `bladeCullMargin` fix), passed to the reused kernel via the existing `bladeCullMargin` uniform.
- Reuse `GrassFieldSpace`, `InstanceBatchPool` (RenderMeshInstanced fallback only), the indirect-args buffer setup from `GrassGpuEngine` (copy the proven `RecordFrameCommands`/`InitLodArgsFromMeshes`/`MakeRenderParams` shape).

## Success criteria

1. A `kind=Mesh` prop layer renders instanced props on the terrain, following terrain height/holes/slope (inherits Phase 1 sampler). **Screenshot-verified** at a frustum-edge angle (no pop — inherits the margin fix).
2. Props frustum + distance + LOD cull correctly: `ScatterInstanceCullHarness.Run()` PASS (GPU==CPU counts, frame-stable, args correct, margin regression).
3. Grass GPU + CPU tiers + demo grass render **byte-stable** vs Phase 2 (all 3 grass harnesses PASS, screenshot match).
4. Clean compile (0 C#/shader errors); indirect props verified by SCREENSHOT (not tri count); perf acceptable (measure via `Time.smoothDeltaTime`).

## Verification (live MCP)

Compile → console clean → `ScatterInstanceCullHarness.Run()` + 3 grass harnesses PASS → open demo (terrain + grass layer + rock prop layer), Play, screenshot props instanced on terrain + grass unchanged → grazing-angle screenshot (no prop pop) → A/B forceTier on grass to confirm no regression.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|---|---|
| Generalized buffer / new engine regresses grass GPU tier | 3 | 5 | 15 | Grass stays on `ChunkedBladeBuffer`; props get sibling buffer; re-run all grass harnesses + screenshot each gate |
| Static instanced shader hits a render trap (renderingLayerMask/keyword/macro) | 3 | 4 | 12 | Apply all known gotchas upfront (RenderParams ctor, multi_compile_local, prefixed const); verify by screenshot |
| Reused cull kernel mis-bound for prop buffers | 2 | 4 | 8 | Cull-parity harness asserts GPU==CPU before any render claim |
| Per-instance margin wrong for large props | 2 | 3 | 6 | Margin = mesh bounds extent; harness margin regression with a tall prop |

## Timeline: L (~1 week). Highest-risk phase (buffer generalization + new shader + engine + harness). Long pole = proving grass byte-stability while sharing the cull kernel.
