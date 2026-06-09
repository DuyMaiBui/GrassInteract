# Phase 2 (B) — Preview Driver: edit-mode full render + debounced rebuild

Effort: **M** · Blocked by: nothing · Blocks: Phase 3 (live feedback), Phase 4 (live feedback), Phase 5 (field-bounds gizmo host)

## Goal

Drive the **real** `InstancedPropEngine` / CPU-GPU `Submit` in edit mode so the Scene view shows WYSIWYG
preview (requirement #2 full-engine render). Every settings change re-scatters the affected layer —
**debounced ~150 ms** via a single shared scheduler (requirement #3), never a full `Rebuild()`.

This is the highest-risk foundation: the edit-mode loop must NOT spin the editor.

## Reuse check

EXTENDS `ScatterField` (currently `LateUpdate` is Play-mode-guarded → no edit-mode draw). `RebuildLayer(idx)`,
`StepAll`, `SubmitAll`, `BuildContext` already exist. `ScatterRebuildScheduler` is NEW (editor-only SSOT debounce).

## File ownership

### Modified
- `Assets/GrassInteract/Runtime/ScatterField.cs`
  - Expose the existing private drivers to the editor companion **without adding `UnityEditor` usage**: change `StepAll`, `SubmitAll`, and the engine list access from `private` to `internal` (or add `internal` edit-mode entry points `EditorStep(float dt)` / `EditorSubmit(Camera)`), so the editor companion can call them. Keep `RebuildLayer(int)` public (already is).
  - Do NOT add an edit-mode `EditorApplication` subscription here — that lives in the editor companion to keep runtime free of `UnityEditor`.

### Created (all under `Assets/GrassInteract/Editor/`)
- `GrassInteract.Editor.asmdef`
  - `"name": "GrassInteract.Editor"`, `"references": ["GrassInteract"]`, `"includePlatforms": ["Editor"]`, `"autoReferenced": false`, `rootNamespace` `GrassInteract.Editor`. Add `UnityEditor.CoreModule` if needed (usually implicit for Editor-platform asmdefs).
  - NOTE: after creating an asmdef-only file, force a recompile (`refresh_unity(force, all)` or touch a `.cs`) — asmdef-only edits can no-op (`ai-velocity-batch-compile-unity.md`).
- `ScatterFieldEditorTick.cs` — `[InitializeOnLoad]` static or a `ScriptableSingleton` driving the edit-mode loop:
  - Subscribes `EditorApplication.update`.
  - Clock: `dt = (float)(EditorApplication.timeSinceStartup - lastTime)` clamped (avoid huge first-frame dt).
  - **GATE (HIGH-risk mitigation):** only tick when `previewEnabled` is true AND (a Scatter `EditorTool` is the active tool OR a `ScatterField` is in `Selection`). Otherwise no Step/Submit/Repaint.
  - On tick: for each enabled `ScatterField`, `field.EditorStep(dt)` + `field.EditorSubmit(SceneView.lastActiveSceneView?.camera)` + `SceneView.RepaintAll()` — but only call `RepaintAll()` when a draw actually happened (guard the spin).
  - `previewEnabled` toggle persisted via `EditorPrefs` (per-project key). Default **OFF**.
  - `previewColliders` toggle (drives whether the edit-mode path spawns the `InstanceColliderPool` GOs) — default **OFF** (no 50k GO spawn while authoring). Lives here or on the scheduler; expose to inspectors.
- `ScatterRebuildScheduler.cs` — editor-only static SSOT debounce:
  - `MarkDirty(ScatterField field, int layerIdx)` — record `(field, layerIdx, EditorApplication.timeSinceStartup)`.
  - On `EditorApplication.update`, when `now - lastMarkTime >= DEBOUNCE_SECONDS` (≈0.15), flush each dirty `(field, layerIdx)` → `field.RebuildLayer(layerIdx)`, then `SceneView.RepaintAll()`.
  - `const double DEBOUNCE_SECONDS = 0.15;` (named constant, no magic literal).
  - Coalesces multiple marks on the same layer into one rebuild. All three tools + inspectors call `MarkDirty` only — never `Rebuild()` / `RebuildLayer()` directly.
- `ScatterFieldEditor.cs` (custom inspector for `ScatterField`) — plain IMGUI/UI-Toolkit, NO Odin:
  - Preview toggle (`previewEnabled`), preview-colliders toggle, "Rebuild Now" button (calls `Rebuild()` once, escape hatch).
  - `OnInspectorGUI` field edits → `ScatterRebuildScheduler.MarkDirty` (not direct rebuild).
- Layer inspectors (`InstanceScatterLayerEditor.cs`, `DensityScatterLayerEditor.cs`) — plain IMGUI, route `OnValidate`/edit through the scheduler. (Runtime `OnValidate` must NOT call `Rebuild()`; if a runtime `OnValidate` exists it stays editor-agnostic — the scheduler is driven from the editor side.)

## Constraints

- Unity `EditorTool`/`Overlays` not required here (this phase is the driver + inspectors). Plain IMGUI/UI-Toolkit. NO Odin in editor.
- Runtime `ScatterField.cs` gains NO `UnityEditor` reference — only visibility changes (`private → internal`).

## Risk table

| Risk | L | I | Score | Mitigation |
|------|:-:|:-:|:-:|------------|
| Edit-mode clock + `RepaintAll` spins the editor (busy loop, fan, battery) | 4 | 4 | 16 | **HIGH** — tick gated behind `previewEnabled` AND (active Scatter tool OR selected ScatterField); `RepaintAll` only when a draw happened; `previewColliders` OFF by default. Verify CPU idle when preview off and when nothing selected. |
| GPU indirect path differs in edit mode vs play | 2 | 3 | 6 | Same `SubmitAll` drives both; CPU fallback on GPU self-test fail already exists; `forceTier=ForceCpu` documented authoring fallback. |
| 50k re-scatter on each keystroke stalls editor | 4 | 3 | 12 | `ScatterRebuildScheduler` 150 ms debounce + per-layer `RebuildLayer`; never full `Rebuild()` from edits. |
| First-frame huge `dt` from `timeSinceStartup` delta | 3 | 2 | 6 | Clamp `dt` to a max (e.g. 0.1 s) and skip the first tick after subscribe. |

## Success criteria (manually validated in-editor — no automated editor-UI test)

- With preview ON and a `ScatterField` selected, the Scene view renders the scatter engine output (grass/props visible) in edit mode without entering Play.
- With preview OFF (or nothing selected), the editor is idle — no continuous repaint, CPU at rest (verify via Editor responsiveness / no constant SceneView repaint).
- Editing any layer field re-scatters that layer within ~150 ms (one rebuild, not per-keystroke), with no manual rebuild click.
- `previewColliders` OFF → no `InstanceCollider` GameObjects spawned while authoring; ON → colliders appear.
- Runtime `ScatterField.cs` contains no `using UnityEditor` (grep clean).
