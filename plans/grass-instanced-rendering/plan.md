# Plan — Interactive Grass Instanced Rendering

**Created:** 2026-06-01
**Source design:** `plans/reports/brainstorm-grass-instanced-rendering-20260601.md`
**Project:** GrassInteract (Unity 6, URP 17.3, Mono — no DOTS, no Burst/Jobs/Collections)
**Mode:** standard single-agent · **Cook handoff:** `/t1k:cook plans/grass-instanced-rendering/plan.md`

## Goal

Reusable system under `Assets/GrassInteract/` rendering 10k–50k interactive grass blades on **mobile** via GPU instancing, CPU chunk-AABB frustum culling, discrete LOD0/1/2 mesh swap, and a zero-GC pooled instance-batch system. Bending is delegated to the **existing** Boing Kit `BoingReactorField` (GPU compute spring grid) — **Boing Kit third-party files are never modified**; the new system only references it and binds its field buffers onto a shared `MaterialPropertyBlock` via `UpdateShaderConstants`.

## Locked decisions (from brainstorm)

| # | Decision |
|---|---|
| Backend | Option A — CPU-chunked cull + `Graphics.RenderMeshInstanced` |
| LOD | Discrete mesh swap LOD0/1/2 by camera distance (no density-thin, no billboard) |
| Scope | New reusable `Assets/GrassInteract/`; Boing demo untouched |
| Grass asset | **Build from scratch** — URP instanced grass shader + procedural blade mesh |
| Pool | `InstanceBatchPool` = recycled `Matrix4x4[1023]` slabs + MPBs (zero per-frame GC) |
| Interaction | `BoingReactorField` only; rebind field MPB **only** when `GpuResourceSetId` changes |

## File ownership (all NEW, under `Assets/GrassInteract/`)

```
Assets/GrassInteract/
  Runtime/
    GrassInteractField.cs        # MonoBehaviour orchestrator
    GrassRenderer.cs             # per-frame cull→LOD→draw loop
    ChunkGrid.cs                 # grid build + GrassChunk struct/class
    InstanceBatchPool.cs         # the object pool
    GrassLODConfig.cs            # ScriptableObject
    GrassInteract.asmdef         # references BoingKit asmdef (or none if Boing is non-asmdef)
  Shaders/
    GrassInteractInstanced.shader  # URP + multi_compile_instancing + Boing field sampling
    GrassFieldSampling.hlsl        # field-buffer sample helper (vertex stage)
  Editor/
    GrassBladeMeshBuilder.cs     # builds + saves the blade mesh asset(s)
    GrassInteractField.Editor.cs # optional inspector helpers
  Meshes/  (generated)  Materials/  Samples/
```

> **Pre-check at cook start:** does Boing Kit have an `.asmdef`? If yes, `GrassInteract.asmdef` must reference it. If Boing is global-namespace (no asmdef), `GrassInteract` must also avoid an asmdef (or add an assembly ref accordingly) or it won't see `BoingKit.*`. Resolve before writing any `.cs`.

---

## Phase 0 — Shader + blade mesh + single-chunk smoke test (DE-RISK FIRST)

**Why first:** Risk #1 (shader URP + field-sampling compat) is the whole project's load-bearing unknown. Prove one chunk bends before building any culling/LOD/pool scaffolding.

**Tasks**
1. `GrassBladeMeshBuilder.cs` (Editor) — generate a low-poly blade mesh (LOD0 ≈ 6–8 tris, e.g. 3-quad tapered blade) and save as `.asset`. Parameterize so LOD1/LOD2 (fewer tris) come from the same builder in Phase 3.
2. `GrassFieldSampling.hlsl` — port Boing's field-sampling math (ref: `Assets/Boing Kit/Examples/Boing Field & Sampler/Warped Teapots/Example Custom Shader (per vertex).shader`) into a URP-compatible vertex-stage helper reading `aBoingFieldParams` / `aBoingFieldCell` `StructuredBuffer`s.
3. `GrassInteractInstanced.shader` — URP Unlit/SimpleLit base + `#pragma multi_compile_instancing`, `#pragma instancing_options`, sample field in vertex stage, apply bend offset + wind-free baseline. Must compile under URP 17.3.
4. Smoke harness: a throwaway `MonoBehaviour` (or a `[ContextMenu]` on `GrassInteractField`) that builds **one** `Matrix4x4[]` of ~256 blades, binds the scene `BoingReactorField` buffers via `UpdateShaderConstants(mpb)` (mirror `BushFieldReactorFieldMain.Update`), and issues **one** `Graphics.RenderMeshInstanced` call.

