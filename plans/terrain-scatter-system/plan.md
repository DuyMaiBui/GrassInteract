# Plan — Terrain-Integrated Multi-Layer Scatter System

**Created:** 2026-06-03 · **Source brainstorm:** `plans/reports/brainstorm-terrain-scatter-system-20260603.md` (READ FIRST)
**Project:** GrassInteract — Unity 6000.3.13f1, URP 17.3, Mono (NO DOTS/Burst). Reusable **library** deliverable.
**Mode:** interactive, sequential (one phase at a time, approval gate between phases). Not `--team`, not `--parallel` — single live Unity editor + data deps between phases + git:false (no worktree isolation), same constraints as the prior grass-gpu cook.

## Goal

Turn the grass-specific scatter+paint system into a genre-neutral **Scatter** system that (a) binds to Unity Terrain and samples height/holes/slope from `TerrainData`, (b) supports an ordered list of paintable **ScatterLayers**, and (c) renders static **mesh props** ("like grass") through the existing GPU-indirect cull pipeline — all painted by the existing density brush extended with a layer dropdown.

## Locked decisions (from brainstorm — do NOT re-litigate)

1. **Terrain** = Bind + QoL (`ScatterField.boundTerrain` → auto origin/bounds; `TerrainData` sampling). Not native TerrainPaintTool; not replacing Unity Detail/Tree.
2. **Surface sampling** = `ISurfaceSampler` seam (`TerrainSurfaceSampler` + `RaycastSurfaceSampler` fallback); `GrassScatter` refactored to consume it; hole/slope/splat masking.
3. **Data** = `GrassLayer` → `ScatterLayer` (kind {Grass|Mesh}); `[Obsolete]` `GrassLayer` alias one cycle.
4. **Field** = `GrassInteractField` → `ScatterField` (List<ScatterLayer> + boundTerrain); `[Obsolete]` alias.
5. **Prop render** = generalize GPU-indirect pipeline (`ChunkedBladeBuffer`→`ChunkedInstanceBuffer`, reuse `GrassCull.compute` UNCHANGED, new static `ScatterInstanced.shader`). `RenderMeshInstanced`+`InstanceBatchPool` = documented low-count fallback only.
6. **Painter** = extend `GrassPainterWindow` (layer dropdown); optional splat-mask painting.

## Architecture (target)

```
ScatterField (was GrassInteractField)
 ├─ Terrain? boundTerrain ──► ISurfaceSampler selection
 │     bound  → TerrainSurfaceSampler (GetInterpolatedHeight/Normal, GetHoles, GetAlphamaps)
 │     unbound→ RaycastSurfaceSampler (LayerMask, today's behavior)
 └─ List<ScatterLayer> layers
        ScatterLayer.kind == Grass ──► GrassCpuEngine / GrassGpuEngine  (EXISTING, unchanged)
        ScatterLayer.kind == Mesh  ──► MeshScatterEngine
                                         └─ ChunkedInstanceBuffer  (generalized ChunkedBladeBuffer)
                                         └─ GrassCull.compute       (REUSED, untouched)
                                         └─ ScatterInstanced.shader (NEW, static VS, no wind/bend)
GrassScatter.Build(layer, origin, pool, ISurfaceSampler)  ◄── seam injected
GrassPainterWindow  ─ layer dropdown ─► active ScatterLayer.densityMap (brush core unchanged)
```

## Naming charter (library mandate)

Generic/genre/perspective-neutral: `ScatterLayer`, `ScatterField`, `ISurfaceSampler`, `SurfaceHit`, `TerrainSurfaceSampler`, `RaycastSurfaceSampler`, `MeshScatterEngine`, `ChunkedInstanceBuffer`, `ScatterInstanced.shader`. Package name `GrassInteract` retained (deliverable identity). `[Obsolete]` aliases: `GrassLayer`, `GrassInteractField`.

## Non-regression invariants (enforced every phase)

