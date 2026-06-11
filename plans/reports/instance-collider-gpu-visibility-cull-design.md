# Instance Collider Pooling — GPU-Visibility-Driven Culling (Design)

**Date:** 2026-06-11 · **Status:** approved, ready to plan/implement · **Scope:** `InstancedPropEngine` collider runtime

## Problem

Pressing Play spawns **zero per-instance colliders** for `InstanceScatterLayer`, even though instances render fine.

### Root cause (confirmed)

- Visual render culls against the live render camera: `cullCam = targetCamera ?? Camera.main` (`InstancedPropEngine.cs:307`), where `targetCamera` is passed into `Submit()` each frame.
- The collider culler `InstanceFrustumCuller` is initialised **once at build time with hardcoded `null`** (`InstancedPropEngine.cs:483`), so it can only ever resolve `Camera.main` (`InstanceFrustumCuller.cs:90`).
- If the render camera is **not tagged `MainCamera`** (Cinemachine brain / injected / custom camera) → `Camera.main == null` → `LateUpdate` early-returns (`:91`) → `Acquire` never called → no colliders.

User intent: **collider culling should follow the visual render**, not a disconnected `Camera.main` CPU path.

## Decision

Drive collider acquisition from the **GPU cull pipeline's visible-index buffer** (exact parity with the render), replacing the CPU culler.

| Decision | Choice |
|---|---|
| Approach | Option 2 — shared GPU visibility (`AsyncGPUReadback` of visible-index buffer) |
| Collider set | **LOD0 band only** (`visibleLod0Buf`) — nearest/highest-detail, bounded by `lod0MaxSqrDist`, stays under `PoolCap` |
| Old `InstanceFrustumCuller` | **Removed entirely** — GPU-readback driver is the sole culler; no `Camera.main` dependency anywhere |
| Pool | **Keep custom `InstanceColliderPool` as-is** (keyed-idempotent `Acquire` + active-count cap; `ObjectPool<T>` models neither) |

## Key constraint — index spaces differ (no existing bridge)

- Props bake via `ChunkedInstanceBuffer`, which **counting-sorts instances by grid cell** (`Bake` steps 1–3). The GPU buffer is NOT in authored order.
- `visibleLod0/1/2Buf` append `globalIdx = ChunkRange.start + k` → indices into the **chunk-sorted** buffer (`GrassCull.compute:195,228`).
- The collider pool keys by **authored-record index** `i` (`InstancedPropEngine.cs:496`).
- **No sorted→authored remap table exists.** This bridge must be built.

## Implementation outline

1. **Capture sort permutation at bake.** In `ChunkedInstanceBuffer.Bake`, record `sortedToAuthored[outI] = authoredInputIndex` during the scatter pass; expose read-only. Assert props are 1 instance/record so input index == pool key `i`.
2. **Readback driver** (new; replaces `InstanceFrustumCuller`). Throttled `AsyncGPUReadback.Request` of `visibleLod0Buf` + its count. On completion: map each `globalIdx → sortedToAuthored[globalIdx]` → desired active record set.
3. **Diff vs pool active set.** `Acquire` newly-visible records, `Release` dropped ones. `PoolCap` + one-shot warning unchanged.
4. **Fixes the bug:** visibility now comes from the GPU pipeline's render camera → the `Camera.main` trap is gone.

### Risks / notes
- 1–2 frame latency from `AsyncGPUReadback` (acceptable for collider interaction; first frames before first readback have no colliders — Prewarm still applies).
- `sortedToAuthored` is the new SSOT linking GPU-sorted ↔ authored space; must stay in sync with the bake (assert length == TotalInstances).
- If LOD0-visible ever exceeds `PoolCap`, existing cap warning fires (same as today).
- Verify `scatter.TotalCount == records.Length` (1:1) for props during implementation — the permutation assumes it.

## Files in scope
- `Runtime/InstancedPropEngine.cs` — collider build path, readback driver wiring, remove old culler call.
- `Runtime/ChunkedInstanceBuffer.cs` — capture + expose `sortedToAuthored` permutation.
- `Runtime/InstanceFrustumCuller.cs` — **delete** (replaced).
- New: GPU-readback collider culler (e.g. `InstanceVisibilityColliderDriver.cs`).
- `Tests/Editor/` — permutation correctness + acquire/release diff tests.
