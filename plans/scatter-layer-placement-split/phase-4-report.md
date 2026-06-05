# R4 Report — Type-Tighten Consumers

## Status: code on disk, gate deferred to main loop

## Scope reduction vs plan
Per spawn brief: field relocation deferred to R5 (CS0108 hide warnings + Unity serializer ambiguity). R4 = consumer type-tightening only. DensityScatterLayer/InstanceScatterLayer field bodies stay empty; all fields still live on base ScatterLayer.

## Files edited (8 total)
- `Runtime/DensityPlacement.cs` — ctor param + field type: `ScatterLayer` → `DensityScatterLayer`
- `Runtime/InstancePlacement.cs` — ctor param + field type: `ScatterLayer` → `InstanceScatterLayer`
- `Runtime/ScatterLayer.cs` — `CreatePlacement()` rewritten as switch on `this` type (pattern match)
- `Editor/TerrainScatterConfigEditor.cs` — 6 sites: `HasAuthoredInstances` → `is InstanceScatterLayer`
- `Editor/MigrateScatterLayerTypes.cs` — `SwapLayerInConfig` promoted from `private static` to `internal static`
- `Editor/ScatterBakeToAuthored.cs` — fully rewritten: creates `InstanceScatterLayer` sub-asset via `ScriptableObject.CreateInstance<InstanceScatterLayer>()`, JSON round-trips 26 shared fields, adds sidecar `AuthoredInstancesData`, swaps config.layers entry via `MigrateScatterLayerTypes.SwapLayerInConfig`, removes old `DensityScatterLayer` sub-asset

## HasAuthoredInstances read sites changed
- `Editor/TerrainScatterConfigEditor.cs:283` — `!activeLayer.HasAuthoredInstances || sidecar == null` → `activeLayer is not InstanceScatterLayer || sidecar == null`
- `Editor/TerrainScatterConfigEditor.cs:574` — `bool isEnabled = layer.HasAuthoredInstances` → `bool isEnabled = layer is InstanceScatterLayer`
- `Editor/TerrainScatterConfigEditor.cs:628` — `!layer.HasAuthoredInstances || layer.AuthoredInstances == null` → `layer is not InstanceScatterLayer || layer.AuthoredInstances == null`
- `Editor/TerrainScatterConfigEditor.cs:999` — `sidecar != null && activeLayer.HasAuthoredInstances` → `sidecar != null && activeLayer is InstanceScatterLayer`
- `Editor/TerrainScatterConfigEditor.cs:1034` — `editSidecar == null || !activeLayer.HasAuthoredInstances` → `editSidecar == null || activeLayer is not InstanceScatterLayer`
- `Editor/TerrainScatterConfigEditor.cs:1138` — `activeLayer.HasAuthoredInstances && activeLayer.AuthoredInstances != null` → `activeLayer is InstanceScatterLayer && activeLayer.AuthoredInstances != null`
- `Editor/TerrainScatterConfigEditor.cs:1166` — `activeLayer.HasAuthoredInstances && activeLayer.AuthoredInstances != null` → `activeLayer is InstanceScatterLayer && activeLayer.AuthoredInstances != null`

## ScatterBakeToAuthored.cs — design choices
- `Validate()` menu guard changed from `is ScatterLayer` → `is DensityScatterLayer` (only density layers can be baked).
- Reflection helpers `SetPrivateBool` / `SetPrivateRef` removed — no longer needed since we create a new subclass instead of mutating the source.
- `MigrateScatterLayerTypes.SwapLayerInConfig` reused (promoted to `internal static`) to avoid duplicated SerializedObject swap logic.
- `hasAuthoredInstances` field wired on `newLayer` via `SerializedProperty("authoredInstances")` pointing to the new sidecar. The `hasAuthoredInstances` boolean is NOT set via reflection — `InstanceScatterLayer` implicitly represents authored mode by type; the bool is dead metadata on the base that R5 will delete.

## Remaining HasAuthoredInstances references in repo (after edits)
- `Runtime/ScatterLayer.cs:360` — public accessor declaration (stays through R5)
- `Runtime/ScatterLayer.cs:93` — private field `hasAuthoredInstances` declaration (stays through R5)
- `Runtime/GrassScatter.cs:74` — engine read (OUT of scope per brief, not touched)
- `Runtime/MeshScatterEngine.cs:722` — engine read (OUT of scope per brief, not touched)
- `Editor/MigrateScatterLayerTypes.cs:52` — migration tool reads it to determine target subtype for legacy base-type assets (intentionally retained — tool must still work for any remaining unmigrated configs)

## ScatterField.cs — no edits required
Grep confirmed zero `HasAuthoredInstances` reads in ScatterField.cs.

## InstancePickingService.cs / InstanceSelectionOverlay.cs — no edits required
Both files take `ScatterLayer` for base-accessor reads only (`ChunkSize`, `LodMeshes`). No authored-instance-specific fields accessed through the parameter. Narrowing deferred to R5 or left as-is (not load-bearing for R4 correctness).

## ScatterBrush.cs — no edits required
`StampAuthored`, `EraseAuthored`, `EditBrushStamp` take `ScatterLayer` and only access base fields (`PlaceSpacing`, `GroundSnapMask`, `ScaleRange`, `DensityMap`, `FieldBounds`). Callers in TerrainScatterConfigEditor already guard with `useAuthored` (now type-checked). Narrowing is cosmetic and deferred.
