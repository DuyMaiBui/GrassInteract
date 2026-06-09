# Brainstorm — GrassInteract Production Authoring Toolchain

Date: 2026-06-09 · Status: design approved, ready for `/t1k:plan`

## Problem statement

Runtime is refactored (composed-struct layers, V2 records, `InstancedPropEngine`, `ScatterField` with `Rebuild`/`RebuildLayer`). Commit `af6c69e` deleted the entire `Editor/` suite (~5000 L: `ScatterBrush` 939L density paint, `DensityScenePaintSession`, `InstanceSceneInput` 482L placement, picking/selection services + overlays, `ScatterBakeToAuthored`, custom inspectors, `BrushCursor.shader`). Result: no edit-mode preview, no density paint, no instance placement, no live rebuild on change.

Rebuild the authoring toolchain **cleanly on the new runtime** — do NOT restore deleted files; re-architect on Unity `EditorTool` + `Overlays`.

## Requirements (from user)

1. Each `InstanceLayer` record carries Transform + Collider + **Collider PhysicMaterial**.
2. Edit-mode Scene preview — **full engine render** (WYSIWYG, real engine `Submit`).
3. Every settings change rebuilds rendering immediately — **debounced** (~150 ms idle coalesce).
4. Density paint tool **like Terrain tool**.
5. Instance placement tool **like Transform tool**.
6. All scene tools draw gizmos.

## Resolved decisions

| # | Decision | Choice |
|---|---|---|
| 1 | "Collider material" | **PhysicMaterial** (physics friction/bounce), per-instance override + layer default. NOT render material — RendererOverride stays removed. |
| 2 | Preview fidelity | **Full engine render** in edit mode (drive real `InstancedPropEngine`/CPU-GPU `Submit`). |
| 3 | Rebuild policy | **Debounced** (~150 ms), per-layer `RebuildLayer(idx)`, single shared scheduler. |
| 4 | Tool architecture | **Unity `EditorTool` API + `Overlays` + `Handles`**. |
| 5 | Scope | **All 5 sub-projects approved**; build order A+B → C/D/E. |
| 6 | Tool panel UI | **Plain IMGUI/UI-Toolkit** (no Odin in editor tools; Odin stays on runtime layer inspectors only). |

## Current-state facts (grounding)

- `InstanceRecord` (V2, 36B header / +16B collider block): `position, rotation, scale(uniform), overrideMask` + `[NonSerialized] generateCollider, colliderConvex, colliderScale, colliderMeshRefIndex`. **No material field.**
- `AuthoredInstancesData`: byte-blob (VERSION_BYTE=2) + `List<Object> objectRefs` (collider meshes indexed from blob); working-list editor API already present (`SetColliderConfig`, `EnsureObjectRef`, etc.). V1→V2 migration exists.
- `InstanceColliderPool`: runtime `MeshCollider` GO pool; sets mesh+convex, **no PhysicMaterial**.
- `ScatterField` `[ExecuteAlways]`: `Rebuild()`/`RebuildLayer(idx)` exist; `LateUpdate` Step/Submit **guarded to Play mode only** → no edit-mode draw today.
- `DensityScatterLayer`: readable R8 `densityMap`; `BrushStamp` + `TerrainScatterConfig.brushStamps` exist but inert.

## Decomposition — 5 sub-projects

### A. Data model — PhysicMaterial per instance + layer default  (foundational for D)
- `InstanceRecord`: add `[NonSerialized] int colliderMaterialRefIndex` (-1 = layer default), `objectRefs`-indexed like the mesh.
- Blob: `COLLIDER_BYTES 16→20`; bump `VERSION_BYTE 2→3`; add `UnpackBlobV3`; **V2→V3 migration** (old 16B block → matRefIdx=-1). V1→V2 chain preserved.
- `InstanceScatterLayer`: add `[SerializeField] PhysicMaterial? defaultColliderMaterial` + accessor (mirrors `defaultColliderMesh`).
- `InstanceColliderPool`: set `mc.sharedMaterial = matOverride ?? defaultMaterial`.
- Tests: V3 pack/unpack round-trip; V2→V3 migration matRefIdx=-1; pool material assignment.

