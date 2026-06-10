# Code Review: PlaceSpacing removal fix

Scope: removal of `PlaceSpacing` / `placeSpacing` / `RespectsSpacing` to allow free instance
placement in both Place and Scatter modes. Read-only adversarial review; no edits made.

## Verdict per item

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | Root cause addressed — no proximity/spacing reject path remains | **PASS** | grep `PlaceSpacing\|placeSpacing\|RespectsSpacing` over all `*.cs` → 0 matches. Place path (`OnPlace`, InstancePlacementTool.cs:171-183) places unconditionally on click; Scatter path (`OnScatter`, :213-228) places every grounded candidate with no spacing rejection. |
| 2 | No dangling interface / consumer references | **PASS** | Only implementor of `IInstancePlacementSource` is `InstanceScatterLayer` (InstanceScatterLayer.cs:18). Only consumer is `InstancePlacement` (InstancePlacement.cs:14,23,72,75,85-89), which reads `AuthoredInstances`, `FieldBounds`, `ScaleRange`, `MaxBladeHeight`, `BendHeadroom` — never `PlaceSpacing`. Interface (IInstancePlacementSource.cs:12-21) no longer declares it. |
| 3 | No unused-variable / unused-using warnings introduced | **PASS (with note)** | `layer` param in `OnPlace` (InstancePlacementTool.cs:168) is now unused inside the body — but unused *method parameters* do not emit a C# warning (only unused locals/fields do), so this is warning-clean. `layer` IS still used in `OnScatter` (:200 `field.ResolveGroundMask(layer)`) and `authored` is used in both. Usings unchanged and all still referenced. |
| 4 | Ghost preview call type-checks | **PASS** | `InstanceGhostPreview.Set` signature is `(InstanceScatterLayer layer, Vector3 hitPoint, Vector3 hitNormal, bool spacingOk, bool visible)` (InstanceGhostPreview.cs:87-92). Call site passes `(layer, hit.point, hit.normal, spacingOk: true, visible: true)` (InstancePlacementTool.cs:128) — matches exactly. |
| 5 | No tests reference removed API | **PASS** | Tests dir has only `AuthoredInstancesDataBlobTests.cs` + `DensityBrushMathTests.cs`. The "spacing" hits in DensityBrushMathTests (:92-165) are `DensityPaintGPU.ComputeStampPositions` brush-stroke stamp spacing — an unrelated concept, not the removed instance-proximity field. Zero references to `PlaceSpacing`/`placeSpacing`/`RespectsSpacing`. |

## Critical (must fix)
None.

## Important (fix before merge)
None.

## Minor / Suggestions
- InstancePlacementTool.cs:168 — `layer` parameter on `OnPlace` is now dead. Compiler-silent, but
  removing it (and the corresponding argument at the call site, :108) would be a tidier signature.
  Optional; not a blocker. Leaving it keeps `OnPlace`/`OnScatter` signatures parallel, which is a
  defensible reason to keep it.

## Adversarial notes (things checked that could have bitten)
- Confirmed there is no *second* implementor of `IInstancePlacementSource` lurking in Editor/Tests
  that would have broken on the interface member removal — there is exactly one.
- Confirmed the Scatter path's only remaining placement constraint is "candidate must hit ground"
  (`Physics.Raycast` probe, :222) and the per-stroke cap `MAX_SCATTER_PER_STROKE` (:213) — neither
  is a proximity/spacing reject, both are pre-existing and unrelated to the bug.
- Confirmed `spacingOk` still exists in `InstanceGhostPreview` as a *coloring* input (green/red),
  not a placement gate; passing `true` unconditionally just keeps the ghost green. No behavior leak.

## Score: 9.5/10
Root cause fully removed, no regressions, no dangling references, tests unaffected. Half-point
deduction only for the cosmetic dead `layer` param on `OnPlace`.
