# R5 Report — Promote Abstract + Façade + Cleanup

## Status: code on disk, gate deferred to main loop

## Pre-flight asset-presence check
- Demo asset sub-asset GUID: 1abe7025d8db77e408740d14122533b7 (DensityScatterLayer) ✅
- Legacy ScatterLayer GUID 0e6346a... NOT present ✅

## Backups written
- plans/scatter-layer-placement-split/backups/r5-pre/ScatterLayer.cs.bak
- plans/scatter-layer-placement-split/backups/r5-pre/GrassScatter.cs.bak
- plans/scatter-layer-placement-split/backups/r5-pre/DensityScatterLayer.cs.bak
- plans/scatter-layer-placement-split/backups/r5-pre/InstanceScatterLayer.cs.bak

## Files edited
- Runtime/ScatterLayer.cs (674 lines → 383 lines; abstract promoted; hasAuthoredInstances field+accessor deleted; targetInstances + [Obsolete]+[FormerlySerializedAs]+[HideIf]+[InfoBox]+#pragma 0618 stack deleted; densityMap/authoredInstances/placeSpacing serialized fields deleted; virtual DensityMap/AuthoredInstances/PlaceSpacing accessors kept on base for ScatterBrush compatibility; ValidateAuthoredAndCommon removed; Validate simplified to shared checks; CreatePlacement now abstract)
- Runtime/GrassScatter.cs (355 lines → 113 lines; Build shrunk to one-line façade; BuildFromAuthored deleted; BuildFieldBounds promoted to internal static; ReturnSlabs kept)
- Runtime/DensityScatterLayer.cs (17 lines → 51 lines; added densityMap + targetInstances serialized fields with FormerlySerializedAs shims; DensityMap accessor overrides base virtual; TargetInstances added; Validate override added)
- Runtime/InstanceScatterLayer.cs (18 lines → 37 lines; added authoredInstances + placeSpacing serialized fields with FormerlySerializedAs shims; AuthoredInstances and PlaceSpacing accessors override base virtuals; Validate override added)
- Runtime/MeshScatterEngine.cs (line 722: !layer.HasAuthoredInstances → layer is not InstanceScatterLayer)
- Editor/MigrateScatterLayerTypes.cs (line 52: oldLayer.HasAuthoredInstances → SerializedObject-based read of legacy field)

## Grep gate
- HasAuthoredInstances anywhere in Assets/ (code, not comments): 0 ✅
- [Obsolete] targetInstances in ScatterLayer.cs: 0 ✅
- FormerlySerializedAs "targetInstances" in DensityScatterLayer.cs: 1 ✅ (the migration shim)
- pragma 0618 in ScatterLayer.cs: 0 ✅
- BuildFromAuthored (executable code): 0 ✅ (one comment reference in InstancePlacement.cs doc comment only)

## Design notes
- DensityMap / AuthoredInstances / PlaceSpacing are kept as virtual properties on the base
  (returning null/default) so ScatterBrush.cs compiles without modification. The serialized
  backing fields are removed from the base; data now lives only on the concrete subclasses.
  FormerlySerializedAs shims ensure R3-migrated demo asset data resolves correctly on the
  DensityScatterLayer subclass when Unity re-reads the sub-asset.
- MigrateScatterLayerTypes now reads the legacy hasAuthoredInstances field via SerializedObject
  instead of the removed C# accessor, preserving the migration tool for any un-migrated projects.