### B. Preview driver — edit-mode full render + debounced rebuild  (foundational for C/D feedback)
- `ScatterField` edit-mode tick (editor companion / `#if UNITY_EDITOR` partial): subscribe `EditorApplication.update` → `StepAll(editorDt)` + `SubmitAll(SceneView.camera)` + `SceneView.RepaintAll()`. Clock = `EditorApplication.timeSinceStartup` delta.
- `previewColliders` toggle, **OFF by default** (no 50k GO spawn while authoring).
- `ScatterRebuildScheduler` (editor-only): tools call `MarkDirty(field, layerIdx)`; coalesce → `RebuildLayer(idx)` after ~150 ms idle. Single SSOT debounce shared by all three tools.
- Inspectors + `OnValidate` route through scheduler, never call `Rebuild()` directly.

### C. Density paint — terrain-like `EditorTool`
- `DensityPaintTool : EditorTool`, active for `DensityScatterLayer`. Overlay: size/opacity/falloff/flow + `BrushStamp` picker (reuses existing brush library).
- Raycast surface → write `densityMap` R8 (CPU buffer, `SetPixels`+`Apply`, `Undo.RegisterCompleteObjectUndo`). Stroke end → `MarkDirty` → live re-scatter.
- Modes: Paint / Erase / Smooth. Reuse `DensityScatterLayer.Validate` (readable+R8).
- Gizmo: projected brush disc + falloff ring via `Handles` (replaces `BrushCursor.shader`).

### D. Placement tool — transform-like `EditorTool` + per-instance inspector  (needs A)
- `InstancePlacementTool : EditorTool`, active for `InstanceScatterLayer`. Modes:
  - **Place** — stamp records honoring `PlaceSpacing`, surface-snap, optional align-to-normal, random yaw/scale.
  - **Select+Transform** — click-pick a record; **move/rotate/scale Handles** write back to the record (the "like transform tool" ask).
  - **Erase** — brush removes via swap-pop.
- Selected-instance inspector: collider toggle/convex/scale/mesh override + **PhysicMaterial override** (A payload).
- Edits → `AuthoredInstancesData` working list → `MarkDirty`; Undo on sidecar.
- Gizmo: selected TRS handles + bounds; unselected = dots/normals.

### E. Shared gizmo layer
- `ScatterGizmos` static (`Handles` helpers: brush disc, instance dot, normal, AABB) used by C+D and `ScatterField` field-bounds gizmo. SSOT, no per-tool copies.

## Build order & parallelism
1. **A + B** (sequential-ish, unblock everything) — data model + preview/scheduler.
2. **C, D, E** in parallel after A+B (E is a small shared dep; land its skeleton with whichever of C/D goes first).

## Risks
1. Edit-mode clock/`RepaintAll` can spin the editor → gate behind preview-enabled toggle + only when a Scatter tool is active.
2. Blob V3 migration must preserve V1→V2→V3 chain → round-trip tests.
3. GPU indirect engine in edit mode may differ from play loop → CPU-tier fallback already supported on self-test fail.
4. 50k live re-scatter → debounce + per-layer `RebuildLayer` (async path explicitly deferred).

## Library-quality / decoupling
- Additions are Unity-stdlib only (PhysicMaterial, EditorTool, Handles, Overlays, SceneView) — zero third-party in core/editor. Passes `library-third-party-decoupling`.
- Naming stays genre-neutral (`ScatterGizmos`, `InstancePlacementTool`, `DensityPaintTool`).

## Success criteria
- InstanceLayer record persists+restores PhysicMaterial (per-instance + layer default); runtime collider uses it.
- Editing any layer field redraws the scene preview within ~150 ms, no manual rebuild.
- Density paint writes the density map and re-scatters live; brush disc gizmo tracks the cursor.
- Placement tool places/selects/transforms/erases instances with TRS handles; edits re-scatter live.
- Every scene tool draws its gizmo; one shared `ScatterGizmos` source.
- All new runtime additions unit-tested; preview/tools manually validated in-editor (no automated editor-UI tests required).

## Next step
Hand to `/t1k:plan` for phased breakdown (A+B plan first, then C/D/E).
