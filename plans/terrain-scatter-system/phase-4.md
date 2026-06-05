# Phase 4 — Polish: Splat-Mask Paint, Align-to-Normal, Slope Ranges, Editor UX

**Delivers:** Production-quality authoring. Paint placement by terrain texture (splat mask), props/grass align to terrain normal, per-layer slope ranges, and editor UX cleanup. Optional/last; depends on Phase 3.

## Scope

Use the `SurfaceHit.SplatWeights` already read in Phase 1 as a placement MASK; apply `alignToNormal`; expose per-layer min/max slope; polish the painter window.

## Files owned (this phase)

| File | Change |
|---|---|
| `Assets/GrassInteract/Runtime/ScatterLayer.cs` | MODIFY (additive) — `int splatLayerIndex = -1` (-1 = off) + `float splatThreshold`; `Vector2 slopeRange` (min/max deg, replaces single maxSlopeDeg or supplements it); `alignToNormal` already present. |
| `Assets/GrassInteract/Runtime/GrassScatter.cs` | MODIFY — placement filter: skip candidate if `splatLayerIndex>=0 && hit.SplatWeights[splatLayerIndex] < splatThreshold`, or slope outside `slopeRange`. Pass `hit.Normal` into the base matrix when `alignToNormal` (compose normal-align rotation with yaw). |
| `Assets/GrassInteract/Shaders/ScatterInstanced.shader` + grass VS | MODIFY (if align done in-shader) — accept a per-instance up-vector OR bake the normal-aligned rotation into `packedYawScale`/matrix at scatter time (prefer bake-time to keep VS cheap). |
| `Assets/GrassInteract/Editor/GrassPainterWindow.cs` | MODIFY — "Paint by terrain texture" toggle (pick splat layer as mask preview); per-layer settings surfaced; brush preview shows effective placement mask (density × splat × slope). |
| `Assets/GrassInteract/Editor/GrassPainterWindow.cs` (overlay) | MODIFY — multi-layer overlay legend; show active layer kind/icon. |
| `Assets/GrassInteract/MIGRATION.md` / `README.md` | MODIFY — document splat-mask + align-to-normal + slope-range authoring; finalize the GPU-tier + ScatterField setup docs (the pre-existing README GPU-tier doc TODO folds in here). |

## Out of scope

- Multi-tile terrain neighbor-stitching (explicitly deferred to a future plan).
- Runtime (play-mode) re-scatter / streaming (current model is edit-time bake).

## Approach notes

- Splat weights are already sampled into `SurfaceHit` in Phase 1 (`TerrainData.GetAlphamaps`); this phase only USES them as a mask + painter UI. No new sampling cost if cached at bake.
- Prefer baking the `alignToNormal` rotation at scatter time (into the instance's matrix/packed rotation) over a per-vertex VS normal lookup — keeps the static prop VS cheap and avoids storing a per-instance normal.
- `slopeRange` supersedes Phase 1's single `maxSlopeDeg` (keep `maxSlopeDeg` as `slopeRange.y` for back-compat or migrate with `[Obsolete]`).

## Success criteria

1. A prop layer with `splatLayerIndex` set only places props where the chosen terrain texture is painted (e.g. rocks only on the rocky splat). Screenshot-verified.
2. `alignToNormal` props/grass visibly tilt to follow terrain slope. Screenshot-verified.
3. Per-layer `slopeRange` excludes both too-flat and too-steep placement as configured.
4. Painter UX: splat-mask toggle works, multi-layer overlay legible; no regression to paint/erase/dropdown.
5. Clean compile; all harnesses PASS; grass + prop tiers byte-stable vs Phase 3.

## Verification (live MCP)

Compile → console clean → paint a splat-masked prop layer on a multi-texture terrain, screenshot mask adherence → toggle alignToNormal, screenshot slope-following → set a slopeRange, screenshot exclusion → re-run all harnesses.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|---|---|
| Alphamap index/resolution mismatch vs height/hole maps | 3 | 3 | 9 | Map all three off the same world→terrain-UV; harness asserts known splat at known UV |
| Align-to-normal rotation math wrong (props lean wrong way) | 2 | 3 | 6 | Bake rotation = FromToRotation(up, normal)*yaw; visual screenshot on a ramp |
| Painter UX clutter regresses simple grass painting | 2 | 2 | 4 | Splat/slope controls collapse when layer has them off; default UX == Phase 2 |

## Timeline: M (~3 days). Independent polish items; can be partially shipped.
