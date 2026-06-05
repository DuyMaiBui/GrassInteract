# Phase 2 — Generalize to ScatterLayer + Layer List

**Delivers:** Multi-layer painting infrastructure (still grass-only render). `GrassLayer`→`ScatterLayer`, `GrassInteractField`→`ScatterField` holding an ordered layer list, `[Obsolete]` aliases keep the demo working, painter gets a layer dropdown.

## Scope

Generalize the data model + field to support an ordered list of paintable layers, each with its own density map, without breaking the existing single-layer demo. No new render kind yet — every layer is still `kind = Grass`.

## Files owned (this phase)

| File | Change |
|---|---|
| `Assets/GrassInteract/Runtime/ScatterLayer.cs` | NEW — `[CreateAssetMenu(menuName="GrassInteract/Scatter Layer")]` SO. Fields: `enum ScatterKind { Grass, Mesh }` `kind` (default Grass); `densityMap`, `targetInstances`, `seed`, `scaleRange`, `maxSlopeDeg` (moved from GrassLayer), `alignToNormal`, `heightOffset`; Grass-kind: `GrassLODConfig renderConfig`; Mesh-kind fields declared but unused until Phase 3 (`Mesh[] meshLODs`, `Material material`, `float[] lodDistances`). Same public getters as GrassLayer. |
| `Assets/GrassInteract/Runtime/GrassLayer.cs` | CONVERT to `[Obsolete("Use ScatterLayer (kind=Grass).")]` thin subclass/wrapper of `ScatterLayer` with `kind` forced to Grass, so existing `.asset` files + references still resolve. Keep getters delegating. |
| `Assets/GrassInteract/Runtime/ScatterField.cs` | NEW — generalizes the field. `Terrain? boundTerrain` (from Phase 1), `List<ScatterLayer> layers`, `cullCompute`/`indirectMaterial` (shared GPU deps). Builds one engine per layer: Grass→`GrassCpuEngine`/`GrassGpuEngine` (tier-probed as today). Owns the LateUpdate/edit-tick/beginCameraRendering driver loop for ALL layers. |
| `Assets/GrassInteract/Runtime/GrassInteractField.cs` | CONVERT to `[Obsolete("Use ScatterField with a layers list.")]` — on enable, wraps its single `grassLayer` into a one-element `ScatterField.layers`, OR is a subclass that seeds `layers` from the legacy `grassLayer` field. Existing demo scene (which references GrassInteractField) MUST keep working unchanged. |
| `Assets/GrassInteract/Editor/GrassPainterWindow.cs` | MODIFY — target a `ScatterField` (or GrassLayer alias); add a **layer dropdown** populated from the field's `layers` list; the active layer's `densityMap` becomes the brush target. `RebuildFields`/`ResolveFieldOrigin` updated to walk `ScatterField.layers`. Brush stamp core UNCHANGED. |
| `Assets/GrassInteract/Editor/*Verify.cs` (bake/chunk) | MODIFY call sites that pass `GrassLayer` to accept `ScatterLayer`. |
| `Assets/GrassInteract/MIGRATION.md` | NEW — migration guide: GrassLayer→ScatterLayer, GrassInteractField→ScatterField, the one-cycle `[Obsolete]` window. |

## Out of scope

- Mesh render path (Phase 3 — `kind=Mesh` fields exist but no engine yet; a Mesh layer is a no-op render this phase, logged).
- Splat-mask painting + align-to-normal application (Phase 4).

## Approach notes

- **Backward compat is the gate.** The existing `GrassInteractDemo.unity` references `GrassInteractField` + a `GrassLayer` asset. After this phase those must deserialize and render identically. Prefer: `GrassLayer : ScatterLayer` (subclass, no field moves that break serialization) and `GrassInteractField : ScatterField` seeding `layers` from the legacy serialized `grassLayer` in `OnEnable`/`OnValidate`. Verify serialized data survives.
- Per-layer engine ownership: `ScatterField` keeps a parallel `List<IGrassEngine>` (one per layer). Tier probe runs per Grass layer (or once, shared). Driver loop iterates engines.
- Painter dropdown: if the field has 1 layer, behaves exactly as today (no UX regression).

## Success criteria

1. `GrassInteractDemo` opens, compiles, and renders **identical** to Phase 1 end-state (same blades, same look) with the `[Obsolete]` aliases — no scene edits. Screenshot-verified.
2. A `ScatterField` with **two grass `ScatterLayer`s** (different density maps, e.g. tall grass + short grass) scatters both independently; painter dropdown switches the active brush target; painting layer B does not disturb layer A. Screenshot-verified.
3. `[Obsolete]` warnings compile clean (no errors); MIGRATION.md present.
4. All existing harnesses PASS; grass tiers byte-stable.

## Verification (live MCP)

Compile → console clean (only `[Obsolete]` warnings allowed) → open demo, screenshot (== Phase 1) → build a 2-grass-layer ScatterField, paint each via dropdown, screenshot independence → re-run grass harnesses.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|---|---|
| Serialization break on GrassLayer/Field convert | 3 | 5 | 15 | Subclass (no field renames that move serialized data); test deserialize of existing .asset + scene BEFORE declaring done; keep legacy field names |
| Painter dropdown regresses single-layer UX | 2 | 2 | 4 | 1-layer field behaves identically (auto-select layer 0) |
| Per-layer engine driver loop double-submits | 2 | 3 | 6 | One engine per layer, clear ownership; reuse the proven single-engine driver, iterate |

## Timeline: M (~3 days). Long pole = serialization-safe `[Obsolete]` conversion.
