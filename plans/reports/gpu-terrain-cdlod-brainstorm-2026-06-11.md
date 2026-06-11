# GPU-Driven CDLOD Terrain — Brainstorm Report

**Date:** 2026-06-11
**Author:** brainstorm session (t1k-brainstorm)
**Status:** Design approved-pending → ready for `/t1k:plan`

---

## Problem statement

Unity built-in Terrain renders the surface as CPU-culled patches with fixed LOD and a
`TerrainData`-bound collider. Across **many terrain tiles** this multiplies draw calls and
per-patch CPU culling cost. We want a **GPU-driven terrain renderer** using the same proven
spine as the existing grass system (bake to GPU buffers → cull in compute → one indirect draw),
with **chunked-quadtree (CDLOD) LOD**, GPU frustum culling, a **custom heightmap source**
(decoupled from Unity Terrain), **multi-tile streaming**, and a **sculpt + paint editor tool**.

## Confirmed requirements (from clarifying Qs)

| Decision | Choice |
|---|---|
| Optimization target | Terrain **surface mesh** + **many terrain tiles** |
| Heightmap source | **Custom heightmap texture** (R16/EXR), decoupled from Unity `TerrainData` |
| Scope | **Full sculpt + render tool** (replaces Unity terrain editor) |
| LOD technique | **Chunked quadtree, CDLOD-style** (vertex morph, crack-free) |
| Target platform | **Mobile (iOS/Android)** — primary constraint |
| Colliders | **Heightfield collider proxy** baked from heightmap |
| World scale | **Large open world (many tiles)** — streaming is core, not optional |

## Existing assets to reuse (from scout)

| Asset | Path | Reuse |
|---|---|---|
| Two-pass GPU cull | `Assets/GrassInteract/Shaders/GrassCull.compute` | ChunkCull frustum/distance kernel → cull CDLOD nodes |
| Chunked GPU buffer bake | `Runtime/ChunkedBladeBuffer.cs`, `ChunkedInstanceBuffer.cs` | Template for tile/node GPU buffers |
| Indirect draw orchestration | `Runtime/GrassGpuEngine.cs` | `RenderMeshIndirect` + RenderGraph submit discipline |
| LOD distance math | `Runtime/LodCullMath.cs` | CDLOD range bands |
| Async-readback collider driver | `Runtime/InstanceVisibilityColliderDriver.cs` + `InstanceColliderPool.cs` | Pattern reference (proxy collider chosen instead) |
| Surface sampling | `Runtime/TerrainSurfaceSampler.cs` (`ISurfaceSampler`) | Seam: add `HeightmapSurfaceSampler` so grass/rocks ground on new terrain |

**Render discipline already hardened (must carry over):** submit from player loop (LateUpdate /
EditorApplication.update), NOT `beginCameraRendering`; non-zero `worldBounds`; never set
`rp.matProps` (bind buffers directly to materials). URP 17.3.0, Unity 6000.3.13f1.

## Recommended approach — A: CPU quadtree selection + GPU-indirect render

```
Custom heightmap tiles (R16) + splatmaps
        │  GPU residency: atlas/ring of resident tiles around camera (streamed async)
        ▼
CDLOD quadtree (per tile, N levels)
        │  CPU selection traversal (few-hundred nodes, deterministic, debuggable)
        ▼
RenderNode[] { worldOffset, scale, lod, tileIdx, morphRange }
        │  reuse GrassCull.compute chunk-AABB frustum cull
        ▼
Visible RenderNode buffer ──► Graphics.RenderMeshInstancedIndirect
        │                         ONE shared grid patch mesh (mobile: 16–32² quads)
        ▼
Vertex shader: CDLOD morph (XZ blend to coarser grid by distance) → sample height (VTF) → normal
```

**Why A:** CDLOD node selection is a cheap CPU traversal; render is one indirect draw per
tile-material. Mirrors the grass bake-then-indirect model → maximum reuse. CDLOD vertex morph
gives crack-free, pop-free LOD (matches built-in quality; naïve chunked-LOD loses this).

