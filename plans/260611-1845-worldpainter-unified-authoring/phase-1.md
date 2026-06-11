# Phase 1 — Vertical Slice: Shell + Layer Stack + Unified Brush Engine + Sculpt + Migration + GATE

**Effort:** L · **Blocked by:** — · **Blocks:** P2, P3, P4, P5, P6

## Goal

Stand up the `WorldPainter` component (runtime/authoring split), the UIToolkit inspector with the Scatter-Studio-token theme, the Layer Stack + hybrid filter chips, and the **unified brush engine** (mask LUT + falloff `CurveField` + spacing-stamping) — with **Sculpt working end-to-end** (one Height layer + one Splat layer present, Height paintable). Add the one-time migration menu from `GpuTerrainRenderer` + `TerrainScatterConfig`, and re-home owner-level tests. **End on a manual GATE**: validate inspector-only ergonomics and measure the flat-merge cost before P2.

## File ownership (concrete paths)

### New
| Path | Responsibility | ≤200 lines |
|---|---|---|
| `Assets/GpuTerrain/Runtime/WorldPainter.cs` | runtime component: LateUpdate submit scheduler, residency/visibility early-out | yes |
| `Assets/GpuTerrain/Runtime/WorldPainter.Data.cs` | Tier-A inline schema (worldGrid, tile refs, splatLayers, scatterLayer refs, biome refs, brush refs) — partial | yes |
| `Assets/GpuTerrain/Editor/WorldPainter.Authoring.cs` | `#if UNITY_EDITOR` brush-engine driver + stroke loop entry | yes |
| `Assets/GpuTerrain/Editor/WorldPainterInspector.cs` | `[CustomEditor(typeof(WorldPainter))]` UIToolkit `CreateInspectorGUI` root | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLayerStackView.cs` | Photoshop-style layer stack + eye/lock/solo + drag-reorder + `+▾` guided add | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterFilterChips.cs` | All/⛰/🎨/🌿/🌳 filter chips (ship in P1) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterBrushDock.cs` | constant brush dock (size/strength/falloff `CurveField`/spacing/flow) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterState.cs` | shared authoring state (active layer, last-stroked tiles) — ports `TerrainSculptState` pattern | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainter.uss` | theme (Scatter Studio Pro tokens) | — |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLight.uss` | light variant | — |
| `Assets/GpuTerrain/Shaders/BrushMask.hlsl` | shared per-texel weight include (falloff LUT × stamp × strength × sign) | yes |
| `Assets/GpuTerrain/Editor/WorldPainterMigration.cs` | `Tools/WorldPainter/Migrate from GpuTerrainRenderer + TerrainScatterConfig` menu | yes |

### Modified
| Path | Change |
|---|---|
| `Assets/GpuTerrain/Shaders/TerrainBrush.compute` | refactor: height + splat kernels become one-liners over `BrushMask.hlsl`; keep behavior/signatures stable for `TerrainBrushMathTests` |
| `Assets/GpuTerrain/Editor/TerrainSculptUndo.cs` | retarget snapshots onto `WorldPainter` tile refs; depth 10, 128 MB cap, evict-oldest; one `Undo` group per stroke |
| `Assets/GpuTerrain/Editor/TerrainSculptRtWriteback.cs` | retarget commit to `WorldPainter` tile refs (density encoder deferred to P3) |
| `Assets/GpuTerrain/Editor/TerrainPaintTargetResolver.cs` | accept `WorldPainter` tile/residency set as input |
| `Assets/GpuTerrain/Tests/Editor/*` (owner-level only) | re-home `GpuTerrainRenderer`-owner tests onto `WorldPainter`; data/math tests untouched |

### Reuse unchanged (cite — do NOT modify)
`TerrainTileAsset.cs`, `TerrainWorldGrid.cs`, `TerrainHeightFormat.cs`, `CdlodQuadtree.cs`, `GpuTerrainEngine.cs`, `TerrainBrushStroke.cs`, `TerrainBrushPreview.cs`, `TerrainSculptConfig.cs`, `TerrainValidationSceneBuilder.cs` (wrapped by migration + empty-state), `ScatterStudio.uss`/`ScatterStudioLight.uss` (token source). `HeightmapSurfaceSampler.cs` + `ISurfaceSampler.cs` (seam).

## Tasks (each with verify-check)

