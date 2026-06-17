# Plan — WorldPainter: Smooth Grass Paint + Click-Terrain-to-Select

**Date:** 2026-06-17 · **Source brainstorm:** `plans/reports/worldpainter-smooth-grass-paint-and-terrain-pick-brainstorm.md`
**Scope:** Editor-only. No runtime/build impact. **Mode:** standard.

## Goal

1. **Smooth continuous grass paint on drag** — decouple high-frequency mouse sampling from expensive GPU work via a buffered stamp pipeline drained at a fixed ~15 Hz, with per-layer scatter rebuild. Eliminates the laggy/steppy drag.
2. **Click GPU terrain in Scene view → select the WorldPainter GameObject** — gated to "a WorldPainter exists AND paint mode OFF AND default/View tool active," deferring to native picking first.

## Key architecture facts (from scout)

- Stroke input: `WorldPainterSculptTool.OnSceneGui` (`WorldPainterSculptTool.cs:307-324`), **inspector-bound** subscription (`WorldPainterInspector.cs:83` sub / `:346` unsub) — only live while the WorldPainter inspector is shown.
- Continuous-stroke math exists + tested: `WorldPainterStroke.Advance` / `CountStamps`.
- Per drag stamp currently dispatches inline: `DoStamp` → `TerrainPaintTargetResolver.Resolve` → per-tile `GetOrCreateDensityRT` (cached per tile, `WorldPainterSculptTool.Density.cs:51`) → density compute (`DensityDispatch.Run`, `DensityBrushTools.cs:36-69`) → `densityEncoder.RequestAsync` (0.15s throttle).
- `RebuildScatterPreview()` (`WorldPainter.Render.cs:162`) rebuilds **ALL** layers/tiles; called **only** on MouseUp (`Stroke.cs:93-97`). Per-**layer** entry points exist: `RebuildGrassLayer(GrassLayer)` / `RebuildPropLayer(PropLayer)` (`WorldPainter.SurfaceLayers.cs:79,109`). No per-tile rebuild.
- `FlushAllDensityRTs` / `ReleaseAllDensityRTs` (`Density.cs:82,99`), called in `TeardownActiveStroke` on MouseUp (`Stroke.cs:130-131`).
- Analytic terrain raycast (no collider, exact on visible surface): `TryGetBrushWorldPoint` + static `TryMapSurfaceHit` / `TryInlineTilesSurfaceHit` (`Stroke.cs:239-348`) + `TerrainHeightSampleCpu`. Currently **private** to the sculpt tool.
- Gating state: `WorldPainterState.PaintModeActive`, `ActivePainter`, `ActiveBrushToolId`, `EffectiveLayerType`, `IsClickOnlyTool` (`WorldPainterState.cs`).
- Tests: `WorldPainter.Tests` asmdef at `Assets/WorldPainter/Tests/Editor/`; pattern `WorldPainter<Feature>Tests.cs`, example `WorldPainterStrokeTests.cs`.

## Phases

### Phase 1 — Stamp buffer + throttled drain scheduler (smooth pipeline)

**Files (own):** `Assets/WorldPainter/Editor/Brush/StrokeStampBuffer.cs` (new), `WorldPainterSculptTool.Stroke.cs`, `WorldPainterSculptTool.cs` (drain hook in `OnSceneGui`), `WorldPainterSculptTool.Density.cs` (if drain helpers needed).

