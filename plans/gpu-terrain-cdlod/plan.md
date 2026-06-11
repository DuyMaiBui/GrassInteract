# Plan: GPU-Driven CDLOD Terrain System

**Created:** 2026-06-11 11:59
**Engine:** Unity 6000.3.13f1, URP 17.3.0, mobile (iOS/Android) primary target
**Source of truth:** `plans/reports/gpu-terrain-cdlod-brainstorm-2026-06-11.md` (all design decisions LOCKED)
**Status:** ready for `/t1k:cook` — start with the Phase 0 + Phase 1 vertical slice

---

## Overview

A GPU-driven terrain renderer that reuses the proven grass spine (bake to GPU buffers →
cull in compute → one indirect draw) and adds **chunked-quadtree CDLOD** LOD with crack-free
vertex morphing, a **custom heightmap source** (R16) decoupled from Unity `TerrainData`,
**multi-tile streaming**, **heightfield proxy colliders**, and a **sculpt + paint editor tool**.

The renderer lives in a NEW `GpuTerrain` module with its own asmdef, decoupled from
`GrassInteract.asmdef`. The only shared seam is the existing `ISurfaceSampler` interface
(`Assets/GrassInteract/Runtime/ISurfaceSampler.cs`): Phase 4 adds a `HeightmapSurfaceSampler`
implementation so grass/rocks ground on the new terrain. Terrain does NOT depend on grass;
grass depends only on the sampler interface.

### Module placement

```
Assets/GpuTerrain/
  GpuTerrain.asmdef                 (rootNamespace GpuTerrain; references: none core, mirror GrassInteract)
  Runtime/                          (≤200 lines/file; this.-prefix; camelCase private; PascalCase public)
  Editor/
  Shaders/
  Tests/Editor/
    GpuTerrain.EditorTests.asmdef   (references GpuTerrain, GpuTerrain.Editor, TestRunner, Unity.Collections)
```

---

## Reuse map (existing grass spine → terrain)

| Existing asset | Path | How terrain reuses it |
|---|---|---|
| Two-pass GPU cull | `Assets/GrassInteract/Shaders/GrassCull.compute` | Port `ChunkCull` frustum+distance AABB kernel to cull CDLOD render nodes (Phase 1). |
| Indirect orchestration | `Runtime/GrassGpuEngine.cs` | Submit discipline template: player-loop submit, non-zero worldBounds, bind buffers to material NOT matProps, RenderParams via Material ctor. |
| Chunked GPU buffer baker | `Runtime/ChunkedBladeBuffer.cs`, `ChunkedInstanceBuffer.cs` | Template for tile height/splat GPU upload + per-node AABB partition + counting-sort + ValidatePartition. |
| LOD distance math | `Runtime/LodCullMath.cs` | CDLOD range-band squared-distance thresholds (extend for N levels). |
| Sampler seam | `Runtime/ISurfaceSampler.cs`, `TerrainSurfaceSampler.cs` | Phase 4 adds `HeightmapSurfaceSampler : ISurfaceSampler` — same `TrySample` contract. |
| Collider pool/driver | `Runtime/InstanceColliderPool.cs`, `InstanceVisibilityColliderDriver.cs` | Pattern reference for Phase 4 streamed near-tile heightfield colliders (proxy chosen, not per-instance). |
| asmdef + tests layout | `GrassInteract.asmdef`, `Tests/Editor/*.asmdef` | Mirror module/asmdef/test layout exactly. |

---

## Phases

| Phase | Name | Scope / files owned | Effort |
|---|---|---|---|
| **0** | Heightmap data model | Tile asset format (R16 height + splat), world tile-grid layout, GPU upload path | **M** |
| **1** | CDLOD GPU-indirect renderer (single tile) | Quadtree select → frustum cull (port GrassCull) → indirect draw of one shared grid patch mesh, vertex morph + height sample. **THE DRAW-CALL WIN DEMO.** | **L** |
| **2** | Terrain shading | 4-layer splat blend (texture array), heightmap normals, URP integration | **M** |
| **3** | Multi-tile streaming | Load/unload + GPU residency ring, async disk streaming, far-tile eviction | **L** |
| **4** | Collider + scatter bridge | Heightfield proxy colliders (near tiles) + `HeightmapSurfaceSampler : ISurfaceSampler`. **LOAD-BEARING: grass floats without it.** | **M** |
| **5** | Sculpt + paint editor tool | GPU brush height sculpt + splat paint (RT → R16 writeback), per-tile snapshot undo, editor window | **L** |

**Effort legend:** S ≈ 0.5–1 day, M ≈ 2–3 days, L ≈ 4–6 days (single implementer, excludes on-device profiling waits).

---

## ⭐ First milestone — Phase 0 + Phase 1 vertical slice

**Build Phase 0 + Phase 1 FIRST and validate the draw-call / perf win on a real mid-tier mobile
device BEFORE committing to streaming (Phase 3) and sculpt (Phase 5) scope.** Phase 1 proves the
thesis: a single custom-heightmap tile rendered crack-free at a handful of indirect draws vs the
built-in per-patch draw count. If the VTF/morph cost or draw-call win does not materialise on
device, the streaming + sculpt investment is reconsidered before it is paid.

**Milestone exit gate:**
1. One tile renders from a custom R16 heightmap via `RenderMeshInstancedIndirect` (Phase 1).
2. Terrain surface draw calls are a small constant (≤ a handful), independent of coverage —
   measured against built-in Terrain per-patch count at equal coverage (`rendering_stats` / Frame Debugger).
