# Brainstorm — GrassInteract GPU-Driven Indirect Rendering for Mobile

Date: 2026-06-03 · Skill: /t1k-brainstorm · Status: APPROVED → handing to /t1k:plan

## Problem statement

Current GrassInteract renders 10k–50k blades via a **CPU-driven** path: `GrassBendSimulator`
rebuilds every blade's `Matrix4x4` on the Mono main thread each frame (wind + lean-away bend),
chunkless, then submits 1023-capped `Graphics.RenderMeshInstanced` slabs. Soft ceiling ~50k —
main-thread matrix rebuild is the wall. Goal: scale to **100k–250k** blades on mobile by moving
culling + transform + deform to the GPU, while still running on old GLES3.0 devices.

## Locked requirements (from user)

| Axis | Decision |
|---|---|
| Min device tier | **GLES3.0 fallback required** (no compute there) → tiered design with CPU fallback |
| Deform location | **Fully on GPU** (wind + lean-away bend in vertex shader; interactors as StructuredBuffer) |
| Scale target | **100k–250k** blades |
| Migration | **Coexist** — GPU high tier + existing CPU path as low/fallback tier, same interface |
| Chunk frustum cull | **GPU compute pre-pass** (fully GPU-driven, zero CPU per-frame cull) |
| LOD meshes | **3-tier**: LOD0 cross-quad → LOD1 single quad → LOD2 billboard/skip |

## Architecture — tiered, one façade

`GrassInteractField` stays the public component. At `OnEnable`, probe:

```
bool gpuOk = SystemInfo.supportsComputeShaders
          && SystemInfo.supportsIndirectArgumentBuffers
          && SystemInfo.maxComputeBufferInputsVertex > 0;   // VS StructuredBuffer reads
```

- **High tier (gpuOk):** `GrassGpuEngine` — compute cull + `RenderMeshIndirect`. 100k–250k.
- **Low tier (else):** existing `GrassBendSimulator` + `RenderMeshInstanced`, kept verbatim behind
  an `IGrassEngine` seam. ~50k. Already shipped + screenshot-verified — zero rewrite risk.

## High-tier GPU pipeline (fully GPU-driven)

One-time bake, then everything on GPU per frame:

```
Bake (once): blades → ChunkedBladeBuffer (StructuredBuffer<BladeInstance>)
                    + ChunkAABB buffer (one AABB per grid cell)
Frame:
  [Compute A] chunk cull   : per chunk, AABB vs frustum+distance → append visible chunk IDs
                             + write DispatchIndirect args for pass B
  [Compute B] blade cull   : per blade in visible chunks → distance→LOD bucket, per-blade frustum
                             → Append blade index into per-LOD AppendStructuredBuffer
  CopyCount  : per-LOD visible count → RenderMeshIndirect args
  [Draw]     RenderMeshIndirect × 3 LODs : VS reads BladeInstance via visible-index buffer
```

### Chunking
Field split into fixed grid cells (~16 m, ~2–4k blades each). Chunk = contiguous blade index range
+ AABB. Chunks are the coarse-cull unit and LOD-locality unit. Reuses `GrassScatter` placement; baker
just sorts blades into cells and records ranges + AABBs.

### Deform fully on GPU (vertex shader)
- Interactors uploaded each frame as tiny `StructuredBuffer<Interactor>` (≤16, ~32 B each).
- VS reconstructs world transform from compact record, applies `sin` wind by per-blade hash + the
  **same lean-away-about-base** math shipped in the CPU path, sampling interactors. No matrix buffer
  write → minimal bandwidth. Mono main thread freed entirely.

### Compact per-blade record (bandwidth)
```hlsl
struct BladeInstance { float3 posWS; uint packedYawScale; uint hash; }   // 20 B
```
250k × 20 B ≈ 5 MB uploaded once. Visible-index buffers: `uint` per visible blade. Today's
76 B/blade per-frame CPU matrix rebuild is eliminated.

### LOD (3-tier)
Compute B's distance bucketing routes each blade to: LOD0 cross-quad (near), LOD1 single quad (mid),
LOD2 camera-facing billboard or skip (far). Per-LOD AppendBuffer + per-LOD indirect draw.

## Reused vs new

- **Reused:** `GrassScatter` placement, `GrassLayer`/density paint, `GrassPainterWindow`,
  `GrassInteractor` registry, lean-away look, `GrassLODConfig` (+ chunk size, LOD count). Entire low tier.
- **New:** `IGrassEngine` seam, `GrassGpuEngine.cs`, `GrassCull.compute` (2 kernels: chunk + blade),
  indirect shader variant of `GrassInteractInstanced.shader`, `ChunkedBladeBuffer` baker, GPU interactor
  upload.

## Approaches considered

1. **Two-level GPU-driven indirect (CHOSEN)** — chunk compute cull → blade compute cull → indirect
   draw per LOD. Matches all six locked requirements; textbook production GPU-grass shape.
2. Flat compute cull, no chunks — simpler but no coarse early-out; user wants chunks. Rejected.
3. Compute-builds-matrices + RenderMeshInstanced — needs a GPU→CPU count readback (stall) or fixed
   over-draw. Rejected (defeats the indirect win).

## Risks / watch-items (gate early)

- **Device smoke test first:** `RenderMeshIndirect` + VS `StructuredBuffer` reads on a real GLES3.1
  Android unit — some devices report `supportsIndirectArgumentBuffers=true` but fail in practice. This
  is the gate for the whole high tier; if it fails, those devices drop to the CPU tier.
- Append-buffer counter reset + `CopyCount` ordering — classic GPU-driven bug source; isolate in a
  tiny test kernel before the full pipeline.
- DispatchIndirect args from Compute A → Compute B must be sized/zeroed correctly each frame.
- Edit-mode rendering: re-prove the `RenderPipelineManager.beginCameraRendering` discipline (from the
  CPU path memory) for the indirect path — Scene view must render in edit mode.
- Tier-selection must be testable: a debug override to force CPU tier on a capable device for A/B.

## Success criteria

- High tier renders 100k–250k blades, GPU culls to frustum + distance, 3 LODs via indirect draw.
- Wind + lean-away interaction visible and GPU-computed (Mono main thread ~0 for grass).
- Low tier still renders on GLES3.0 (forced-fallback A/B identical look to today).
- Edit-mode Scene view renders both tiers.
- Same `GrassInteractField` public interface; placement/paint/interactor workflows unchanged.

## Next step

Hand to `/t1k:plan`. Suggested phase shape: IGrassEngine seam (wrap existing CPU path) → ChunkedBladeBuffer
baker → Compute A chunk cull → Compute B blade cull + LOD + indirect args → indirect shader + VS GPU deform
→ GPU interactor upload → device smoke test + tier selection → edit-mode render parity → code-review gate.
