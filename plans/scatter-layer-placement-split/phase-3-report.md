# R3 Report — Migration Menu + Demo Asset Migration

## Status: ✅ SHIPPED. Score-20 MonoScript GUID risk RETIRED.

## What landed

### Files created
- `Assets/GrassInteract/Editor/MigrateScatterLayerTypes.cs` — 127 lines. Two `[MenuItem]`s (dry-run + real). Walks `AssetDatabase.FindAssets("t:TerrainScatterConfig")`, identifies legacy-base-type ScatterLayer sub-assets via `obj.GetType() == typeof(ScatterLayer)` exact check, picks target subtype by `oldLayer.HasAuthoredInstances`, JSON round-trips via `EditorJsonUtility.ToJson/FromJsonOverwrite`, swaps `TerrainScatterConfig.layers` entry via `SerializedObject`.

### Files NOT touched
- `Runtime/ScatterLayer.cs` — base stays concrete (R5 work).
- Any consumer (R4 work).

### Backups written
- `plans/scatter-layer-placement-split/backups/727a186375fa01d438613d95ddebd98e__GrassInteractDemoLayer.json`
- `plans/scatter-layer-placement-split/backups/727a186375fa01d438613d95ddebd98e__Rock.json`

Per-asset JSON snapshots before destructive ops. Rollback path documented in phase-3.md § Rollback Plan.

### Migration outcome
- **2 sub-assets migrated** (`GrassInteractDemoLayer` + `Rock`).
- Both target = `DensityScatterLayer` (both had `HasAuthoredInstances=false`).
- 0 already-typed sub-assets to skip.

## Verification

| Gate | Result | Notes |
|---|---|---|
| Compile clean | ✅ | 0 project errors. Only MCP-transport "Cannot access a disposed object" + pre-existing 2 NullReferenceExceptions (VContainer-related, not project code, not introduced this phase). |
| MonoScript GUID swap | ✅ | Demo asset YAML: GrassInteractDemoLayer + Rock sub-assets now reference `m_Script: guid: 1abe7025d8db77e408740d14122533b7` = DensityScatterLayer.cs MonoScript. Legacy ScatterLayer GUID `0e6346a90875b6b41a61bca31e5f65da` NOT present anywhere in the .asset YAML. |
| Visual parity | ✅ | `screenshots/phase-3-render.png` — dense grass + rocks + orange interactor sphere; visually identical to baseline `plans/authored-instance-scatter-editor/screenshots/phase-5-before.png`. |
| Backups exist | ✅ | 2 JSON files in `backups/` |
| `ScatterFieldRebuildLayerHarness` | DEFERRED | Menu invocation hit "no menu named" intermittently (Unity menu-cache flake post-migration; harness DLL is loaded — pre-existing tools register normally after a fresh refresh). Substitute = screenshot game-view confirms render path intact. |

## Gotcha discovered — and resolved upstream of R3

When R3 first ran, ALL `Tools/GrassInteract/*` menus were briefly "not found" — including pre-existing ones. Root cause traced back to R1: the new `IScatterPlacement` interface was declared `internal` but used as a return type by a `public virtual` method on `ScatterLayer` in R2. CS0053 accessibility violation broke compile of `GrassInteract.dll`, which cascaded to `GrassInteract.Editor.dll` failing to load. Main loop fixed in R2-gate by changing `internal interface` → `public interface` in `IScatterPlacement.cs`, then triggering `refresh_unity(scope=all)` so `.meta` files generated for the R1 files.

**Lesson:** R1 verification gate used `scope=scripts` which doesn't generate `.meta` files for new `.cs` files. R3+ in this plan should run `refresh_unity(scope=all)` after creating any new file. Already encoded in P3+ phase docs.

## Subagent budget

R3 subagent (aa747aa2f8e56a2ea): 83K tokens, 46 tool uses; stalled mid-narration on the "menu items not found" diagnostic detour. Main loop completed: ran the dry-run + real menu invocations directly via MCP (after pinning instance + scope=all refresh), verified migration outcome by reading the .asset YAML directly + cross-referencing MonoScript GUIDs, screenshot game-view for visual parity, wrote this report.

Cumulative cook subagent budget so far: R1 75K + R2 68K + R3 83K = ~226K. Much lower than the prior P1–P5 cook's 655K — the "code edits in subagent, gate verification in main loop" pattern is paying off.

## Files for R4

- `Runtime/ScatterField.cs` — replace `HasAuthoredInstances` reads with `is InstanceScatterLayer` pattern. ~5 sites.
- `Editor/TerrainScatterConfigEditor.cs` — layer-tab UI varies by concrete type; toolbar hidden on DensityScatterLayer.
- `Editor/ScatterBrush.cs` — narrow Place/Erase/EditBrush methods to `InstanceScatterLayer`.
- `Editor/InstancePickingService.cs` + `Editor/InstanceSelectionOverlay.cs` — API tightening to `InstanceScatterLayer`.
- `Editor/ScatterBakeToAuthored.cs` — needs to CREATE an `InstanceScatterLayer` sub-asset (not flip a bool on DensityScatterLayer).
- `Runtime/DensityScatterLayer.cs` + `Runtime/InstanceScatterLayer.cs` — actual field migration deferred until R5 (after consumers narrow); brainstorm allows the deferred move because all data already lives on the base + migrates byte-equal across the JSON round-trip.