**Alternatives considered:**
- **B — fully GPU-driven quadtree (compute traversal):** max scale for giant worlds, but
  persistent-compute traversal is hard to debug. Reserved as a later upgrade if profiling demands.
- **C — geometry clipmap:** ruled out (CDLOD chosen); clipmap rings fight tile boundaries and
  sculpt edits.

## Mobile-specific design adjustments

- **Heightmap-in-vertex-shader (VTF):** supported on GLES3.1/Vulkan/Metal; keep as primary with
  **conservative chunk resolution** (16–32² quads) and **a pre-baked per-chunk vertex-Y fallback
  toggle** for very low-end devices.
- **Residency:** only a ring (e.g. ~5×5) of resident tiles' height/splat textures on GPU at once;
  async stream from disk, evict far tiles. Atlas or texture-array residency.
- **Splat layers:** cap at 4 (mobile), texture-array sampling.
- **Colliders:** heightfield proxy generated only for tiles near the player; streamed with tiles.
- **Draw batching:** aim for a handful of indirect draws total (per resident material).

## Subsystem decomposition + build order

| Phase | Subsystem | Deliverable | Win |
|---|---|---|---|
| **0** | Heightmap data model | Tile asset format (R16 height + splat), world tile-grid layout, GPU upload | Foundation |
| **1** | CDLOD GPU-indirect renderer (single tile) | Quadtree select → frustum cull → indirect draw, vertex morph + height sample | **Draw-call win — visible demo** |
| **2** | Terrain shading | 4-layer splat blend, heightmap normals, URP integration | Looks like real terrain |
| **3** | Multi-tile streaming | Load/unload + GPU residency ring around camera | Large worlds, bounded memory |
| **4** | Collider + scatter bridge | Heightfield proxy colliders **+ `HeightmapSurfaceSampler`** for grass/rock grounding | Gameplay-ready; **grass keeps working** |
| **5** | Sculpt + paint editor tool | GPU brush height sculpt + splat paint (RT → R16 writeback), undo, Scatter-Studio-style window | Replaces Unity terrain editor |

Phase 1 proves the thesis. **Phase 4 is a hard dependency for existing systems:** once terrain is
custom-heightmap, `TerrainSurfaceSampler` (Unity `TerrainData`) no longer grounds scatter — a
parallel `HeightmapSurfaceSampler` must ship or grass/rocks float.

## Architecture / placement

New **`GpuTerrain` module** with its own asmdef, **decoupled from `GrassInteract.asmdef`** per the
library-decoupling rule. Shared seam = `ISurfaceSampler` (already exists). Terrain renderer does
not depend on grass; grass depends only on the sampler interface, not the terrain renderer.

## Risks

| Risk | Mitigation |
|---|---|
| VTF stalls on low-end mobile | Conservative chunk res + pre-baked per-chunk vertex-Y fallback toggle |
| Many tiles → memory blowup | Residency ring + async streaming + far-tile eviction (Phase 3 core) |
| Scatter floats after custom terrain | `HeightmapSurfaceSampler` is in-scope Phase 4, flagged as load-bearing |
| RenderGraph drops draws | Carry over hardened submit discipline (player-loop submit, non-zero bounds, no matProps) |
| CDLOD cracks/popping | Vertex morph between LOD bands (the reason CDLOD was chosen) |
| Sculpt undo memory | Per-tile snapshot diffs, bounded undo stack |

## Success metrics

- Phase 1: terrain surface draw calls **< handful** vs built-in per-patch count at equal coverage;
  crack-free LOD transitions verified.
- Frame time on target mid-tier mobile device within budget at target tile count.
- Phase 4: existing grass/rock demo grounds correctly on custom terrain (no floating/sinking).
- Phase 5: sculpt + paint round-trips to R16 with undo, no editor stalls.

## Next steps

1. `/t1k:plan` to phase this into an implementation plan (Phases 0→5, file ownership, tests).
2. Recommend planning **Phase 0 + 1 first** as a vertical slice to validate the draw-call win
   before committing streaming + sculpt scope.
