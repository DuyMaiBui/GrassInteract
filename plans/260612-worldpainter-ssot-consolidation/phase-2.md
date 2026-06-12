# Phase 2 — Scatter absorption + WorldPainter reads container

**Effort:** L · **Wave:** B (sequential foundation) · **Depends on:** P1 · **Blocks:** P3 (deletes), P5, P8

## Goal

Absorb `ScatterField` orchestration into a new `WorldPainter.Scatter.cs` partial (Rebuild / StepAll / SubmitAll / engine selection). **Keep** the engines (`GrassGpuEngine`, `GrassCpuEngine`, `InstancedPropEngine`, `InstanceBatchPool`) untouched. Repoint `WorldPainter` to read tiles + layers from the referenced `WorldMapAsset` instead of inline `tiles`/`layers` lists. After this lands, `ScatterField.cs` + `GpuTerrainScatterGround.cs` are dead and can be deleted in P3.

## What absorbs vs what stays

- **Absorb (move orchestration into WorldPainter):** `ScatterField.cs` Rebuild/StepAll/SubmitAll/engine-selection logic → `WorldPainter.Scatter.cs`.
- **Keep (untouched engines + helpers):** `GrassGpuEngine`, `GrassCpuEngine`, `InstancedPropEngine`, `InstanceBatchPool`, `IGrassEngine`, `IScatterPlacement`, all `Grass*` sim/buffer files, `Density/InstancePlacement`, samplers.
- **Internalize:** the `GpuTerrainScatterGround` bridge job (height grounding) is already partially inlined in `WorldPainter.cs` (`DriveScatterField` + `HeightmapSurfaceSampler`); finish moving it so no external bridge component is needed.

## File-ownership group (single group — no fan-out)

**G2.1 — Scatter partial + container read (Runtime, EDITED ONLY IN P2 then FROZEN for WAVE D)**
- `Assets/WorldPainter/Runtime/WorldPainter.Scatter.cs` *(new partial)* — `RebuildScatter`, `StepScatter`, `SubmitScatter`, engine selection (GPU vs CPU tier). Reads layers from `this.map` (`WorldMapAsset`).
- `Assets/WorldPainter/Runtime/WorldPainter.cs` *(edit)* — replace `cachedScatterField`/`DriveScatterField` with calls into the new partial; `LateUpdate` → `SubmitTerrain + StepScatter + SubmitScatter`.
- `Assets/WorldPainter/Runtime/WorldPainter.Data.cs` *(edit)* — add `[SerializeField] WorldMapAsset? map;` reference; mark inline `tiles`/`layers` lists obsolete (read-through to `map` getters; remove fully in P3 once nothing else reads them).
- `Assets/WorldPainter/Runtime/WorldPainter.Render.cs` *(edit)* — terrain submit reads tile RTs from `map.EnumerateTiles()` instead of inline list.

> **FROZEN-PARTIAL RULE:** these four files are edited ONLY in P1/P2. After P2 green, they are frozen for WAVE D. Any later WorldPainter hook goes in a new owned partial (e.g. `WorldPainter.Bake.cs` in P8).

## Parallelizable vs sequential

**Sequential, single group.** Engine-keeping means the diff is contained, but the WorldPainter partials are mutually coupled — one subagent edits them in sequence. Do NOT fan out across the WorldPainter partials (shared-file race).

## Verification

1. **Compile:** `read_console` clean + `run_tests` in one pass.
2. **Existing tests must stay green:** `HeightmapSurfaceSamplerTests`, `WorldPainterOwnerTests`, `ScatterLodCullTests`, `InstanceVisibilityColliderDriverTests`, `ChunkedInstanceBufferTests` — the `ISurfaceSampler` seam is UNCHANGED, so these prove the absorption preserved behavior.
3. **New test:** `WorldPainterScatterAbsorptionTests.cs` — a WorldPainter with a `WorldMapAsset` (one tile, one density layer) rebuilds + steps + submits scatter without a `ScatterField` component present.
4. Play-mode smoke (manual via MCP later in P9): grass renders from container with no `ScatterField` in scene.

## Success criteria

- `WorldPainter.Scatter.cs` drives scatter end-to-end with **no `ScatterField` MonoBehaviour** in the scene.
- `WorldPainter` reads tiles + layers from `WorldMapAsset`.
- `ScatterField.cs` and `GpuTerrainScatterGround.cs` have **zero live references** from runtime + WorldPainter (grep-verified) → ready to delete in P3.
- All engine files unchanged; `ISurfaceSampler` seam intact; pre-existing scatter tests green.
