# Plan — Defer Grass Scatter Rebuild to Mouse-Up + Density Heatmap Overlay

Source design: `plans/reports/grass-paint-defer-rebuild-to-mouseup-brainstorm.md` (approved 2026-06-17)
Mode: standard · single-agent · Cook handoff: `/t1k:cook plans/grass-paint-defer-rebuild-plan.md`

## Goal

Eliminate per-drain CPU grass scatter during a brush drag. Today every ~15 Hz
`DrainAndPreview` tick calls `PreviewActiveScatter` → `RebuildGrassLayerDeferred`,
which re-scatters **every** painted tile (full `DensityPlacement.Build`), not just
the brush footprint → lag on large painted maps. After this change: zero scatter
during drag; one rebuild on mouse-up; a live density heatmap overlay gives the
artist painted-area feedback during the stroke.

## Success criteria

1. During a grass drag, the Unity Profiler shows **no** `DensityPlacement.Build` /
   `GrassCpuEngine.Build` calls; they appear **once** on mouse-up.
2. Editor framerate stays smooth while dragging a large grass stroke over a
   previously-painted area (the prior lag repro).
3. The painted footprint is visible live via the heatmap overlay during drag;
   final blades on release match where the overlay showed density.
4. Overlay is fully removed after mouse-up / tool switch / undo — never lingers
   over real blades.
5. Existing behaviour intact: stamp buffering, density RT painting/writeback,
   deferred-dispose flicker fix, end-of-stroke rebuild, props path, undo grouping.

## Scope / files

| File | Change |
|---|---|
| `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Stroke.cs` | Drop in-drag blade rebuild from `PreviewActiveScatter`; keep density flush. Hook overlay show/hide. |
| `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Density.cs` | Expose read access to `densityRtCache` entries (coord + RT) for the overlay. |
| `Assets/WorldPainter/Editor/Brush/TerrainBrushPreview.cs` *(or new sibling)* | Density heatmap overlay draw, mirroring `DrawMaskDecal` (per-tile quad, `rootMatrix` premultiply, `Graphics.DrawMeshNow`). |
| New: density-heatmap editor shader + material | RFloat density → color ramp, additive/alpha decal. |
| New: `Assets/WorldPainter/Tests/Editor/...` EditMode test | Asserts in-drag path triggers no scatter Build; mouse-up does. |

---

## Phase 1 — Remove in-drag blade rebuild (the actual perf fix)

**Change**
- In `WorldPainterSculptTool.Stroke.cs` → `PreviewActiveScatter(painter)`
  (lines 164–173): keep the layer-type guard + `FlushAllDensityRTs()`, **remove**
  the `painter.RebuildGrassLayerDeferred(layer)` call. Keep `SceneView.RepaintAll()`
  (overlay needs the repaint; Phase 2 relies on it).
- `HandleMouseDown` (line 66) and `DrainAndPreview` (line 152) both call
  `PreviewActiveScatter` → both automatically lose the in-drag rebuild. No other edit.
- Confirm `HandleMouseUp` (lines 114–122) is now the **sole** per-stroke grass
  rebuild — it already calls `RebuildGrassLayerDeferred(grassLayer)`. No change there.

**Rationale**
Density RT painting (the actual paint data) is GPU compute in `DoStamp`/`DispatchOneTile`
and is untouched. Only the CPU scatter generation is deferred. The deferred-dispose
machinery + end-of-stroke deferred rebuild already exist, so this is a *removal*.

**Verify**
- Profiler: drag a grass stroke → no `DensityPlacement.Build`; release → exactly one
  rebuild burst. Density still persists (paint a stroke, release, blades appear correctly).
- Props path (`LayerType.Props`) unaffected — `PreviewActiveScatter` early-returns for non-grass.

**Interim UX note:** after Phase 1 alone, blades vanish during drag (only brush ring
visible). Phase 2 restores feedback. Land them together before calling the feature done.

---

## Phase 2 — Density heatmap overlay (drag feedback)

**Overlay source data**
- `densityRtCache` (Density.cs, line 29) is keyed by tile coord and populated on
  first-touch — its keys ARE the touched tiles, and the RTs hold live in-progress
  density (updated by the compute dispatch each stamp). The overlay samples these
  directly each repaint — **no flush needed** for display.
- Add a read accessor in Density.cs, e.g.
  `internal IEnumerable<(Vector2Int coord, RenderTexture rt)> EnumerateDensityRTs()`
  yielding non-legacy entries.

**Draw path (mirror `DrawMaskDecal`, TerrainBrushPreview.cs lines 177–214)**
- New `DrawDensityOverlay()`: for each `(coord, rt)`, compute the tile's world rect
  via `TerrainWorldGrid` (tile coord → origin + tile size), build a ground quad,
  set `_MainTex = rt`, draw with `rootMatrix * localMatrix` premultiply and
  `Graphics.DrawMeshNow` (Handles.matrix is ignored by DrawMeshNow — same reason the
  decal bakes `rootMatrix`; this respects the painting→world root transform).
