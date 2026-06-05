---
phase: 2
name: edit-single
effort: M
agent: t1k-unity-developer
blocked-by: P1
blocks: P3, P4, P5
---

# Phase 2 - Edit Single

## Goal

Click-pick an instance in Scene view -> wireframe overlay + transform gizmo + focused Inspector panel with Transform / Collider / Renderer override blocks. Drag-gizmo writes back to sidecar with per-drag Undo.

## Scope

IN: ray-vs-bounding-sphere picking in InstancePickingService; InstanceSelectionOverlay (Handles.DrawWireMesh + Handles.PositionHandle/RotationHandle/ScaleHandle); focused Inspector panel below the toolbar with three override blocks (Transform always-on; Collider with override checkbox; Renderer with override checkbox).

OUT: brush-edit ops (P3); engine consumption of overrides (P4); migration menu (P5).

## File Ownership

| File | Action |
|---|---|
| Editor/InstancePickingService.cs | EXTEND - add RaycastPick(ray, layer, sidecar): int? - walks cells along ray, tests ray-vs-sphere (r = mesh.bounds.extents.magnitude * scale), returns nearest by t |
| Editor/InstanceSelectionOverlay.cs | CREATE - holds selectedIndex; OnSceneGUI: Handles.DrawWireMesh(layer.LOD0Mesh, TRS) + Handles.PositionHandle/RotationHandle/ScaleHandle at TRS pos |
| Editor/TerrainScatterConfigEditor.cs | EDIT - wire EditSingle toolbar slot; render focused Inspector panel below toolbar when selectedIndex != null |
| Runtime/AuthoredInstancesData.cs | EXTEND - public API: TryGetRecord(idx, out InstanceRecord), SetRecord(idx, InstanceRecord, undoOp), SetColliderOverride(idx, ColliderOverride?), SetRendererOverride(idx, RendererOverride?) |

## Step-by-Step Tasks

1. **Extend InstancePickingService.RaycastPick**: walk cells along ray using 3D-DDA; for each cell, iterate its index list; test ray-vs-sphere with r = layer.LOD0Mesh.bounds.extents.magnitude * record.scale.max(); track nearest t. Skip cells with empty list. Early-out when t exceeds best hit.
2. **OnSceneGUI EditSingle handler in TerrainScatterConfigEditor**: when ToolMode == EditSingle and Event.current.type == MouseDown left-button, cast ray via HandleUtility.GUIPointToWorldRay, call InstancePickingService.RaycastPick, set overlay.selectedIndex.
3. **InstanceSelectionOverlay.OnSceneGUI**: if selectedIndex == null return. Read InstanceRecord at idx, draw wireframe via Handles.DrawWireMesh(mesh, pos, rot, scale * 1.02) with Handles.color = highlight (Color.cyan with alpha). Three gizmo modes: position / rotation / scale; Tools.current selects which (matches Unity convention).
4. **Gizmo drag**: EditorGUI.BeginChangeCheck before Handles.*Handle, EndChangeCheck after; if changed: Undo.RecordObject(sidecar, Move Instance) and AuthoredInstancesData.SetRecord(idx, new TRS).
5. **Focused Inspector panel** (below toolbar in TerrainScatterConfigEditor.OnInspectorGUI when ToolMode==EditSingle && selectedIndex!=null):
   - Header: Instance #{idx} of {total}.
   - Transform block (always editable): Vector3Field for pos, EulerHandles for rot (display Euler internally store Quaternion), Vector3Field for scale.
   - Collider block: Toggle hasColliderOverride; when on: Toggle Enabled, Toggle Convex, ObjectField for ColliderMesh (Mesh asset slot).
   - Renderer block: Toggle hasRendererOverride; when on: ObjectField Material (with warning icon + tooltip ADDS A DRAW CALL), EnumPopup ShadowCastingMode.
   - Delete button at bottom: Undo.RegisterCompleteObjectUndo(sidecar, Delete Instance) + remove via swap-pop + selectedIndex = null.
6. **Unchecked override blocks**: greyed-out values pulled from layer defaults. Keep them visible (read-only) so user sees what they will inherit. Tooltip: Inherits from layer.
7. **Picking-latency probe**: at 100k synthetic instances (script-spawned via test menu), pick latency must be <16 ms / frame. Record in phase-2-report.md.
8. **OnDestroy / scene unload**: clear overlay.selectedIndex to prevent stale-index gizmo from surviving domain reload.

## Verification Gate

1. refresh_unity + read_console: clean compile.
2. ScatterInstanceCullHarness: PASS unchanged.
3. Manual pick test: with demo layer at ~1000 instances, click any blade -> wireframe + gizmo appear in <1 frame. Drag gizmo -> position updates; release -> single Undo step reverts the drag.
4. Override-block test: enable Collider override on one instance; values persist after domain reload (close + reopen sidecar inspector).
5. Delete-instance test: select + delete -> instance count decrements; Ctrl+Z restores.
6. Asmdef boundary unchanged (P1 gate re-runs).
7. Screenshot: select an instance, save screenshots/phase-2.png showing wireframe + gizmo + focused inspector panel.
8. Picking latency: spawn 100k instances via test menu, time RaycastPick, log in phase-2-report.md (<16 ms target).

## Exit Criteria

- Click-pick on any visible instance in Scene view selects it (wireframe + gizmo + panel).
- Transform gizmo drag updates the record AND single Undo reverts.
- Override toggles persist across domain reload.
- Delete Instance button works and is undoable.
- Picking latency < 16 ms @ 100k.
- ScatterInstanceCullHarness PASS.
- phase-2-report.md written.

## Rollback Plan

- Delete Editor/InstanceSelectionOverlay.cs + meta.
- Revert InstancePickingService.cs to P1-end version (drop RaycastPick).
- Revert TerrainScatterConfigEditor.cs to P1-end (drop EditSingle handler + focused panel).
- Revert AuthoredInstancesData.cs to P1-end (drop public SetRecord/Set*Override APIs; internal byte layout unchanged so existing sidecars still load).
- Rollback risk: LOW. No engine touch, no schema change.

