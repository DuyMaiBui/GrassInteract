# Phase 1 — CDLOD GPU-Indirect Renderer (single tile) ⭐ MILESTONE

**Effort:** L · **Blocks:** Phases 2, 3 · **Blocked by:** Phase 0 (GPU height upload + tile grid)

## Goal

Render ONE custom-heightmap tile via `Graphics.RenderMeshInstancedIndirect`: a CPU CDLOD quadtree
selects render nodes, the ported `ChunkCull` compute kernel frustum+distance-culls them, and one
shared grid patch mesh is instanced once per surviving node. The patch vertex shader applies CDLOD
morph (XZ blend toward the coarser grid by distance) then samples height (VTF primary / pre-baked
vertex-Y fallback). **This is the draw-call win demo and the milestone gate** — it must prove a
small constant draw count and crack-free LOD on a real mobile device before Phases 3/5 proceed.

## Feasibility

- **Reuse check:** `ChunkCull` kernel ported from `GrassCull.compute` (frustum positive-vertex AABB
  test + nearest-point distance reject — reused verbatim, node AABBs replace chunk AABBs). Submit
  orchestration cloned from `GrassGpuEngine.Submit` (the hardened RenderGraph discipline). LOD band
  thresholds from `LodCullMath`. The CDLOD quadtree traversal + morph VS are NEW.
- **Complexity:** complex — the highest-risk, highest-value phase. VTF cost, morph correctness, and
  RenderGraph draw-drop pitfalls all converge here.

## File ownership (new files)

```
Assets/GpuTerrain/
  Runtime/
    CdlodQuadtree.cs                (CPU quadtree build + per-frame node selection by camera distance) ≤200
    CdlodNode.cs                    (blittable RenderNode GPU struct: worldOffset, scale, lod, morphRange, tileIdx) ≤120
    TerrainPatchMesh.cs             (builds the ONE shared NxN grid patch mesh; res = named const 16–32) ≤150
    TerrainNodeBuffer.cs            (bakes selected nodes + per-node AABB into GPU buffers; mirrors ChunkedBladeBuffer) ≤200
    GpuTerrainEngine.cs            (Build/Step/Submit/Dispose; indirect draw orchestration; SSOT submit discipline) ≤200
    GpuTerrainRenderer.cs           (MonoBehaviour [ExecuteAlways]: owns engine, submits from player loop) ≤200
  Shaders/
    TerrainNodeCull.compute         (ported ChunkCull: frustum + distance cull of CDLOD nodes → visible append) ≤180
    TerrainPatch.shader             (URP patch VS: CDLOD morph + VTF height sample + simple lit OUT; Phase 2 extends) ≤200
    TerrainVtf.hlsl                 (shared VTF height-sample + morph helpers; SSOT with TerrainHeightSampleCpu formula) ≤150
  Tests/Editor/
    CdlodQuadtreeTests.cs           (node selection: correct LOD band per distance; child/parent coverage; no gaps)
    CdlodMorphMathTests.cs          (morph blend factor monotonic + reaches 0/1 at band edges — crack-free proof)
    TerrainNodeBufferTests.cs       (node AABB partition validates; ValidatePartition-style coverage check)
```

## Tasks

1. **`TerrainPatchMesh`** — build the single shared `(res+1)²` grid patch mesh (unit XZ in [0,1],
   Y=0). `res` is a named const (e.g. `PATCH_RES = 16`, mobile-conservative, tunable 16–32).
   - *Verify:* `mesh.GetIndexCount(0) > 0` (the `GrassGpuEngine.InitLodArgsFromMeshes` 0-index pitfall); vertex count == (res+1)².
2. **`CdlodNode`** — blittable struct `{ float3 worldOffset; float scale; uint lod; float2 morphRange; uint tileIdx; }`.
   Document exact byte layout (mirror the `BladeInstance`/HLSL parity discipline). Keep float/uint only.
   - *Verify:* struct stride matches the HLSL `RenderNode` declaration in `TerrainNodeCull.compute` (size unit test).
3. **`CdlodQuadtree`** — build the per-tile quadtree (N levels from tile size + min patch); per-frame
   `Select(cameraPos)` traversal that picks node LOD by squared distance (use `LodCullMath.Thresholds`
   extended for N bands) and emits `CdlodNode[]` with morph ranges. Deterministic, CPU-side, debuggable.
   - *Verify:* `CdlodQuadtreeTests` — selection covers the tile with no gaps/overlaps; near camera → finer LOD; far → coarser; node count bounded.