- New heatmap shader (`WorldPainter/DensityHeatmap`): samples RFloat `_MainTex.r`,
  maps 0→transparent, low→cool, high→warm via a ramp; alpha-blended decal, ZTest
  always/lift like the decal (`Y_OFFSET` pattern) so it reads over terrain.
- Lift quads slightly above terrain (reuse decal `Y_OFFSET_MIN`/`Y_OFFSET_FRACTION`
  pattern) to avoid z-fighting.

**Lifecycle (show only mid-stroke)**
- Enable overlay state on `HandleMouseDown` (grass layer only); the overlay draws
  inside the brush `OnSceneGui` repaint while active.
- **Disable** on `HandleMouseUp` (after rebuild scheduled), and defensively on tool
  exit (`OnDisable`/`OnWillBeDeactivated`) and undo. Tie "active" to stroke state so
  if `densityRtCache` is empty (RTs released in `TeardownActiveStroke`, line 206) the
  overlay no-ops — releasing the RTs already neutralizes it; the explicit flag is belt-and-suspenders.
- Material/mesh cached with `HideFlags.HideAndDontSave`, lazy-built like `decalMat`/`quadMesh`.

**Verify**
- Drag over multiple tiles → heatmap appears on each touched tile, intensifies as
  density accumulates, follows the brush.
- Release → overlay disappears same frame blades appear; no double-image.
- Switch tool mid-strokeless / undo → no lingering overlay.
- Shader-not-found → overlay skipped, ring still drawn (match decal's graceful skip).

---

## Phase 3 — Profile + EditMode test + cleanup

- **Profiler capture**: document before/after (scatter Build count during a fixed
  large-area drag) in the brainstorm report's "results" addendum.
- **EditMode test** (`Assets/WorldPainter/Tests/Editor`): drive a synthetic stroke
  (mouse-down + N drags + mouse-up) against a test painter+grass layer; assert grass
  engine rebuild count == expected (0 mid-drag, ≥1 on up). If direct count is hard to
  observe, assert via a seam: instrument `RebuildGrassLayerDeferred` call count behind
  an editor-test hook, or assert `surfaceEngines` is rebuilt only post-up.
  (Note project memory: `execute_code` is unusable here → verify via EditMode test +
  `run_tests`, not inline code execution.)
- Run full EditMode suite (`run_tests`) → zero new failures.
- Remove any now-dead helpers the rebuild removal orphaned (none expected — confirm).

---

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| Mouse-up rebuild hitch on very large painted map | 3 | 2 | 6 | One-time per stroke vs 15×/sec; acceptable. Dirty-tile rebuild is the documented follow-up lever if it bites. |
| Overlay quad misaligned under non-identity root transform | 3 | 3 | 9 | Mirror `DrawMaskDecal` exactly: `rootMatrix` premultiply + `DrawMeshNow` (proven path). Test under a rotated/scaled WorldPainter root. |
| Overlay lingers after stroke (stale RTs / missing teardown) | 2 | 3 | 6 | Tie active-flag to stroke + rely on `densityRtCache` empty after `TeardownActiveStroke`; defensive disable on tool-exit/undo. |
| Heatmap shader not imported / pink material | 2 | 2 | 4 | Graceful skip like decal (`Shader.Find` null → no overlay, keep ring). |
| Artist confused by no-blades-during-drag if overlay subtle | 2 | 2 | 4 | Tune ramp/alpha for clear contrast; overlay covers full touched footprint. |
| RFloat density range not normalized → flat-color heatmap | 2 | 2 | 4 | Sample max density / known target-density scale into ramp; clamp 0..1 by layer density config. |

No score ≥15 → no phase-blocking risk.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Remove in-drag rebuild | S (~0.5d) | Pure removal + profiler verify. Lowest risk, full perf win. |
| Phase 2: Density heatmap overlay | M (~2d) | New shader + per-tile draw + lifecycle; mirrors existing decal. |
| Phase 3: Profile + EditMode test | S (~1d) | Test harness for rebuild-count assertion is the main effort. |
| Total | ~3.5d | Critical path: P1 → P2 (ship together) → P3. |

## Notes / constraints carried from project memory

- WorldPainter coords are root-LOCAL (painting space); overlay MUST map painting→world
  via `rootMatrix` (`_WP_ROOT_TRANSFORM`) — `DrawMaskDecal` already does this.
- `execute_code` MCP is unusable in this env → Phase 3 verifies via EditMode test + `run_tests`.
- Per-tile GPU buffer/texture binding caveats apply to grass rendering, not this overlay
  (overlay binds the density RT per draw, not a shared global).
