# Brainstorm — GrassInteract debug gizmos + Play-mode trample diagnosis

Date: 2026-06-01 · Project: GrassInteract (Unity 6, URP 17.3, Mono)

## Problem
- Effector trample-folds grass in **edit mode** (verified prior session) but in **Play mode** the orbiting effector leaves no trail.
- Need debug visualization: interactor footprint + effector orbit gizmos, and a live view of the trample RT to separate data-path failure from shader failure.

User-confirmed: repro is **Play mode**; debug depth = **gizmos + live trample-RT overlay**.

## Leading hypothesis (Play-mode-specific)
`EditorSceneManager.playModeStartScene` is set to `Assets/0.Game/Scenes/Demo.unity` (memory note) — pressing Play loads a scene with **no grass field**, so nothing reacts. The overlay confirms instantly via active-scene-name readout + empty/absent trample map.

Secondary candidates (check only if scene is correct):
- First-frame script-order race: `GrassTrampleMap.LateUpdate` runs before `GrassInteractField.Rebuild` binds `_GrassFieldRect` → splat collapses to world-origin for one frame (self-heals next frame).
- Recovery fade (`recoveryPerSecond=1.5`) masking a thin fast-moving trail.

## Design — additive, no change to the working trample/render hot path

1. **`GrassInteractor` gizmos** (`Runtime/GrassInteractor.cs`)
   - `OnDrawGizmos`: XZ wire disc at `WorldPosition`, radius `worldRadius`, color green→red by `strength` (`Handles.DrawWireDisc`, `#if UNITY_EDITOR`).
   - `OnDrawGizmosSelected`: translucent filled disc + vertical stalk + `Handles.Label` (`r=…, strength=…`).

2. **`GrassInteractDemoEffector` gizmos** (`Demo/GrassInteractDemoEffector.cs`)
   - Orbit ring (`radius` at `height`) + center marker, to see the swept path vs. the field rect (field already draws its rect in `OnDrawGizmosSelected`).

3. **Trample-RT overlay folded into `GrassTrampleMap`** (`Runtime/GrassTrampleMap.cs`)
   - New serialized `bool debugDrawOverlay`. When on, `OnGUI` `GUI.DrawTexture`s the live `_GrassTrampleMap` into a 256² screen corner with a readout: **active scene name**, bound `_GrassFieldRect`, registered interactor count, trample-RT max. Works in the **Play-mode Game view** — the crux diagnostic.
   - Interpretation: moving hot disc but upright grass → shader-read bug; black/absent overlay or scene name `Demo` → scene-load issue.

4. **Diagnose + fix Play-mode issue live** using the overlay. If `playModeStartScene` is the cause, set it to null (or the grass demo scene); optionally add a `Tools/GrassInteract/Clear Play-Mode Start Scene` menu item so it can't silently return.

## Rationale
- **Reusability:** gizmos on the reusable `GrassInteractor`; overlay usable in any field.
- **Surgical:** all additive — cannot regress working edit-mode behavior.
- **Testable:** makes the invisible trample data path observable (the reason the prior "resolved" fix didn't stick).

## Next step
Hand to `/t1k:cook` — implement gizmos + folded overlay, then drive the live editor (instance `GrassInteract@de203215`, port 6401; `set_active_instance` first) to diagnose and fix the Play-mode failure.