**Verify (gate — do not proceed until all pass)**
- Shader compiles, no URP errors in `read_console`.
- 256 blades render instanced (Frame Debugger shows 1 instanced draw).
- Driving an effector (sphere/car) through them visibly **bends** the blades → field sampling works.
- Confirm `RenderMeshInstanced` (not deprecated `DrawMeshInstanced`) path is used.

**Risk Assessment**
| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| URP can't bind Boing `StructuredBuffer` in vertex stage as authored | 3 | 5 | 15 | Fallback: pass field cells via a `RenderTexture`/`Texture3D` the field writes, sample in VS. Spike both in Phase 0. |
| Boing example shader math is built-in-RP-specific (matrix/space mismatch) | 3 | 4 | 12 | Re-derive bend in world space from cell offset; validate against the working bush demo visually. |
| `multi_compile_instancing` + StructuredBuffer unsupported on min mobile GPU tier | 2 | 5 | 10 | Confirm target min spec supports compute buffers in vertex shaders (GLES3.1+/Vulkan/Metal). Document min spec. |

---

## Phase 1 — Data model: LOD config + chunk grid

**Tasks**
1. `GrassLODConfig.cs` (ScriptableObject): `Mesh[] lodMeshes` (3), `float[] lodDistances` (2 thresholds), `Material material`, `int bladesPerChunkAxis`/density, `float chunkSize`, `Vector2 fieldBounds`, scale range, RNG seed.
2. `ChunkGrid.cs`: build chunks tiling `fieldBounds` by `chunkSize`. Each `GrassChunk` = `{ Bounds aabb; Matrix4x4[] instances; Vector3 center; }`. Deterministic placement (seeded `Random`, mirror bush demo TRS).
3. `GrassInteractField.cs` skeleton: serialized `BoingReactorField field`, `GrassLODConfig config`; `Start()` builds the grid; gizmo draws chunk AABBs.

**Verify**
- Chunk count + total instance count match expected (`fieldBounds/chunkSize`, density) — logged.
- Gizmos show chunk grid covering the field; AABBs tight on blade extents (include max blade height in `aabb`).
- No render yet — data only.

**Risk:** AABB too tight (clips tall/bent blades) L2·I3=6 → expand AABB by max blade height + max bend offset.

---

## Phase 2 — Renderer core: frustum cull + instanced draw (single LOD)

**Tasks**
1. `GrassRenderer.cs`: per-frame (called from `GrassInteractField.LateUpdate`):
   - `GeometryUtility.CalculateFrustumPlanes(cam, _planes)` (reuse cached array — no alloc).
   - For each chunk: `GeometryUtility.TestPlanesAABB(_planes, chunk.aabb)` → skip if false.
   - Visible chunk: split its `instances` into ≤1023 sub-batches, `RenderMeshInstanced` each with the field-bound MPB (LOD0 only this phase).
2. Bind field MPB once/frame (cache; full pool + rebind-gating comes in Phase 4 — here a simple per-frame bind is acceptable, GC measured later).
3. Use `RenderParams` with correct layer/shadow/bounds.

**Verify**
- Off-screen chunks issue **zero** draws — rotate camera, confirm via Frame Debugger / `rendering_stats` draw-call count drops.
- All on-screen blades bend (interaction preserved through the batched path).
- Draw calls ≈ ceil(visible_instances / 1023).

**Risk:** world-space bounds wrong → blades culled by Unity's own per-draw bounds L2·I4=8 → set `RenderParams.worldBounds` to encompassing volume.

---

## Phase 3 — LOD mesh swap by distance

**Tasks**
1. Extend `GrassBladeMeshBuilder` to emit LOD1 (≈ half tris) + LOD2 (≈ quad/cross) meshes; wire into `GrassLODConfig.lodMeshes`.
2. In `GrassRenderer`: per visible chunk, `dist = Vector3.Distance(cam.pos, chunk.center)` → pick LOD index via `lodDistances`; draw that LOD mesh for the chunk's batches.
3. Optional hysteresis band to avoid LOD flicker at thresholds.

