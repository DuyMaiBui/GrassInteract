# Phase 4 (D) — Placement Tool: transform-like `EditorTool` + per-instance inspector

Effort: **L** · Blocked by: Phase 1 (PhysicMaterial payload) + Phase 2 (preview/scheduler) · Parallel-safe with Phase 3, Phase 5

## Goal

A Transform-tool-style placement editor (`InstancePlacementTool : EditorTool`) active for
`InstanceScatterLayer`. Place / Select+Transform / Erase authored records, with move-rotate-scale
`Handles` writing back to the selected record. Per-instance inspector exposes collider config +
**PhysicMaterial override** (Phase 1 payload). All edits flow through the working list with Undo and the
Phase 2 scheduler.

## Reuse check

`AuthoredInstancesData` working-list edit API (`WorkingList`, `AddRecord`, `RemoveRecordSwapPop`,
`TryGetRecord`/`SetRecord`, `SetColliderConfig` [extended in Phase 1 with matRefIdx], `EnsureObjectRef`,
`PackBlob`) already exists. `InstanceScatterLayer.PlaceSpacing`, `DefaultColliderMesh/Convex/Scale`, and
(Phase 1) `DefaultColliderMaterial` exist. `ScatterRebuildScheduler.MarkDirty` (Phase 2) re-scatters live.
`ScatterGizmos` (Phase 5) draws dots/normals/AABB/TRS handles.

## File ownership

### Created (all under `Assets/GrassInteract/Editor/`)
- `InstancePlacementTool.cs` — `[EditorTool("Instance Placement", typeof(InstanceScatterLayer))]` (confirm context object during cook; mirror Phase 3 choice):
  - Modes (enum, exposed in overlay): **Place** / **Select+Transform** / **Erase**.
  - **Place** — raycast surface; honor `PlaceSpacing` (reject a stamp within spacing of an existing record); surface-snap; optional align-to-normal; optional random yaw/scale. `AddRecord` to working list.
  - **Select+Transform** — click-pick nearest record (screen-space distance to projected positions); draw `Handles.PositionHandle`/`RotationHandle`/`ScaleHandle` (or `TransformHandle`) at the selected record; on change, write TRS back via `SetRecord`. This is the "like the Transform tool" requirement.
  - **Erase** — brush radius; `RemoveRecordSwapPop` for records inside the radius.
  - Undo: `Undo.RegisterCompleteObjectUndo(authoredInstancesData, "Edit Instances")` before a mutation batch; `EditorUtility.SetDirty` + `PackBlob` after.
  - After any mutation → `ScatterRebuildScheduler.MarkDirty(field, layerIdx)` (debounced live re-scatter).
- `InstancePlacementToolOverlay.cs` (or inline `[Overlay]`) — plain IMGUI/UI-Toolkit, NO Odin:
  - Mode selector; Place options (align-to-normal, random yaw/scale ranges, spacing readout); Erase brush size.
  - **Selected-instance inspector section** (shown when a record is selected):
    - collider toggle (`generateCollider`), convex (`colliderConvex`), `colliderScale`, mesh override (object field → `EnsureObjectRef`), **PhysicMaterial override** (object field → `EnsureObjectRef`, stored as `colliderMaterialRefIndex`; clear → -1 = layer default).
    - Writes via `SetColliderConfig(idx, generate, convex, scale, meshRef, matRef)` (Phase 1 extended signature).

### Consumed (do not create)
- `ScatterGizmos.cs` (Phase 5) — instance dot, normal, AABB, TRS handles.
- `ScatterRebuildScheduler.cs` (Phase 2) — `MarkDirty`.
- Phase 1 `AuthoredInstancesData.SetColliderConfig` with the `colliderMaterialRefIndex` parameter.

## Constraints

- Unity `EditorTool` + `Overlays` + `Handles` only; plain IMGUI/UI-Toolkit panel. NO Odin.
- Unity-stdlib only (PhysicMaterial, EditorTool, Handles, Undo). Genre-neutral name `InstancePlacementTool`.
- Edits operate on the `AuthoredInstancesData` working list, then `PackBlob` + `SetDirty`; Undo on the sidecar SO.
- TRS handles + bounds for the selected record; unselected records draw as dots/normals (shared `ScatterGizmos`).

## Risk table

| Risk | L | I | Score | Mitigation |
|------|:-:|:-:|:-:|------------|
| Picking against 50k records is slow (per-frame screen-space loop) | 3 | 3 | 9 | Limit pick to records within camera frustum / a max radius of the cursor ray; coarse spatial reject before exact distance. |
| Working-list edit not persisted (forgot `PackBlob`/`SetDirty`) → loss on reload | 3 | 4 | 12 | Always `PackBlob` + `EditorUtility.SetDirty` after a mutation batch; Undo entry wraps the batch; manual reload test in success criteria. |
| PhysicMaterial override path desync with Phase 1 codec | 2 | 4 | 8 | Hard-depend on Phase 1 (matRefIdx field + extended `SetColliderConfig`); do not start D until A's tests pass. |
| Transform handle writes fight the edit-mode preview tick (jitter) | 3 | 2 | 6 | Apply handle delta on `EndChangeCheck`; MarkDirty (debounced) rather than rebuild per drag-frame. |

## Success criteria (manually validated in-editor — no automated editor-UI test)

- Place mode stamps records honoring `PlaceSpacing`; new instances appear in the live preview within ~150 ms.
- Select+Transform: clicking a record selects it; move/rotate/scale Handles edit it and the change persists (re-scatter shows it; survives domain reload after save).
- Erase mode removes records under the brush; preview updates.
- Selected-instance inspector toggles collider/convex/scale and assigns a mesh override AND a **PhysicMaterial override**; in Play mode the spawned `MeshCollider.sharedMaterial` matches the override (Phase 1 wiring).
- Every state draws its gizmo (selected = TRS + AABB; unselected = dots/normals), all from one `ScatterGizmos`.
- Undo reverts an edit batch in one step.
