---
phase: 1
name: editor-scaffolding
effort: M
agent: t1k-unity-developer
blocked-by: none
blocks: P2, P3, P4, P5
---

# Phase 1 - Editor Scaffolding

## Goal

Stand up the authored-instance data model and editor toolbar. Place + Erase functional. Engine still uses GrassScatter procedural path (untouched); Place writes to BOTH the density texture mask AND the new authored list (transitional).

## Scope

IN: AuthoredInstancesData ScriptableObject sidecar, ScatterLayer hook fields (HasAuthoredInstances, AuthoredInstances ref, PlaceSpacing), 5-mode toolbar in TerrainScatterConfigEditor, ScatterBrush rewire of Place+Erase to append/remove records, InstancePickingService skeleton (spatial hash only; ray-vs-sphere lives in P2).

OUT: per-instance picking UI (P2), brush-edit ops (P3), engine skip-path (P4), targetInstances deprecation (P5).

## File Ownership

| File | Action |
|---|---|
| Runtime/AuthoredInstancesData.cs | CREATE - ScriptableObject + ISerializationCallbackReceiver, byte[] blob + List<Object> refs, NativeArray<InstanceRecord> at runtime |
| Runtime/ScatterLayer.cs | EDIT - add HasAuthoredInstances bool, AuthoredInstancesData reference, PlaceSpacing float (default 0.5 m, range 0.05-5 m) |
| Editor/TerrainScatterConfigEditor.cs | EDIT - replace 2-mode (Paint/Erase) toolbar with 5-mode (Place/Erase/Edit-Single/Edit-Brush/Off); only Place+Erase functional this phase |
| Editor/ScatterBrush.cs | EDIT - Place mode appends InstanceRecord(s) to sidecar AND paints density mask; Erase mode removes records inside radius AND clears density mask |
| Editor/InstancePickingService.cs | CREATE - CPU spatial hash (cellId -> List<idx>), Rebuild(layer), Invalidate(); ray-pick deferred to P2 |

## Step-by-Step Tasks

1. **Define InstanceRecord schema** in AuthoredInstancesData.cs. Variable-size encoding per brainstorm: Vector3 pos (12B), Quaternion rot (16B), Vector3 scale (12B), uint32 overrideMask (4B). Optional ColliderOverride (12B) and RendererOverride (12B) appended when flagged. Total 44-68 B/instance.
2. **Implement byte-blob (de)serialization** via ISerializationCallbackReceiver. OnBeforeSerialize: pack NativeArray -> byte[]. OnAfterDeserialize: unpack byte[] -> NativeArray (allocate as Persistent). Object refs (materials, meshes) stored in List<Object> with index into byte stream.
3. **Add ScatterLayer fields**: public bool HasAuthoredInstances; public AuthoredInstancesData AuthoredInstances; [Range(0.05f, 5f)] public float PlaceSpacing = 0.5f. Add [Tooltip] explaining density-map role flip (count-multiplier -> placement mask) when HasAuthoredInstances=true.
4. **Rewire TerrainScatterConfigEditor toolbar**: replace existing Paint/Erase enum with ToolMode { Off, Place, Erase, EditSingle, EditBrush }. Top toolbar = 5 GUIToggles; only Place+Erase wired this phase. Edit-Single/Edit-Brush show coming-soon-in-P2/P3 labels.
5. **Rewire ScatterBrush.Place stroke**: on mouse-down: Undo.RegisterCompleteObjectUndo(sidecar, Paint Stroke); on stamp: sample density mask, if mask allows, generate Poisson-disk candidates at PlaceSpacing within stamp radius, append each as new InstanceRecord with random yaw + scale within layer ranges, ALSO paint density mask texel for visualization parity. Hard-cap stamp at MAX_INSTANCES_PER_STAMP=10000; warn + truncate.
6. **Rewire ScatterBrush.Erase stroke**: Undo.RegisterCompleteObjectUndo as above; on stamp: query InstancePickingService spatial hash for indices within radius; remove via swap-pop; also clear density mask texel.
7. **InstancePickingService**: cellSize = layer.ChunkSize. Build(records, layer): Dictionary<int, List<int>> cellId -> instanceIdx. QueryRadius(center, r): yield indices in cells touching radius (cheap AABB).
8. **Sidecar creation flow**: when user toggles HasAuthoredInstances=true with no AuthoredInstances set, ScriptableObject.CreateInstance + AssetDatabase.AddObjectToAsset(sidecar, layer asset) + SetDirty + SaveAssets. Sub-asset, not standalone file.
9. **Stroke-end hook**: ScatterBrush calls InstancePickingService.Invalidate() on mouse-up so next pick query (in P2) sees fresh state.
10. **Smoke-throughput**: open demo scene, enable HasAuthoredInstances on demo layer (creates empty sidecar), Place-paint a 5x5 m patch at PlaceSpacing=0.5, count instances (~100), time elapsed. Expect >5000 inst/sec stamp on desktop.

## Verification Gate

1. mcp__UnityMCP__manage_script - refresh_unity(mode=force, scope=scripts, compile=request, wait_for_ready=true).
2. mcp__UnityMCP__read_console - filter Error+Warning, expect 0 NEW errors and 0 NEW warnings beyond pre-existing baseline.
3. ScatterInstanceCullHarness - run via Window menu OR EditorWindow command; MUST PASS (engine path untouched this phase).
4. Asmdef boundary check: grep -L UnityEditor Runtime/AuthoredInstancesData.cs (no UnityEditor refs in Runtime). grep -l UnityEditor Editor/InstancePickingService.cs Editor/ScatterBrush.cs Editor/TerrainScatterConfigEditor.cs (Editor refs allowed).
5. Screenshot: enable Place mode on demo layer, paint a small patch, save screenshots/phase-1.png via mcp__UnityMCP__screenshot_editor.
6. Throughput sanity: measured >=5000 inst/sec stamp on the smoke run; record actual number in phase-1-report.md.
7. Sidecar size check: after painting ~1000 instances, asset file size < 100 KB (sanity for 5 MB @ 100k target).

## Exit Criteria

- Editor compiles clean.
- Toolbar shows 5 modes; Place+Erase functional; Edit-Single/Edit-Brush show stub label.
- ScatterLayer asset exposes HasAuthoredInstances + AuthoredInstances + PlaceSpacing in inspector with tooltips.
- AuthoredInstancesData sidecar can be created, populated, saved, reloaded across domain reload (instance count survives).
- ScatterInstanceCullHarness PASS unchanged.
- Undo: one stamp-stroke is a single Ctrl+Z step (verified manually).
- phase-1-report.md written with: what compiled, harness result, screenshot path, throughput number, sidecar size.

## Rollback Plan

- New files (AuthoredInstancesData.cs, InstancePickingService.cs): delete + their .meta files; refresh.
- ScatterLayer.cs: revert (manual; no git). Note: any demo layer asset edited with HasAuthoredInstances=true will lose that flag on revert, but the data is on the sidecar sub-asset and not lost.
- TerrainScatterConfigEditor.cs / ScatterBrush.cs: revert; toolbar returns to 2-mode.
- AuthoredInstancesData sidecar sub-assets: leave orphaned (harmless; can be cleaned via Assets > Remove Sub-Asset).
- Rollback risk: LOW. No engine touch, no schema touch.

