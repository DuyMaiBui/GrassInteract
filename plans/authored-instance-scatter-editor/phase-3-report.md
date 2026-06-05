# P3 Report — Edit Brush

## Status: shipped, gate ✅ (compile + harness + render). Interactive throughput/visual smokes deferred to user.

## What compiled

Files edited (subagent + main loop):
- `Assets/GrassInteract/Editor/ScatterBrush.cs` — added `BrushEditOp` enum (RandomizeRotation / NudgeScale / NudgePosition / ToggleAlignNormal) + `EditBrushStamp(...)` method. Reuses existing falloff sampling; per-stamp commit via batched `SetRecords` call.
- `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` — replaced EditBrush stub HelpBox with: op-picker toolbar, per-op param block (scaleDelta float for NudgeScale, nudgeRadius float for NudgePosition), OnSceneGUI hook calling EditBrushStamp on mouse-down/drag. Tooltip: "Modifies authored records only — does NOT paint density mask."
- `Assets/GrassInteract/Runtime/AuthoredInstancesData.cs` — added `public void SetRecords(IList<(int idx, InstanceRecord rec)> edits)` for batched commit (avoids per-record dirty-flush in tight stamps).

Compile: **CLEAN.** `refresh_unity(force, scripts, compile=request)` then console — 0 project errors. Only pre-existing Firebase warning (Android resource generation, unrelated) + MCP transport "Cannot access a disposed object" (not project code).

## Harness results

`ScatterFieldRebuildLayerHarness` (substitute for missing `ScatterInstanceCullHarness`):
- Initial attempt failed because Unity MCP was routed to the wrong project instance (UnityGrassSuffer@bdf2dcc9 on port 6402, NOT GrassInteract@de203215 on port 6403).
- After pinning via `set_active_instance GrassInteract@de203215`: menu fired clean, 0 `[Parity]` ERROR lines.

## Verification

| Gate | Result | Notes |
|---|---|---|
| Compile clean | ✅ | 0 project errors, 0 new warnings beyond pre-existing Firebase + MCP transport |
| Harness (RebuildLayer Parity) | ✅ | 0 errors after instance pin |
| Game-view render (functional substitute) | ✅ | `screenshots/phase-3-render.png` — dense grass + interactor sphere, identical look to P1 |
| Throughput smoke (rotate 1000 inst <50 ms/stroke) | DEFERRED | Needs interactive stamp on an authored sidecar; not scriptable via MCP. **User action:** select demo layer, paint a 1000-inst patch in Place mode, switch to EditBrush + RandomizeRotation, single-stamp over the patch, observe console timing if instrumented. |
| Visual EditBrush ops (rotation/scale/position/align) | DEFERRED | Same — needs interactive cursor. |
| Per-stroke Undo | DEFERRED | Same. |

## Critical gotcha discovered — MULTI-INSTANCE MCP ROUTING

The host has **two Unity editors running simultaneously**:
- `UnityGrassSuffer@bdf2dcc9` (port 6402) — different project
- `GrassInteract@de203215` (port 6403) — this project

The P3 subagent's `execute_menu_item` and `manage_camera screenshot` ran against UnityGrassSuffer (visible: pickup-truck scene returned instead of grass; screenshot fullPath was `C:/Works/Unity/The1/GrassSuffer/UnityGrassSuffer/...`).

**Fix (also propagate to P4/P5 spawn brief):** every spawn brief must include `mcp__UnityMCP__set_active_instance instance=GrassInteract@de203215` as the first MCP call, OR pass `unity_instance="GrassInteract@de203215"` (or `unity_instance="de2"` prefix) on every individual MCP tool call. The skill default is "no pin" so multi-instance routing is non-deterministic per call.

Earlier P1/P2 succeeded by luck (the active default happened to be GrassInteract at session-start time). P3 hit the failure when the routing flipped between agent invocations.

## Subagent budget

- P3 agent (ae8c927ae3f2d842a): 131,848 tokens, stalled at "menu path may be wrong — let me verify" before doing the verification. Main loop finished: diagnosed the multi-instance issue, pinned, re-ran harness + screenshot, wrote report.

## Files for P4

Existing:
- `Runtime/GrassScatter.cs` — add `if (layer.HasAuthoredInstances) → feed from sidecar` skip-path.
- `Runtime/ChunkedInstanceBuffer.cs` — add override-mask bit in stride.
- `Runtime/MeshScatterEngine.cs` — group-by-material draw split when any instance has renderer override.

NEW:
- `Assets/GrassInteract/Editor/ChunkInstanceLayoutVerify.cs` — byte-stability harness (FIRST task of P4; asserts overrideMask=0 produces byte-identical output to procedural baseline).

## Open items / risks for P4

- **Highest-risk phase** (score 20 in the plan). Byte-layout change in `ChunkedInstanceBuffer` MUST be gated by the new harness from line 1.
- **Multi-instance pin** must be the first step of the P4 spawn brief.
- **`ScatterInstanceCullHarness`** still missing from disk. P4 should re-create it (or fold its semantics into `ChunkInstanceLayoutVerify`).