**Verify**
- Far chunks demonstrably use LOD1/LOD2 (temporarily tint per-LOD or log; Frame Debugger mesh name).
- No popping artifacts at thresholds (hysteresis works).
- Bending still correct at every LOD (field sampling is per-vertex, LOD-independent).

**Risk:** LOD2 too sparse → visible blade-count drop at boundary L3·I2=6 → tune distances; keep LOD2 silhouette-preserving.

---

## Phase 4 — InstanceBatchPool (zero-GC) + MPB rebind gating

**Tasks**
1. `InstanceBatchPool.cs`: pool of `Matrix4x4[1023]` slabs + `MaterialPropertyBlock`s. `Rent()` / `Return()`; pre-warm to worst-case visible-batch count. No `new` in steady state.
2. Refactor `GrassRenderer` to rent slabs, `Array.Copy` chunk matrices into them, draw, return — replacing any per-frame `new Matrix4x4[]`.
3. MPB field rebind gating: cache `BoingReactorField.GpuResourceSetId`; call `UpdateShaderConstants` **only** when it changes (per `BoingReactorFieldGPUSampler` pattern). Otherwise reuse cached MPB.
4. Audit all per-frame paths for hidden allocs (LINQ, closures, boxing, `foreach` on structs).

**Verify**
- Unity Profiler: **0 B GC Alloc** per frame in the render loop (steady state) — record before/after.
- Field still binds correctly after a `GpuResourceSetId` bump (toggle field cell count to force it).
- Visual output identical to Phase 3.

**Risk:** chunk has >1023 instances and slab copy miscounts L2·I3=6 → unit-test the sub-batch split (1022/1023/1024/2047 boundaries).

---

## Phase 5 — Scale to 50k + full verification pass

**Tasks**
1. Populate field to 50k blades; tune `chunkSize`/density for best cull granularity (smaller chunks = better cull, more iteration; find knee).
2. Build to a real mobile device (or device sim) and profile.
3. Write `Samples/` demo scene wiring car effector + field + `GrassInteractField`.
4. README in `Assets/GrassInteract/` documenting setup + the GPU-indirect future-upgrade note.

**Verify (final gates — all mandatory)**
- 50k blades hold the mobile target framerate on device.
- Off-screen chunks: zero draws (Frame Debugger).
- Far chunks: LOD1/LOD2 confirmed.
- Render loop: zero per-frame GC (Profiler).
- Grass bends under the car at all LOD levels.
- Boing Kit files: `git status` shows zero modifications under `Assets/Boing Kit/`.

**Risk:** mobile fill-rate (overdraw), not draw calls, becomes the bottleneck L3·I4=12 → mitigate with LOD2 alpha-coverage, shorter far blades, reduced far density (defer density-thin to a follow-up if needed).

---

## Timeline

| Phase | Effort | Notes / critical path |
|---|---|---|
| 0 — Shader + mesh + smoke | M (3d) | **Critical path** — gates everything; shader risk lives here |
| 1 — Data model | S (1d) | depends on 0 only for config shape |
| 2 — Renderer core | M (3d) | depends on 0,1 |
| 3 — LOD swap | S (1d) | depends on 2 |
| 4 — Pool + rebind gating | M (2d) | depends on 2 (independent of 3) |
| 5 — Scale + verify | M (2d) | depends on all |
| **Total** | **~12d** | Critical path: 0 → 2 → 3/4 → 5 |

## Cross-cutting constraints
- Unity C# conventions: `this.` member access, `camelCase` private fields (no `_`), `[SerializeField] private`, `#nullable enable` in new files.
- No magic numbers — all tunables on `GrassLODConfig`.
- Never `new` in the per-frame render loop after Phase 4.
- Never edit `Assets/Boing Kit/**`.
- Don't kill/quit/Reimport-All Unity; use `refresh_unity` after script edits, check `read_console` before using new types.

## Cook handoff
```
/t1k:cook plans/grass-instanced-rendering/plan.md
```
Start at Phase 0; do not advance past a phase until its Verify gate passes.