1. **`StrokeStampBuffer`** (new, editor): a simple FIFO of pending stamp world-positions (painting space). Methods: `Enqueue(Vector3)`, `DrainInto(List<Vector3>)` / `PopAll`, `Count`, `Clear`. Main-thread only. No GPU.
2. **`HandleMouseDrag`** — stop calling `DoStamp` inline. Instead, in the `stroke.Advance(...)` `onStamp` callback, **enqueue** the interpolated `stampPos` into the buffer (O(1)). Keep `CommitLastStrokedState` semantics but driven by the drain.
3. **Throttled drain** — add a time-gated drain (fixed ~15 Hz; `1/15s` interval; **not** exposed as config). Drive it from `OnSceneGui` (check elapsed since last drain on each scene repaint) plus an `EditorApplication.update` subscription registered on stroke begin / removed on stroke end so drains still fire if the mouse pauses mid-drag. On drain: pop ALL pending stamps → call `DoStamp` for each (reusing the existing per-tile RT cache + `DispatchOneTile`) → request one coalesced writeback → trigger the Phase-2 scoped rebuild once.
4. **`HandleMouseDown`** — keep the immediate initial stamp (instant feedback at click). Initialize/clear the buffer and start the drain clock.
5. **`HandleMouseUp` / `TeardownActiveStroke`** — drain any remaining buffered stamps **synchronously**, then run the existing full `FlushAllDensityRTs` + final full `RebuildScatterPreview`. Unregister the `EditorApplication.update` drain. Preserve the single Undo group per stroke and seam-sync neighbour registration (unchanged).

**Invariant:** the set of stamps dispatched across a stroke is identical to today's per-event path (same `stroke.Advance` spacing math) — only the *timing* of dispatch + rebuild changes. Final committed density must be byte-identical to the current per-stamp result.

**Verify:** long multi-tile drag paints continuously with no stutter; pausing mid-drag still flushes within ~1 drain tick; one Ctrl+Z reverts the whole stroke.

### Phase 2 — Flicker-free live rebuild during drain (DEFERRED DISPOSE)

**Files (own):** `Assets/WorldPainter/Runtime/WorldPainter.SurfaceLayers.cs` (deferred-dispose queue + `RebuildGrassLayerDeferred`), `WorldPainterSculptTool.Stroke.cs` (drain rebuild call), `WorldPainterSculptTool.cs` (tick deferred-dispose in `OnEditorUpdate`).