4. **`TerrainNodeBuffer`** — upload selected `CdlodNode[]` + per-node AABB (min/max from worldOffset,
   scale, and the node's height min/max from Phase 0 height data) into GPU buffers. Mirror
   `ChunkedBladeBuffer` upload + `ValidatePartition` (node AABB union covers selected region).
   - *Verify:* `TerrainNodeBufferTests` — AABB union covers the tile XZ; empty-node sentinel (min>max) respected.
5. **`TerrainNodeCull.compute`** — port `ChunkCull` from `GrassCull.compute`: same frustum
   positive-vertex test + nearest-point distance reject; input node AABB buffer, output visible-node
   append buffer + CopyCount into indirect draw args. (Single LOD-bucket-free pass — CDLOD LOD is
   already chosen on CPU; one args buffer, instanceCount = visible node count.)
   - *Verify:* dispatch on a known node set in a probe; visible count matches CPU frustum reference for an axis-aligned camera.
6. **`TerrainVtf.hlsl`** — shared height-sample (VTF `tex2Dlod` on the Phase 0 height texture) + CDLOD
   morph helper. **SSOT:** the sample/decode math must equal `TerrainHeightSampleCpu` (Phase 0) so
   grass grounding (Phase 4) agrees with the rendered surface. Provide a `#define` toggle
   `TERRAIN_VTF_FALLBACK` selecting pre-baked vertex-Y (Phase 1 ships the toggle; baked path is a stub
   reading a per-vertex Y attribute, fully realized when low-end profiling demands it).
   - *Verify:* CPU reference vs shader sample agree on a ramp tile (host-side replicate the HLSL math in a unit test).
7. **`TerrainPatch.shader`** — URP patch: VS applies morph (blend XZ toward coarser grid by
   `morphRange` + camera distance) → samples height via `TerrainVtf.hlsl` → outputs world pos +
   simple lit. Bind the visible-node buffer + height texture as MATERIAL properties (NOT MPB — the
   `GrassGpuEngine` lesson: MPB silently dropped under URP RenderGraph). Phase 2 replaces the lit body.
   - *Verify:* renders a recognizable heightfield in editor; toggling morph off shows cracks, on shows none.
8. **`GpuTerrainEngine`** — `Build` (patch mesh, node/cull/args buffers, material clone, bind buffers),
   `Step` (none / time accumulator), `Submit` (build frustum planes non-alloc → record+execute cull
   CommandBuffer → `RenderMeshIndirect` once). **Carry the SSOT submit discipline verbatim:**
   non-zero `worldBounds`, RenderParams via `Material` ctor (renderingLayerMask default), never set
   `rp.matProps`, rebind buffers each Submit (domain-reload guard), `Dispose` releases every buffer.
   - *Verify:* a `SelfTest` (mirror `GrassGpuEngine.SelfTest`) records cull + `RenderMeshIndirect` without throwing.
9. **`GpuTerrainRenderer`** — `[ExecuteAlways]` MonoBehaviour: owns the engine, submits from the
   **player loop** (LateUpdate in play, `EditorApplication.update`/per-camera `beginCameraRendering`
   discipline in edit) — NOT from `beginCameraRendering` as the all-cameras submit. Mirror the grass
   renderer's camera scoping (null in play, firing camera in edit) to avoid N× Scene+Game overdraw.
   - *Verify:* tile renders in both Game and Scene views with no double-submit; `rendering_stats` shows the expected small draw count.

## Risk Assessment

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| RenderGraph silently drops the indirect draw (zero bounds / matProps / object-init RenderParams) | 4 | 5 | 20 | Copy the exact `GrassGpuEngine` discipline: non-zero worldBounds, RenderParams via Material ctor, bind buffers to material not MPB, submit from player loop. SelfTest gate before claiming render. |
| VTF height fetch stalls vertex stage on low-end mobile | 4 | 4 | 16 | Conservative PATCH_RES (16); `TERRAIN_VTF_FALLBACK` pre-baked vertex-Y toggle shipped in this phase; on-device profile is the milestone gate. |
| CDLOD morph wrong → visible cracks/popping at band edges | 3 | 4 | 12 | `CdlodMorphMathTests` proves blend factor reaches exactly 0/1 at band boundaries; shared `morphRange` from quadtree; skirt convention from Phase 0. |
| Node AABB Y-extent wrong → nodes wrongly culled (terrain holes) | 3 | 4 | 12 | Per-node min/max Y from Phase 0 height data (not a flat AABB); ValidatePartition union-coverage test; conservative Y inflation like the blade `bladeCullMargin`. |
| Draw-call win does not materialise (milestone fails) | 2 | 5 | 10 | This IS the milestone gate — measured on device BEFORE Phase 3/5 spend; fallback is to reconsider scope, not push forward blind. |
| Shared-args race / N× overdraw across Scene+Game views | 2 | 3 | 6 | Camera-scoped RenderParams (grass pattern): null in play, firing camera in edit-mode per-camera path. |

**Score ≥ 15:** RenderGraph draw-drop (20) and VTF stall (16). Both have shipped-in-this-phase
mitigations (submit discipline + fallback toggle); the on-device smoke is the load-bearing gate.

## Timeline

| Task | Effort | Notes |
|---|---|---|
| TerrainPatchMesh + CdlodNode | S | foundation structs |
| CdlodQuadtree + tests | M | core selection logic |
| TerrainNodeBuffer + tests | M | reuse ChunkedBladeBuffer pattern |
| TerrainNodeCull.compute | M | port ChunkCull |
| TerrainVtf.hlsl + TerrainPatch.shader | M | VTF + morph — highest risk |
| GpuTerrainEngine | M | SSOT submit discipline |
| GpuTerrainRenderer | S | player-loop submit wiring |
| On-device milestone smoke | S | user-run; the gate |
| **Total** | **L** | Critical path: PatchMesh → Quadtree → NodeBuffer → Cull → Shader → Engine → Renderer → device smoke |

## Test strategy

- `CdlodQuadtreeTests` — LOD-by-distance correctness, full coverage, bounded node count, deterministic.
- `CdlodMorphMathTests` — morph blend monotonic, hits 0/1 at band edges (the crack-free proof, host-replicated HLSL math).
- `TerrainNodeBufferTests` — AABB partition coverage + empty sentinel (mirrors `ChunkedBladeBufferTests`/`ChunkedInstanceBufferTests`).
- Struct-stride parity test (CdlodNode C# vs HLSL) — same discipline as the BladeInstance 20-byte layout.
- Cull-correctness probe vs CPU frustum reference (EditMode, axis-aligned camera).
- **On-device:** draw-call count + frame time + crack-free visual = the milestone exit gate (user-run).
</content>
