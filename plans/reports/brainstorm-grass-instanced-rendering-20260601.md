# Brainstorm Report — Interactive Grass Instanced Rendering (frustum cull + LOD + pooling)

**Date:** 2026-06-01
**Project:** GrassInteract (Unity 6, URP 17.3, Mono — no DOTS)
**Status:** Design approved, ready for `/t1k:plan`

## Problem statement

Render 10k–50k interactive grass blades on **mobile** that:
1. Use **GPU instancing** to draw many meshes cheaply.
2. **Frustum-cull** off-screen blades.
3. **Recycle** instance draw data via an object pool (no per-frame GC).
4. Use **LOD** to reduce mesh detail with distance.

Interaction = grass bends when the car/effectors pass through (existing Boing Kit `BoingReactorField`).

## Key finding — interaction is already solved

`BoingReactorField` (Boing Kit) is a GPU-compute spring grid. Effectors push cells; the **grass vertex shader** reads the field's `ComputeBuffer`s (bound via `UpdateShaderConstants(MaterialPropertyBlock)`) and bends blades by world position.

- Interaction cost is **O(cells), not O(instances)** — 10k vs 50k blades interact at identical cost.
- The 4 asks are therefore a **rendering-side upgrade only**; the field stays the untouched interaction engine.
- Existing `BushFieldReactorFieldMain` already has a primitive instanced path (deprecated `Graphics.DrawMeshInstanced`, 1000/batch, shared MPB carrying field buffers) but lacks frustum cull, LOD, recycling, and uses a deprecated API.

## Approaches evaluated

| Option | Cull | Draw | Mobile fit | Ceiling | Verdict |
|---|---|---|---|---|---|
| **A — CPU-chunked** | chunk-AABB on CPU | `RenderMeshInstanced` | ✅ bulletproof | 100k+ | **CHOSEN** |
| B — GPU-indirect | compute per-instance | `RenderMeshIndirect` | ⚠️ compute-cull device-risky | highest | future upgrade |
| GameObject pool + LODGroup | Unity built-in | auto-batch | ✅ but high CPU/GO overhead | ~few k | rejected (won't reach 50k) |

## Approved design (Option A)

### Decisions locked
- **Backend:** Option A — CPU-chunked frustum cull + `Graphics.RenderMeshInstanced` + pooled batch slabs.
- **LOD:** Discrete **mesh swap** LOD0/LOD1/LOD2 by camera distance (no density-thinning, no billboards this pass).
- **Scope:** New **reusable** system under `Assets/GrassInteract/` that *uses* `BoingReactorField`. Boing demo untouched (survives asset reimport/update).
- **Grass asset:** User provides grass mesh + instancing-capable URP shader; we **adapt the shader** to sample the Boing field buffers in the vertex stage. *(Confirm path + that shader has `#pragma multi_compile_instancing` at impl start.)*

### Component breakdown

```
GrassInteractField (MonoBehaviour)            ← scene authoring + orchestration
 ├─ ref BoingReactorField  (interaction engine, untouched)
 ├─ GrassLODConfig (ScriptableObject)         ← LOD meshes[], distance thresholds[], material
 ├─ ChunkGrid                                 ← builds chunks at Start
 │   └─ GrassChunk[]  { AABB, Matrix4x4[] instances }
 ├─ InstanceBatchPool                         ← THE "object pool": reused Matrix4x4[1023] slabs + MPBs
 └─ GrassRenderer (per-frame Update/LateUpdate)
      1. GeometryUtility.CalculateFrustumPlanes(cam)        once/frame
      2. for each chunk: GeometryUtility.TestPlanesAABB     cull off-screen chunks
      3. visible chunk → distance(cam, chunk.center) picks LOD index
      4. rent pooled slab, copy ≤1023 matrices, bind field MPB (UpdateShaderConstants)
      5. Graphics.RenderMeshInstanced(rp, lodMesh, 0, slab, count)   modern API
      6. return slabs to pool
```

### "Object pool" definition (the reconciliation)
Pool = `InstanceBatchPool` holding reusable `Matrix4x4[1023]` arrays + `MaterialPropertyBlock`s, rented per draw-call each frame and returned — **zero per-frame allocation**. NOT GameObject pooling (wrong tool at 50k on mobile).

### Why frustum cull at chunk granularity
~hundreds of chunk AABB tests/frame vs 50k per-blade tests. Mobile-trivial. 50k / 1023 ≈ 49 worst-case draw calls; typical visible fraction → ~10–25 calls; LOD0/1/2 swap shrinks far-chunk cost further.

## Risks / open items
1. **Shader URP+field compat (HIGH):** Boing's sample field-sampling shaders are typically built-in RP. The user's URP grass shader must (a) declare `multi_compile_instancing`, (b) bind & sample `aBoingFieldParams`/`aBoingFieldCell` buffers in the vertex stage. First implementation task = verify/adapt shader against a 1-chunk smoke test before scaling.
2. **No Burst/Jobs/Collections in manifest** — culling is plain C# (fine at chunk granularity; revisit only if profiler shows CPU cull hotspot).
3. **LOD0/1/2 meshes** must exist or be authored/decimated from the provided grass mesh.
4. **MPB field rebind frequency:** `UpdateShaderConstants` only needs rebinding when `GpuResourceSetId` changes; cache to avoid redundant per-call sets.

## Success criteria
- 50k blades, mobile target framerate held; draw calls scale with *visible* chunks, not total.
- Off-screen chunks issue zero draws (verify via Frame Debugger / `rendering_stats`).
- Far chunks render LOD1/LOD2 (verify by distance).
- Zero per-frame GC alloc in render loop (verify via Profiler).
- Grass still bends under effectors at all LOD levels (interaction intact).
- Boing Kit third-party files unmodified.

## Next step
Run `/t1k:plan` with this report to produce the phased implementation plan (smoke-test single chunk + shader first, then chunking, cull, LOD, pool).
