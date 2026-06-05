# R2 Report — Concrete Subclasses

## Status: code on disk, gate deferred to main loop

## Files created
- Runtime/DensityScatterLayer.cs (17 lines) — empty subclass + CreatePlacement override
- Runtime/InstanceScatterLayer.cs (18 lines) — empty subclass + CreatePlacement override

## Files edited
- Runtime/ScatterLayer.cs — added one virtual method `CreatePlacement()` at line 360 (after `HasAuthoredInstances` accessor). Base class stays CONCRETE (no abstract keyword added).

## CreateAssetMenu paths
- GrassInteract/Density Scatter Layer (order=1)
- GrassInteract/Instance Scatter Layer (order=2)

## Notes
- Existing base [CreateAssetMenu] on ScatterLayer.cs left as-is (will be removed in R5 when base becomes abstract).
- Field population deferred to R4 per plan.