3. CDLOD LOD transitions are crack-free and pop-free (vertex morph verified visually + by morph-math unit test).
4. Frame time within budget on the target mid-tier device (user-run on-device smoke).

Only after this gate passes do Phases 2 → 5 proceed.

---

## Cross-phase dependency graph

```
Phase 0  (height data model + GPU upload)
   │
   ├──────────────► Phase 1  (CDLOD renderer — consumes Phase 0 GPU height texture + tile grid)   ◄── MILESTONE GATE
   │                   │
   │                   ├──────► Phase 2  (shading — consumes Phase 1 patch VS + Phase 0 splat data)
   │                   │
   │                   └──────► Phase 3  (streaming — wraps Phase 0 tile assets + Phase 1 per-tile renderer in a residency ring)
   │                                          │
   ├──────────────► Phase 4  (HeightmapSurfaceSampler reads Phase 0 height data; proxy colliders stream with Phase 3 near tiles)
   │                                          │
   └──────────────► Phase 5  (sculpt writes back into Phase 0 R16 format; live re-upload via Phase 0 path; multi-tile via Phase 3)
```

**Hard dependencies:**
- **Phase 1 → Phase 0:** renderer consumes Phase 0's GPU height-texture upload + tile-grid layout.
- **Phase 2 → Phase 1:** shading extends the Phase 1 patch vertex/fragment shader.
- **Phase 3 → Phase 0 + Phase 1:** streaming manages Phase 0 tile assets and instantiates Phase 1 per-tile renderers.
- **Phase 4 sampler → Phase 0:** `HeightmapSurfaceSampler` reads Phase 0 height data (CPU-readable copy).
- **Phase 4 colliders → Phase 3:** near-tile heightfield proxies stream with the Phase 3 residency ring (Phase 4 colliders can ship single-tile against Phase 0 first, then integrate Phase 3).
- **Phase 5 → Phase 0:** sculpt round-trips into Phase 0's R16 tile format and re-uploads via the Phase 0 GPU path.
- **Phase 5 multi-tile → Phase 3:** cross-tile brush strokes need the Phase 3 residency set.

**Parallel-safe after the milestone gate:** Phase 2 (shading) and Phase 4 (sampler half) touch
disjoint files and can proceed concurrently once Phase 1 lands. Phase 3 and Phase 5 are the
heavyweight serial tail.

---

## Mobile-specific risk register (cross-cutting — detailed per phase)

| Risk | Where | Mitigation |
|---|---|---|
| VTF (vertex-texture-fetch) stalls on low-end GPUs | Phase 1 | Conservative patch res 16–32² quads; pre-baked per-chunk vertex-Y FALLBACK toggle (primary=VTF, fallback=baked vertex Y). |
| Many tiles → GPU/CPU memory blowup | Phase 3 | Residency ring (~5×5) of resident height/splat textures; async stream; far-tile eviction. |
| MeshCollider / heightfield cooking cost spikes frame | Phase 4 | TerrainCollider heightfield proxy (cheaper than MeshCollider cook); near-tiles only; amortise cook across frames (driver pattern). |
| RenderGraph silently drops the indirect draw | Phase 1, 3 | Carry over hardened discipline: submit from player loop (NOT beginCameraRendering); non-zero worldBounds; never set rp.matProps; RenderParams via Material ctor (renderingLayerMask default). |
| CDLOD cracks / popping between LOD bands | Phase 1 | Vertex morph (XZ blend to coarser grid by distance) — the reason CDLOD was chosen. |
| Sculpt undo memory growth | Phase 5 | Per-tile snapshot diffs, bounded undo stack. |
| 4-layer splat array exceeds mobile sampler budget | Phase 2 | Cap at 4 layers, single texture-array sample, weight-normalize in fragment. |

---

## Success metrics (from brainstorm)

- **Phase 1:** terrain surface draw calls < a handful vs built-in per-patch count at equal coverage; crack-free LOD transitions verified.
- Frame time on target mid-tier mobile within budget at target tile count.
- **Phase 4:** existing grass/rock demo grounds correctly on custom terrain (no floating/sinking).
- **Phase 5:** sculpt + paint round-trips to R16 with undo, no editor stalls.

---

## Conventions (enforced every phase)

- `this.`-prefix mandatory on member access; private fields `camelCase` (no underscore); public `PascalCase`; constants `UPPER_SNAKE_CASE`.
- One asmdef per module; `≤200 lines/file` guidance; one responsibility per file.
- `#nullable enable` in new files; guard clauses over nesting.
- No hardcoded magic numbers — tile res, ring size, LOD bands, layer cap are named constants / serialized config.
- EditMode unit tests mirror `Assets/GrassInteract/Tests/Editor/` (NUnit, pure-math + bake-validation tests; no Play-mode dependency where avoidable).

## Phase files

- [phase-0.md](phase-0.md) — Heightmap data model
- [phase-1.md](phase-1.md) — CDLOD GPU-indirect renderer (single tile) ⭐ milestone
- [phase-2.md](phase-2.md) — Terrain shading
- [phase-3.md](phase-3.md) — Multi-tile streaming
- [phase-4.md](phase-4.md) — Collider + scatter bridge
- [phase-5.md](phase-5.md) — Sculpt + paint editor tool
</content>
</invoke>