- Grass GPU + CPU tiers and `GrassInteractDemo` render **byte-stable** (same instance counts, same look).
- `GrassCull.compute` cull kernels **untouched** (only the render shader differs for props).
- `[Obsolete]` aliases keep the existing demo compiling + running with zero scene edits.
- Per-phase gate = **live-editor evidence via Unity MCP** (`set_active_instance GrassInteract@de203215`, screenshots for any indirect render — `UnityStats.triangles` does NOT count `RenderMeshIndirect`). Main loop drives MCP verification (implementer sub-agents historically stall on live gates).

## Known render gotchas (from project memory — carry into every render phase)

- `new RenderParams { ... }` object-initializer leaves `renderingLayerMask=0` → URP silently skips the draw. Use `new RenderParams(material)` ctor.
- Runtime-toggled `shader_feature_local` keywords are stripped from player builds → use `multi_compile_local`.
- `TWO_PI`/`PI`/`HALF_PI` are predefined URP `Macros.hlsl` macros → prefix custom ones (`GRASS_`/`SCATTER_`).
- Correct API name: `SystemInfo.supportsIndirectArgumentsBuffer`.
- Screenshot capture inflates that frame's CPU time → measure perf via `Time.smoothDeltaTime`, not the capture frame.

## Phase index

| Phase | Title | Delivers | File |
|---|---|---|---|
| 1 | Surface-sampler seam + Terrain bind | Grass follows terrain height/holes/slope | `phase-1.md` |
| 2 | Generalize to ScatterLayer + layer list | Multi-layer painting infra (grass-only) | `phase-2.md` |
| 3 | Mesh prop kind (GPU-instanced) | Props-like-grass via texture brush | `phase-3.md` |
| 4 | Polish: splat-mask paint, align-to-normal, slope ranges, UX | Production quality | `phase-4.md` |

Critical path: 1 → 2 → 3 (each depends on the prior). Phase 4 depends on 3.

## Risk Assessment (plan-level)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| Generalizing `ChunkedBladeBuffer` regresses the grass GPU tier | 3 | 5 | 15 | Phase 3 keeps grass on the original buffer type OR proves byte-stable counts before/after; cull compute untouched; re-run all 3 existing harnesses each phase |
| `GrassScatter` refactor breaks placement byte-stability (rng draw order) | 3 | 4 | 12 | Preserve exact rng draw order (localX, localZ, accept[, yaw, scale]); sampler only replaces the Y-snap step; diff instance count + first-N positions vs baseline |
| `[Obsolete]` alias migration breaks `GrassInteractDemo` | 2 | 4 | 8 | Alias = subclass/wrapper with kind=Grass; open demo + render before declaring phase done; migration guide in Phase 2 |
| `TerrainData.GetHoles`/`GetAlphamaps` cost or API mismatch | 2 | 3 | 6 | Sample at build time (edit), cache alphamaps per bake; verify on a real Terrain in-editor |
| Multi-tile terrain seams | 2 | 2 | 4 | v1 = one field per terrain tile; neighbor-stitching explicitly deferred (documented) |

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Surface-sampler seam + Terrain bind | M (~3d) | Refactor + new samplers; byte-stability diff is the long pole |
| Phase 2: Generalize ScatterLayer + field list | M (~3d) | SO/field generalization + `[Obsolete]` shims + painter dropdown |
| Phase 3: Mesh prop kind (GPU-instanced) | L (~1wk) | Buffer generalization + new shader + engine + harness; highest risk |
| Phase 4: Polish | M (~3d) | Splat-mask paint, align-to-normal, slope ranges, UX |
| **Total** | **~2.5wk** | Critical path 1→2→3; 4 optional/last |

## Cook handoff

After approval: `/t1k:cook plans/terrain-scatter-system/phase-1.md` (sequential, one phase per approval gate — mirrors the grass-gpu cook cadence).
