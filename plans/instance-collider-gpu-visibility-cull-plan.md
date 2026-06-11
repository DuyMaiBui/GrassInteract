# Plan — GPU-Visibility-Driven Instance Collider Culling

**Created:** 2026-06-11 · **Mode:** standard (single-module, Unity prop engine runtime)
**Design:** `plans/reports/instance-collider-gpu-visibility-cull-design.md` (approved)
**Cook handoff:** `/t1k:cook plans/instance-collider-gpu-visibility-cull-plan.md`

## Goal

Fix **zero per-instance colliders on Play** for `InstanceScatterLayer`, by driving collider acquisition from the GPU cull pipeline's LOD0 visible-index buffer (exact parity with the visual render) instead of a `Camera.main`-bound CPU culler.

**Success criteria (verifiable):**
1. Entering Play with a render camera **not tagged `MainCamera`** spawns colliders for on-screen near (LOD0) instances. (Reproduces the original bug; must pass after fix.)
2. Colliders acquire/release as the camera moves, tracking the LOD0 visible set within `PoolCap`.
3. `InstanceFrustumCuller.cs` is deleted; no `Camera.main` reference remains in the collider path.
4. All EditMode tests green, including new permutation + diff tests.

## Locked decisions (from brainstorm — do NOT re-litigate)

| Decision | Value |
|---|---|
| Approach | GPU-visibility readback (`AsyncGPUReadback` of visible-index buffer) |
| Collider set | **LOD0 band only** (`visibleLod0Buf`) |
| Old CPU culler | **Deleted entirely** — no fallback CPU path |
| Pool | Keep custom `InstanceColliderPool` unchanged |
| Driver shape | Plain engine-owned class ticked from `Submit` (NOT a MonoBehaviour) — engine owns the buffers + pool + permutation |

## Architecture

```
InstancedPropEngine.Build:
  ChunkedInstanceBuffer.Bake  ──►  exposes sortedToAuthored[]   (NEW: Phase 1)
  BuildColliderRuntime        ──►  builds authored-indexed record arrays + pool
                                   constructs InstanceVisibilityColliderDriver  (NEW: Phase 2)

InstancedPropEngine.Submit (per frame):
  RecordFrameCommands → ExecuteCommandBuffer   (GPU cull fills visibleLod0Buf + count)
  driver.Tick(visibleLod0Buf, lod0Count)       ──►  AsyncGPUReadback (throttled)
       on complete: globalIdx → sortedToAuthored[globalIdx] → authored visible set
                    diff vs pool active → Acquire new / Release dropped   (NEW: Phase 2)
```

---

## Phase 1 — Index bridge: capture sort permutation

**Owns:** `Runtime/ChunkedInstanceBuffer.cs`, `Tests/Editor/ChunkedInstanceBufferTests.cs` (new or extend)

**Tasks:**
1. In `Bake`, during the scatter pass (`outI = writeCursor[cell]++`), record `sortedToAuthored[outI] = authoredInputIndex`, where `authoredInputIndex` is the running index over the scatter input (0..TotalInstances-1).
2. Store as `int[] sortedToAuthored`; expose `public IReadOnlyList<int> SortedToAuthored` (or `int[] SortedToAuthoredView`). Null before bake; reset on re-bake/dispose.
3. Assert `sortedToAuthored.Length == TotalInstances` at end of bake (throw on mismatch — SSOT invariant, errors-over-silent-fallbacks).
4. **Verify the 1:1 prop invariant:** confirm `scatter.TotalCount == authored records.Length` for props so `authoredInputIndex` == pool key `i`. If scatter can emit ≠1 instance per record, document and adjust the mapping (capture authored record id, not input ordinal).

**Verify:** EditMode test bakes a known small instance set across ≥2 chunks, asserts `sortedToAuthored` is a valid permutation of `[0,N)` and that `Instances[k]` corresponds to authored record `sortedToAuthored[k]` (position match).

### Risk Assessment
| Risk | Likelihood | Impact | Score | Mitigation |
|------|-----------|--------|-------|------------|
| Scatter emits ≠1 instance/record (mapping breaks) | 2 | 5 | 10 | Task 4 verifies the invariant first; assert in code |
| Permutation drifts from bake on future edits | 2 | 4 | 8 | Length assert + permutation test guards regressions |

---

## Phase 2 — GPU-readback collider driver

**Owns:** `Runtime/InstanceVisibilityColliderDriver.cs` (new), `Tests/Editor/InstanceVisibilityColliderDriverTests.cs` (new)
**Depends:** Phase 1 (`sortedToAuthored`)

**Tasks:**
1. New plain class `InstanceVisibilityColliderDriver`. Constructor/Init takes: `InstanceColliderPool`, `int[] sortedToAuthored`, the authored-indexed record arrays (positions/rotations/scales/meshes/convex/wantsCollider/materials — same arrays `InstanceFrustumCuller.SetRecords` used), `PoolCap`.
2. `Tick(GraphicsBuffer visibleLod0Buf, int lod0Count)` called per frame from `Submit`:
   - Throttle: skip if a readback is in-flight, or interval not elapsed.
   - `AsyncGPUReadback.Request(visibleLod0Buf, lod0Count*4 bytes, ...)` (+ obtain `lod0Count`; see Task 3).
   - On completion: for each `globalIdx` in the readback, `authoredIdx = sortedToAuthored[globalIdx]`; build the desired active set (a reused `HashSet<int>`/scratch — no per-frame alloc).
   - Diff vs pool's current active keys: `Acquire(authoredIdx, …)` for newly-visible (respect `PoolCap`, existing one-shot warning); `Release(idx)` for keys no longer visible.