**CRITICAL constraint (discovered mid-impl):** a prior per-frame in-stroke rebuild was REMOVED (see `WorldPainterSculptTool.cs:117-131` `OnEditorUpdate` comment) because `GrassGpuEngine.Dispose()` calls `argsLodN.Release()` while a player-loop-deferred `Graphics.RenderMeshIndirect` draw is still pending → reads freed buffer → black-square flicker. `GrassCpuEngine` (RenderMeshInstanced, transient matrices) is flicker-free; `GrassGpuEngine` (the Editor's GPU-capable tier pick) is the flicker source. True in-place density update is impossible (a density change alters which blades exist → requires re-bake). So live preview MUST rebuild the engine — the fix is to rebuild without the dispose race.

**Decision (user-approved): deferred engine disposal**, implemented entirely in the non-frozen `SurfaceLayers.cs` — the frozen `GrassGpuEngine` is NOT edited.

1. **Deferred-dispose queue** — add `pendingGrassDispose` (list of `(IGrassEngine engine, GrassTileScatterLayer adapter, int framesLeft)`).
2. **`RebuildGrassLayerDeferred(GrassLayer)`** — mirrors `RebuildGrassLayer` but routes the OLD engines/adapters to the pending queue (with `framesLeft=2`) instead of immediate `Dispose()`/`DestroyImmediate`. New engines are built + submitted normally; the old engine's buffer stays valid until its pending draw flushes.
3. **`TickDeferredScatterDispose()`** — decrement `framesLeft`; `Dispose()`+destroy at 0. Called each editor frame from `WorldPainterSculptTool.OnEditorUpdate` (already ticks the encoders).
4. **`FlushDeferredScatterDispose()`** — dispose all pending immediately; called from `DisposeSurfaceLayers()` and stroke teardown so nothing leaks on Map swap / final rebuild.
5. **Drain call** — each drain (~15 Hz): pop buffered stamps → density compute → **sync-flush touched-tile density RTs** (`FlushAllDensityRTs`, so `GetTileDensity` reads fresh) → `painter.RebuildGrassLayerDeferred(activeGrassLayer)`. Resolve the active layer via `BrushToolTargets`/`WorldPainterState`.
6. **MouseUp** keeps the existing single full `RebuildScatterPreview()` (clean final state; one rebuild never flickers).
7. **Per-tile rebuild** remains an optional later optimization — per-LAYER deferred rebuild is correct + simple first (KISS); only scope to touched tiles if profiling shows the layer rebuild is still too heavy.

**Verify:** long drag shows continuous live grass under the cursor with NO black-square flicker (Scene + Game + Inspector); pending engines disposed within ~2 frames (no buffer leak — check `GraphicsBuffer` count stable across a stroke); final state matches a full rebuild.

**Scope note:** this phase touches ONE runtime file (`SurfaceLayers.cs`) — a deliberate change from the plan's original "editor-only" scope, approved because flicker-free live preview is otherwise unachievable. No engine/shader edits; no build/runtime-behaviour change outside the new editor-driven preview path (the new methods are only invoked from editor code).

### Phase 3 — Click terrain → select WorldPainter

**Files (own):** `Assets/WorldPainter/Editor/Brush/WorldPainterTerrainRaycast.cs` (new shared helper), `WorldPainterSculptTool.Stroke.cs` (delegate to helper — SSOT), `Assets/WorldPainter/Editor/WorldPainter/WorldPainterTerrainPicker.cs` (new, `[InitializeOnLoad]`).

1. **Extract SSOT raycast helper** — move the analytic terrain surface intersection (`TryMapSurfaceHit`, `TryInlineTilesSurfaceHit`, the painting-space conversion in `TryGetBrushWorldPoint`) into a new internal static `WorldPainterTerrainRaycast` class. Have `WorldPainterSculptTool` call it (no behaviour change to the brush). This avoids duplicating the raycast in the picker.
2. **Persistent picker** — new `[InitializeOnLoad]` static class `WorldPainterTerrainPicker`; static ctor subscribes a handler to `SceneView.duringSceneGui` (independent of the inspector lifetime). Cache the scene's `WorldPainter` (refresh via `FindObjectsByType` on hierarchy change / when null).
3. **Gate (all must hold):** a `WorldPainter` exists in scene; `!WorldPainterState.PaintModeActive`; `Tools.current` is `Tool.View` or `Tool.None` (default-tool-only); left mouse-down, no alt.
4. **Pick order:** on `MouseDown`, first call `HandleUtility.PickGameObject(e.mousePosition, false)` — if it returns a non-null GameObject (a real object under cursor), **do nothing** (let Unity handle it; real objects keep priority). Else run `WorldPainterTerrainRaycast` against the painter; on a terrain hit → `Selection.activeGameObject = painter.gameObject`, `e.Use()`. **Do NOT consume** when there's no terrain hit (empty-space click must still deselect normally).
5. Suppress while a brush stroke is active (defensive: check the sculpt tool isn't mid-stroke / paint mode off already covers this).

**Verify:** with WorldPainter deselected and View tool active, clicking the terrain selects the WorldPainter; clicking a real object selects that object; clicking empty sky deselects; with Move/Rotate/Scale active or paint mode on, terrain click does nothing special.

### Phase 4 — EditMode tests + manual verify

**Files (own):** `Assets/WorldPainter/Tests/Editor/WorldPainterStampBufferTests.cs` (new), `WorldPainterTerrainRaycastTests.cs` (new).

1. **`StrokeStampBuffer`** — enqueue N, drain pops all in FIFO order, count/clear semantics, empty-drain is a no-op.
2. **Drain-equivalence (pure math)** — assert the buffered path emits the same stamp positions for a given drag polyline as direct `stroke.Advance` (reuse `WorldPainterStroke.CountStamps`); guards the "identical final density" invariant at the math level.
3. **`WorldPainterTerrainRaycast`** — flat single-tile: known ray hits known surface point; off-terrain ray misses; painting-space conversion under a non-identity root transform maps correctly (reuses root-transform invariants).
4. **Run** `WorldPainter.Tests` via Unity Test Runner (`run_tests` EditMode); zero failures gate.
5. **Manual verify checklist** (Scene view): long drag smoothness, mid-drag pause flush, undo-per-stroke, click-to-select matrix from P3, no regression to height/splat/prop strokes.

## Risk Assessment

| Risk | L (1-5) | I (1-5) | Score | Mitigation |
|------|------|------|------|------------|
| Buffered drain changes final density vs per-stamp path | 2 | 5 | 10 | Drain-equivalence test (P4.2) + identical-result invariant; reuse same `stroke.Advance` math, only retime dispatch |
| `EditorApplication.update` drain subscription leaks past stroke end | 2 | 3 | 6 | Register on Begin, unregister in `TeardownActiveStroke` + on tool Disable; idempotent `-=` before `+=` |
| Per-layer rebuild still too heavy on dense scenes (lag persists) | 2 | 4 | 8 | Per-layer deferred rebuild first; scope to touched tiles only if profiled |
| Black-square flicker re-introduced (GPU engine dispose races pending RenderMeshIndirect) | 3 | 5 | 15 | **Deferred dispose** (hold old engine `framesLeft=2`, dispose on later tick) — the approved core of P2; verify no flicker in Scene+Game+Inspector during a long stroke |
| Deferred-dispose queue leaks GraphicsBuffers (engine never disposed) | 2 | 4 | 8 | `FlushDeferredScatterDispose()` on teardown/Map-swap; assert buffer count stable across a stroke in verify |
| Persistent picker swallows legitimate clicks (deselect/gizmo) | 3 | 4 | 12 | Defer to `HandleUtility.PickGameObject` first; only `e.Use()` on confirmed terrain hit; default-tool + not-painting gate |
| Raycast extraction changes brush hit behaviour | 2 | 4 | 8 | Pure move to static helper, brush calls same code; covered by existing brush use + P4.3 tests |
| `[InitializeOnLoad]` picker active in scenes without WorldPainter | 2 | 2 | 4 | Early-out when no `WorldPainter` found; cache + refresh cheaply |

No score ≥ 15. Highest (12) — picker click-stealing — mitigated by pick-order + strict gate; covered by P3 verify matrix.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| P1: Stamp buffer + drain scheduler | M (~3d) | Core retiming; most subtle (drain lifecycle, undo/seam preservation) |
| P2: Scoped scatter rebuild | S (~1d) | Mostly wiring `RebuildGrassLayer`; stretch per-tile only if profiled |
| P3: Terrain-pick selection | M (~2-3d) | Raycast extraction (SSOT) + persistent hook + gate matrix |
| P4: Tests + verify | S (~1d) | Buffer + raycast EditMode tests; manual matrix |
| **Total** | **~M-L (1 wk)** | Critical path: P1 → P2 (rebuild depends on drain); P3 independent, can parallelize |

## Conventions / guards

- Unity C#: `this.` prefix, camelCase private fields, `#nullable enable` in new files (`code-conventions-unity.md`).
- Editor-only — all new files under `Assets/WorldPainter/Editor/` or `Tests/Editor/`; no runtime asmdef changes except the optional P2.3 stretch entry point in `WorldPainter.SurfaceLayers.cs` (runtime, behind measured need).
- Verify-once batch-compile (`ai-velocity-batch-compile-unity.md`): implement each phase's files, then one `refresh_unity(force, scripts)` + `read_console` + `run_tests` pass.
- Painting space: all stamp positions and raycast results are root-LOCAL; preserve the existing root-transform conversions (`worldpainter-root-transform-painting-space` memory).

## Cook handoff

`/t1k:cook plans/260617-worldpainter-smooth-grass-terrain-pick/plan.md`
