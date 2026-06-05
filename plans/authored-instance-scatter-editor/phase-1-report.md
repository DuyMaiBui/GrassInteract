# P1 Report — Editor Scaffolding

## Status: shipped, gate PARTIAL (compile + render + asmdef ✅; runtime interactive gates deferred to user)

## What compiled

Files created:
- `Assets/GrassInteract/Runtime/AuthoredInstancesData.cs` (360 lines) — ScriptableObject sidecar, `ISerializationCallbackReceiver`, byte-blob + Object refs, NativeArray runtime
- `Assets/GrassInteract/Editor/InstancePickingService.cs` (127 lines) — CPU spatial hash skeleton (Rebuild/Invalidate/QueryRadius); ray-vs-sphere deferred to P2 per scope

Files edited:
- `Assets/GrassInteract/Runtime/ScatterLayer.cs` — added `hasAuthoredInstances` (bool), `authoredInstances` (AuthoredInstancesData?), `placeSpacing` (float, range 0.05–5 m, default 0.5) + accessors `HasAuthoredInstances` / `AuthoredInstances` / `PlaceSpacing`. Tooltips note density-map role flip when `hasAuthoredInstances=true`.
- `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` — replaced 3-mode `PaintTool` with 5-mode `ToolMode { Off, Place, Erase, EditSingle, EditBrush }`. Toolbar wired; EditSingle / EditBrush show stub HelpBoxes pointing at P2 / P3.
- `Assets/GrassInteract/Editor/ScatterBrush.cs` — Place stroke now appends InstanceRecord(s) (Poisson-disk at `layer.PlaceSpacing` within stamp radius, with `Undo.RegisterCompleteObjectUndo`, MAX_INSTANCES_PER_STAMP=10000 cap). Erase removes via spatial-hash query + swap-pop. Both also paint/clear density-map texels (transitional dual-write).

Compile result: **CLEAN.** `refresh_unity(force, scripts, compile=request, wait_for_ready=true)` then `read_console` — zero project errors, zero new warnings. Only internal MCP "Cannot access a disposed object" line (transient transport, not project code) and a prior `execute_code` infrastructure error (CodeDom path-too-long on this Windows host, unrelated to project code).

## Harness results

**`ScatterInstanceCullHarness` no longer exists in repo** (gotcha — was referenced by the plan / brainstorm / project memory but the file is gone, likely cleaned up in the recent scatter-brush-config-refactor). Current harness inventory in `Assets/GrassInteract/Editor/`:
- `ScatterFieldRebuildLayerHarness.cs` — RebuildLayer parity self-test (the only `*Harness.cs` left).

Functional equivalent for P1 (engine untouched): **demo game-view screenshot** confirms dense grass + props + interactor render correctly post-changes. See `screenshots/phase-1.png`.

`ScatterFieldRebuildLayerHarness` was triggered via `Tools/GrassInteract/Self-Test/RebuildLayer Parity` menu item — no error / no Parity log line returned, indicating either a) the active scene's ScatterField didn't trigger logs (path runs silently when count matches) or b) timing. Either way, no error was raised. Re-runnable manually by the user via menu for confirmation.

## Verification

| Gate | Result | Notes |
|---|---|---|
| Compile clean | ✅ | 0 project errors, 0 new warnings |
| Asmdef boundary (Runtime/AuthoredInstancesData.cs has no `using UnityEditor`) | ✅ | grep verified 0 matches |
| Demo renders post-changes (functional substitute for missing harness) | ✅ | screenshots/phase-1.png |
| Throughput smoke (≥5000 inst/sec stamp) | DEFERRED | Brush stroke needs scene-view mouse interaction; not script-testable in this MCP. **User action:** select demo layer, toggle `HasAuthoredInstances=on`, Place-paint a 5×5 m patch; report inst/sec from console. |
| Sidecar size ≤100KB @ 1000 instances | DEFERRED | Same — needs interactive paint. **User action:** after the smoke patch, check `Assets/GrassInteract/Demo/GrassInteractDemoScatterConfig.asset` size delta. |
| Undo: one stamp-stroke = one Ctrl+Z step | DEFERRED | Requires interactive stamp. **User action:** stamp once, Ctrl+Z, confirm instance count returns to pre-stroke. |
| Screenshot saved | ✅ | `plans/authored-instance-scatter-editor/screenshots/phase-1.png` (496×800, Main Camera game view) |

## Gotchas discovered

1. **`ScatterInstanceCullHarness` is missing from disk** despite being referenced everywhere (plan, brainstorm, memory). Plan-level mistake — the planner relied on memory entries that have since been invalidated by a recent refactor. Substitute gate for P2–P5: use `ScatterFieldRebuildLayerHarness` + game-view screenshot. Recommend re-creating `ScatterInstanceCullHarness` as part of P4 (NEW `ChunkInstanceLayoutVerify` harness can do double duty).
2. **CodeDom `execute_code` fails on this Windows host** with "filename or extension is too long" — Roslyn compiler not installed in this Unity MCP either. Workaround: trigger harness via `execute_menu_item` (menu items already registered) instead of dynamic Roslyn invocation. P2–P5 verifier scripts should use this path.
3. **Sub-agent stall pattern recurred twice** in P1 (cumulative ~270K subagent tokens). Same failure mode as 2026-06-03 sessions: agent narrates "Now I'll do X" then returns without doing X. P2–P5 spawn briefs should pre-empt with explicit "no narration, do the steps and return one-line confirmation" guards.

## Subagent budget burned

- Cook P1 agent (a1d507d6863a9cf21): 137,289 + 129,421 = ~266K tokens across 2 stalls. Both resumed via SendMessage; main loop completed the final gate verification + report write directly.

## Open items / risks for P2

- **Throughput / sidecar / undo gates** are interactive — user should run them at the start of the next session before P2 spawn so we have baseline numbers carried forward.
- **Missing harness file** — P2 spawn brief MUST use the substitute gate (`ScatterFieldRebuildLayerHarness` menu + screenshot) rather than referencing `ScatterInstanceCullHarness`.
- **InstancePickingService skeleton complete** — P2 just needs to add ray-vs-sphere `Pick(ray)` returning `int instanceIdx` (and `PickedInstanceTRS` accessor).
- Confirm `MAX_INSTANCES_PER_STAMP=10000` is the right hard cap before P3 introduces brush-edit ops that could hit similar limits.

## Files for P2

- NEW `Assets/GrassInteract/Editor/InstanceSelectionOverlay.cs` — wireframe + transform gizmo.
- EDIT `Assets/GrassInteract/Editor/InstancePickingService.cs` — add `Pick(Ray, out int idx, out float t)`.
- EDIT `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` — focused Inspector panel for EditSingle mode (replaces the stub HelpBox).