3. **Source the LOD0 count:** `visibleLod0Buf` is an Append buffer; its counter is copied to `argsLod0Buf` at `ARGS_INSTANCE_COUNT_OFFSET` (`InstancedPropEngine.cs:560`). Either read that back, or `CopyCounterValue` into a dedicated 1-uint count buffer and read it back alongside. Pick the lower-latency single-readback option; document it.
4. **Graceful degradation:** if `!SystemInfo.supportsAsyncGPUReadback`, log ONE clear warning and skip collider culling (warn+skip per errors-over-silent-fallbacks). Do not fall back to a CPU camera path (deleted by decision).
5. No per-frame GC: reuse scratch collections + `NativeArray`/managed readback buffers.

**Verify:** EditMode test with a synthetic `sortedToAuthored` + fake visible-index list (bypassing GPU) drives the diff logic: assert correct Acquire on newly-visible, Release on dropped, idempotent Acquire on still-visible, and PoolCap cap behavior. (Isolate the diff/map logic from the GPU readback so it is unit-testable headless.)

### Risk Assessment
| Risk | Likelihood | Impact | Score | Mitigation |
|------|-----------|--------|-------|------------|
| LOD0-visible count exceeds PoolCap | 2 | 3 | 6 | Existing cap + one-shot warning; LOD0 is distance-bounded by lod0MaxSqrDist |
| Per-frame readback alloc / GC spikes | 2 | 3 | 6 | Reuse scratch buffers; throttle; single in-flight request |
| Readback returns stale/partial on resize | 2 | 4 | 8 | Guard on `lod0Count`; discard readback if buffers rebuilt since request |
| Headless test can't run GPU readback | 3 | 2 | 6 | Unit-test the map/diff core separately from the GPU request |

---

## Phase 3 — Integration & cleanup

**Owns:** `Runtime/InstancedPropEngine.cs`, `Runtime/InstanceFrustumCuller.cs` (DELETE), `Runtime/AssemblyInfo.cs` (if InternalsVisibleTo references the culler)
**Depends:** Phase 1, Phase 2

**Tasks:**
1. `BuildColliderRuntime`: after Bake, grab `instanceBuffer.SortedToAuthored`; build the authored-indexed record arrays (reuse the existing loop at `:448-478`); construct the driver. Remove the `InstanceFrustumCuller` branch (`:480-488`). Keep `Prewarm`.
2. `Submit`: after `Graphics.ExecuteCommandBuffer(this.cullCmd)` (`:329`), call `this.colliderDriver?.Tick(this.visibleLod0Buf, …)`. Ensure it runs only in Play + when colliders exist.
3. `Dispose`: dispose the driver (cancel/await in-flight readback, release count buffer); remove `colliderCuller` field. Keep `colliderPool.Dispose()` + `SafeDestroy(colliderRoot)`.
4. **Pre-delete grep** for `InstanceFrustumCuller` across Runtime + Tests + Editor; remove all references (incl. `AssemblyInfo.cs` InternalsVisibleTo comment and any test). Then delete `InstanceFrustumCuller.cs` (+ `.meta`).
5. Commit `.meta` files for the new driver + any deletions.

**Verify (HARD gate — no side effects):**
1. Compile clean (`read_console` — zero errors).
2. Full EditMode suite green (`run_tests`), including Phase 1 + Phase 2 tests.
3. **In-editor Play check:** load the demo scene, ensure the render camera is NOT tagged `MainCamera`, enter Play, confirm near-instance colliders spawn (raycast / physics probe or visible MeshCollider gizmos) — reproduces & closes the original bug.
4. No `Camera.main` reference remains in the collider path (grep).
5. `git status` clean after commit; `.meta` files included.

### Risk Assessment
| Risk | Likelihood | Impact | Score | Mitigation |
|------|-----------|--------|-------|------------|
| Dangling InstanceFrustumCuller refs break compile | 2 | 3 | 6 | Pre-delete grep (task 4) before deletion |
| In-flight readback survives Dispose → callback on freed buffer | 3 | 4 | 12 | Guard callback with a disposed-generation token; ignore stale completions |
| Missing `.meta` commit (Unity asset desync) | 2 | 3 | 6 | Explicit task 5 + git status verify |

---

## Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Index bridge | S (~1d) | Mirrors ChunkedBladeBuffer counting sort; mostly capture + test |
| Phase 2: Readback driver | M (~2-3d) | AsyncGPUReadback lifecycle + diff logic + isolating GPU for tests |
| Phase 3: Integration & cleanup | S (~1d) | Wire-in, delete culler, Play verify |
| **Total** | **~4-5d** | Critical path: P1 → P2 → P3 (strictly sequential; P2 needs P1's permutation, P3 needs both) |

## Cross-cutting notes
- **SSOT:** `sortedToAuthored` is the single bridge between GPU-sorted and authored index space — every map MUST go through it; never assume identity.
- **Latency:** 1–2 frame collider lag is accepted; first frames before first readback have no colliders (Prewarm still applies).
- **No `Camera.main` anywhere** in the collider path after this plan.
- **Pre-existing note:** `colliderFollowsTilt` warning (`:167-170`) is out of scope — leave untouched.

---

**Cook handoff:** `/t1k:cook plans/instance-collider-gpu-visibility-cull-plan.md`
