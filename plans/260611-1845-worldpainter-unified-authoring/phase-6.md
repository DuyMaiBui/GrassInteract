# Phase 6 — Polish & Discoverability

**Effort:** M · **Blocked by:** P1–P5 · **Blocks:** —

## Goal

The premium-feel and beginner-discoverability layer (design §4.5–§4.7, §6): USS animations, live readout strip, header mini-map, perf badge, Overlays-API scene HUD + radial scrub + hotkeys + eyedropper + symmetry, coach marks, empty states, cheat-sheet popover. No new payload types — this is UX/perf-instrumentation polish over the finished P1–P5 engine.

## File ownership (concrete paths)

### New
| Path | Responsibility | ≤200 lines |
|---|---|---|
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLiveReadoutStrip.cs` | height histogram / density heatmap / instance counts animating during stroke (async GPU counters) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterMiniMap.cs` | header mini-map of loaded tiles + camera dot | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterPerfBadge.cs` | draw calls · dispatches · instance count · VRAM via `rendering_stats`/`ProfilerRecorder` | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterSceneOverlay.cs` | `UnityEditor.Overlays` toolbar: active-layer chip + LOD0 mini-thumb, size readout, mode dot | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterSceneInput.cs` | radial scrub, Shift-inverse, Alt-eyedropper, Ctrl-smooth, symmetry, active-layer label at cursor | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterCoachMarks.cs` | first-selection-per-layer tips (`EditorPrefs`-gated) + `?` cheat-sheet popover + empty states | yes |

### Modified
| Path | Change |
|---|---|
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainter.uss` | 120ms ease on expand/collapse, tab-underline slide, hover card elevation |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterInspector.cs` (P1) | mount readout strip / mini-map / perf badge / coach marks into the fixed header/footer zones |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterBrushDock.cs` (P1) | brush-stamp 72×92 wrap-grid with 2px selected border; preset slots |

### Reuse unchanged (cite)
`ScatterBrushPreview.cs` (brush gizmo decal), `InstanceGhostPreview.cs`, `AnchorPreviewPanel.cs` (LOD0 mini-thumb), `TerrainValidationSceneBuilder.cs` (empty-state "Create 1×1 tile"), async GPU counter pattern (P3), `rendering_stats` MCP / `ProfilerRecorder`.

## Tasks (each with verify-check)

1. **USS animations** — 120ms ease expand/collapse, tab-underline slide, hover elevation. → verify: transitions render smoothly; no layout jump.
2. **Live readout strip** — histogram/heatmap/counts on the 0.15s async tick (never CPU recount, design §6). → verify: readouts animate during stroke; zero per-frame readback stall (profile).
3. **Header mini-map** — loaded tiles + camera dot. → verify: dot tracks scene camera; resident tiles highlighted.
4. **Perf badge** — draw calls/dispatches/instances/VRAM. → verify: numbers match `rendering_stats`; updates on the tick, not per-repaint.
5. **Scene Overlay HUD** — `UnityEditor.Overlays` toolbar (active-layer chip + LOD0 thumb, size, mode dot). → verify: layer switch from the scene overlay works without leaving the viewport.
6. **Scene input** — radial scrub (mod+drag: H=size, V=strength), Shift-inverse, Alt-eyedropper, Ctrl-smooth, symmetry mirror, cursor label (design §4.5/§5.5). → verify: each modifier behaves per spec; eyedropper samples layer/biome under cursor.
7. **Onboarding** — empty states (no tiles / empty stack), per-layer coach marks (`EditorPrefs`-gated once), `?` cheat-sheet popover (design §4.7). → verify: empty scene shows "Create 1×1 tile"; first grass-layer select shows one tip, never again.
8. **Brush-stamp grid + presets** — 72×92 wrap-grid, 2px blue selected border; F1–F3 preset slots, X=swap. → verify: stamp selection highlights; preset recall restores `BrushSettings`.

## Risk Assessment (L×I)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Live readouts / perf badge re-read GPU per repaint → inspector stall | 3 | 4 | 12 | All counters on the 0.15s async tick (`AsyncGPUReadback`); never CPU recount; profile idle repaint = no readback |
| Overlays-API / scene-input hotkeys conflict with Unity defaults | 2 | 3 | 6 | Scope shortcuts to the active tool; document in cheat-sheet; honor `unity-forbidden-operations` |
| Polish scope creep delays ship | 3 | 3 | 9 | Each task independently shippable; cut from the tail without breaking P1–P5 engine |

## Test plan

- `run_tests`: full suite stays green (UX layer adds no SSOT change).
- New (where unit-testable): histogram/heatmap data mapping, preset save/recall round-trip, coach-mark `EditorPrefs` gate.
- Manual: animation smoothness, scene overlay layer-switch, all modifiers, empty-state and coach-mark first-run.