1. **Component split scaffold** — create `WorldPainter.cs` (+`.Data.cs`) in Runtime asmdef, `WorldPainter.Authoring.cs` in Editor asmdef under `#if UNITY_EDITOR`. → verify: `read_console` clean compile; grep confirms zero authoring symbols in Runtime asmdef.
2. **Tier-A schema** — encode design §7.2 schema inline (worldGrid, tiles→`TerrainTileAsset` refs, splatLayers≤4, scatterLayers/biomes/brushPresets as refs). → verify: a `WorldPainter` serializes/deserializes round-trip in a scene; `EditorJsonUtility` snapshot stable.
3. **UIToolkit inspector + theme** — `CreateInspectorGUI` root, apply `.pro`/`.light` via `EditorGUIUtility.isProSkin`, port exact Scatter Studio tokens into `WorldPainter.uss`. → verify: inspector renders with selected-row tint `rgba(63,127,210,.35)`, card BG `rgb(55,55,55)`.
4. **Layer stack + filter chips** — vertical stack (eye/lock/solo, drag-reorder, `+▾` guided add with smart defaults), All/⛰/🎨/🌿/🌳 chips. Height(base) + one Splat row present by default. → verify: add/remove/reorder updates schema via `Undo.RecordObject`; chips filter visible rows.
5. **Constant brush dock** — size/strength/falloff `CurveField`/spacing/flow; one `BrushSettings` SSOT (design §5.1). → verify: editing a field updates `WorldPainterState`; dock position fixed across layer selection.
6. **`BrushMask.hlsl` + compute refactor** — extract per-texel weight (falloff LUT × stamp × strength × sign) into the include; rewrite Height raise/lower/smooth/flatten + splat-blend kernels over it. 256×1 `RFloat` LUT uploaded on `CurveField` change. → verify: `run_tests` — `TerrainBrushMathTests` (22) all green.
7. **Spacing-stamping stroke model** — interpolate drag path, stamp every `spacing` m; `flow` accumulates per stamp (design §5.4). → verify: dragging at varying mouse speeds yields consistent deposited height (manual + a stamp-count math test).
8. **Sculpt end-to-end** — wire stroke → `TerrainPaintTargetResolver` → `TerrainBrushStroke` dispatch → live RT bind → `TerrainSculptRtWriteback` commit on mouse-up, all targeting the `WorldPainter` tile refs. → verify: brush visibly changes rendered terrain in real time; persists after domain reload (manual, MCP scene).
9. **Unified undo** — `TerrainSculptUndo` retargeted, depth 10 / 128 MB / evict-oldest, one `Undo` group per stroke so a single Ctrl+Z walks interleaved structural+stroke history. → verify: `run_tests` — `TerrainSculptUndoTests` (11) green; manual Ctrl+Z reverts one stroke then one structural change.
10. **Migration menu** — `WorldPainterMigration.cs` reads `GpuTerrainRenderer.tiles` + `GrassInteract.Runtime.TerrainScatterConfig.layers`, builds Tier-A data + `Assets/Worlds/<name>/` folder (reuse `TerrainValidationSceneBuilder`); dry-run report first, originals preserved. → verify: migrate the 2-tile validation scene → `WorldPainter` renders identical tiles; report lists every mapped tile/layer.
11. **Test re-home (tracked, bounded blast-radius)** — move ONLY `GpuTerrainRenderer`/owner-level assertions onto `WorldPainter`; data/math tests untouched. Document which test files changed and why. → verify: `run_tests` full suite — 216 baseline minus re-homed deltas, **zero failures**.
12. **Build-isolation verify** — confirm authoring never ships: compile a player build target (or `BuildPipeline` script-only check) and grep that `WorldPainter.Authoring`/brush symbols are absent. → verify: player-target compile clean; authoring symbols not in runtime asmdef.

## GATE (manual — design §9 P1 gate)

Before P2 begins, the user validates:
- **Inspector-only ergonomics** — sculpt + the layer-stack + brush dock are comfortable purely in the inspector (no window).
- **Flat-merge cost** — measured: inspector repaint cost, compile time, scene weight delta, full-suite pass.

Gate is a HARD-GATE (`.claude/rules/workflow-gates.md`): no P2 without explicit user go-ahead. On fail → `AskUserQuestion` with concrete options (adjust IA / split component / revisit asset-ref boundary).

## Risk Assessment (L×I)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Brush-engine refactor breaks working live sculpt | 4 | 4 | **16** | Ship Sculpt through new include before any new payload; `TerrainBrushMathTests` is the contract gate |
| Test re-home breaks data/math tests (over-broad blast-radius) | 4 | 5 | **20** | Re-home ONLY owner-level tests; full suite before/after; freeze SSOT types |
| Authoring leaks into player build | 3 | 5 | **15** | `#if UNITY_EDITOR` + Editor-asmdef ownership; task 12 build-isolation verify |
| Migration mis-maps `TerrainScatterConfig` (lives in GrassInteract, not GpuTerrain) | 3 | 4 | 12 | Dry-run report; preserve originals; cite correct asmdef ref in migration |
| Inspector ergonomics fail the gate (IA wrong) | 2 | 4 | 8 | Gate is explicit; cheap to iterate IA before payloads exist |

## Test plan

- `run_tests` full EditMode suite (GpuTerrain.EditorTests + GrassInteract.EditorTests): zero failures, esp. `TerrainBrushMathTests`, `TerrainSculptUndoTests`, `TerrainSculptRtWritebackTests`, `HeightmapSurfaceSamplerTests`.
- New tests: spacing-stamp count math (deterministic stamps-per-metre), Tier-A schema round-trip, migration mapping (tile/layer count parity).
- Manual (MCP scene): sculpt visibly updates rendered mesh, persists across domain reload; single Ctrl+Z semantics.
