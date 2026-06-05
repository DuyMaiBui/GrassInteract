# Brainstorm — Terrain-Integrated Multi-Layer Scatter System

**Date:** 2026-06-03 · **Skill:** /t1k:brainstorm · **Status:** APPROVED → /t1k:plan
**Project:** GrassInteract (Unity 6000.3.13f1, URP 17.3, Mono — no DOTS/Burst). Library deliverable (reusable).

## Problem statement

User: *"improve terrain tool built-in of Unity; integrate grass to terrain and painting grass; props (like grass) use texture brush."*

Decoded against the existing code: grass density painting over terrain **already exists** (`GrassPainterWindow` raycasts any collider incl. Unity Terrain, stamps a density map; `GrassScatter` rejection-samples + raycast-snaps to ground). The genuinely new asks are:
1. **Deeper Unity-Terrain integration** — bind to a Terrain, sample height/holes/slope from `TerrainData` instead of raycast.
2. **Generalize the grass-only scatter+painter into a multi-type PROP system** — paint flowers/rocks/bushes like grass, each its own layer + density brush, rendered GPU-instanced.

## What already exists (reuse, do not rebuild)

- `GrassLayer` (SO): densityMap, fieldBounds, targetInstances, scaleRange, seed, groundSnapMask, renderConfig.
- `GrassPainterWindow`: density texture brush (paint/erase, radius/strength/falloff, live overlay, throttled flush, re-scatter on stroke-end) over any collider incl. Terrain.
- `GrassScatter.Build`: rejection sampling vs density map + downward `Physics.Raycast` ground-snap; deterministic by seed.
- `GrassFieldSpace` (world↔uv), `InstanceBatchPool` (1023-matrix slabs), GPU-indirect cull pipeline (`ChunkedBladeBuffer` + `GrassCull.compute` + per-LOD indirect args), CPU/GPU grass tiers.

## Decisions (via AskUserQuestion)

| Axis | Decision |
|---|---|
| Terrain integration depth | **Bind + QoL** — `ScatterField.boundTerrain`; auto bounds/origin; `TerrainData` height/holes/slope sampling. (Not native TerrainPaintTool; not replacing Unity Detail/Tree.) |
| Prop placement/render | **GPU-instanced static meshes** (Mesh kind), LODs, frustum/distance cull; no per-vertex deform. |
| Layer organization | **Stacked `ScatterLayer`s** — generalize `GrassLayer`; field holds an ordered list; painter dropdown selects active layer. |
| Surface sampling | **`TerrainData` direct + raycast fallback** — height/holes/slope/splat masking when bound; raycast for mesh/plane. |
| Prop pipeline | **Generalize the GPU-indirect pipeline** — `ChunkedBladeBuffer`→`ChunkedInstanceBuffer`, reuse `GrassCull.compute`, new static `ScatterInstanced.shader`; grass + props share one cull path. |

## Recommended architecture

**Seam — `ISurfaceSampler`** (load-bearing): extract the hardcoded raycast in `GrassScatter.Build`.
```
interface ISurfaceSampler { bool TrySample(float wx, float wz, out SurfaceHit hit); }
struct SurfaceHit { float y; Vector3 normal; float slopeDeg; float[] splatWeights; }
TerrainSurfaceSampler(Terrain)  // GetInterpolatedHeight/Normal, GetHoles, GetAlphamaps
RaycastSurfaceSampler(LayerMask) // today's behavior, fallback
```
Scatter skips candidate on hole / slope>max / splatWeight<threshold.

**Data — `ScatterLayer` (SO)** generalizes `GrassLayer` (`[Obsolete]` alias, kind=Grass, one release cycle):
`kind {Grass|Mesh}`, densityMap, targetInstances, seed, scaleRange, maxSlopeDeg, alignToNormal, heightOffset; Grass→`GrassLODConfig renderConfig`; Mesh→`Mesh[] meshLODs, Material material, float[] lodDistances`.

**Field — `ScatterField`** generalizes `GrassInteractField` (`[Obsolete]` alias): `Terrain? boundTerrain`, `List<ScatterLayer> layers`; each layer scatters independently → Grass layers use existing `GrassCpuEngine`/`GrassGpuEngine`; Mesh layers use new `MeshScatterEngine`.

**Prop render — `MeshScatterEngine`**: generalize `ChunkedBladeBuffer`→`ChunkedInstanceBuffer` (posWS + packed yaw/scale + LOD idx), reuse `GrassCull.compute` cull machinery unchanged, new static `ScatterInstanced.shader` (no wind/bend VS). `Graphics.RenderMeshInstanced`+`InstanceBatchPool` documented as low-count fallback.

**Painter — extend `GrassPainterWindow`** (don't fork): layer dropdown picks active density map (brush core already kind-agnostic); optional paint-by-terrain-splat mask.

## Naming charter (library mandate)
Generic, genre/perspective-neutral: `ScatterLayer`, `ScatterField`, `ISurfaceSampler`, `MeshScatterEngine`, `ChunkedInstanceBuffer`, `ScatterInstanced.shader`. Package name `GrassInteract` retained (its identity). `[Obsolete]` aliases for `GrassLayer`/`GrassInteractField` one cycle + migration guide.

## Phasing (→ plan)
1. **Surface-sampler seam** — `ISurfaceSampler` + Terrain/Raycast impls, refactor `GrassScatter`, terrain bind, hole/slope masking. *Delivers grass-on-terrain.* Verify: grass follows terrain height/holes; raycast path byte-stable for non-terrain.
2. **Generalize → `ScatterLayer` + layer list** — `[Obsolete]` shims, painter layer dropdown. *Multi-layer infra, still grass-only.* Verify: existing demo unchanged; 2 grass layers paint independently.
3. **Mesh prop kind** — `ChunkedInstanceBuffer`, `MeshScatterEngine`, `ScatterInstanced.shader`, prop LOD/cull. *Props-like-grass via brush.* Verify (live MCP, screenshot): props instanced on terrain, frustum/distance cull, harness for cull parity.
4. **Polish** — splat-mask painting, align-to-normal, per-layer slope ranges, editor UX.

## Risks
- `TerrainData.GetHoles`/`GetAlphamaps` reads at build time (editor) — confirm runtime availability if scatter ever runs at runtime (currently edit-time bake).
- Multi-tile terrains: v1 = one field per terrain tile; neighbor-stitching deferred.
- Generalizing `ChunkedBladeBuffer` must not regress the grass GPU tier — keep grass path byte-stable; cull compute untouched.
- `[Obsolete]` migration must not break the existing `GrassInteractDemo`.

## Success criteria
- Grass placement follows Unity Terrain height + skips holes + respects max slope.
- ≥2 layer types (grass + a mesh prop) paint independently via one brush with a layer dropdown.
- Mesh props render GPU-instanced with LOD + frustum/distance cull; cull-parity harness PASS; live screenshot on `GrassInteract@de203215`.
- Existing demo + grass GPU/CPU tiers unchanged (regression-free); `[Obsolete]` aliases compile.

## Next
→ `/t1k:plan` with this report as context.
