# WorldPainter — Consolidate Authoring onto Unified SurfaceLayers (Migration)

**Date:** 2026-06-13 · **Branch:** `feat/worldpainter-ssot-consolidation`
**Supersedes** the per-tile plan (`plans/260613-worldpainter-splat-grass-pertile-fixes/`) — that work is now *part* of the target system.

## Why this exists
The codebase has **two parallel layer systems**. The user's inspector drives the **legacy** one; the per-tile/albedo/play-mode fixes already shipped landed in the **unified** one. Decision: **promote the unified `SurfaceLayers` system to be the single authoring SSOT, migrate splat + grass + props onto it, and drop all legacy** — preserving editor and runtime behavior (frozen render engines reused via adapters). **Data-structure change only; no behavior regression.**

## Locked scope (user)
- Remove legacy, promote the new unified layer system as the inspector SSOT.
- Migrate **splat**, **grass**, AND **props** to unified (props follow the same pattern as grass — a `WorldPainterLayer` subclass + a runtime adapter — NOT necessarily per-tile; props keep their authored-instance render).
- **Drop legacy:** `DensityScatterLayer`, inline `splatLayers[]` / `SplatLayerDef`, `InstanceScatterLayer`.
- **Render compatibility is mandatory:** the FROZEN engines (`GrassGpuEngine`, `GrassCpuEngine`, `InstancedPropEngine`, `InstanceBatchPool`, `IGrassEngine`, `IScatterPlacement`, `GrassScatter`) are reused unchanged, via adapters. Grass + props must render exactly as before.

## Already in the target system (prior commits — keep)
`d78d6e6` unified splat albedo sub-asset · `09d6a75` unified per-tile grass density · `4640be4` play-mode grass build+submit · `79d8755` per-target density writeback queue.

## Key architectural fact (makes props tractable)
`InstancedPropEngine.Build(ScatterLayer, …)` and the grass engines consume any `ScatterLayer` implementing the placement interfaces. `GrassLayer` already does this through the transient `GrassVariantScatterLayer : ScatterLayer, IDensityPlacementSource` adapter (`WorldPainter.SurfaceLayers.RebuildSurfaceLayers`, which legally calls the frozen private helpers since it's the same partial class). **Props mirror this exactly.**

---

## Phase 0 — Wiring confirmation (focused scout, read-only)
Confirm the exact seams before touching anything (the cost of getting this wrong is high — see "Why this exists"):
- Main inspector layer stack: how rows are built + which data each card binds (`WorldPainterLayerStackView*`, `WorldPainterSplatLayerCard`, `WorldPainterScatterLayerCard`, `WorldPainterPropLayerCard`). Is `WorldPainterSplatPaletteView` wired in or dead?
- Paint routing: `WorldPainterState` active-layer/kind model; `BrushToolTargets` for splat/density/instance.
- Splat runtime binding: where `GpuTerrainEngine` binds `TerrainLayerSet`/`splatSet` vs inline `splatLayers[]` albedos (confirms why inline-splat edits don't reach runtime today).
- Full legacy reference inventory (every `DensityScatterLayer`, `SplatLayerDef`/`splatLayers`, `InstanceScatterLayer`, `AddDensityLayer`, `AddInstanceLayer`, legacy cards, tests).
**Output:** an exact seam + reference list that locks Phases 1-6.

### Phase 0 findings (DONE)
- **Splat runtime already unified:** `GpuTerrainEngine.BindSplatLayers` ← `TerrainLayerSetBinder.Build(map.SplatSet)` (`WorldPainter.Render.cs:147,214`). Inline `splatLayers[]` NEVER reach runtime → splat migration is **UI-only** (detail card + add reroute to `SplatLayer`/`TerrainLayerSet`).
- **`WorldPainterSplatPaletteView` is dead code** (never instantiated) → delete in Phase 5.
- **Main inspector** = `WorldPainterInspector.cs` + `WorldPainterLayerStackView` (reads inline `splatLayers` + `scatterLayers`→`map.Layers` + `biomes`; no `SurfaceLayers`).
- **Prop adapter strategy (locked):** `PropLayer : WorldPainterLayer` (NOT a ScatterLayer) + transient `PropLayerScatterLayer : ScatterLayer, IInstancePlacementSource`; `RebuildSurfaceLayers` builds via `new InstancedPropEngine(cullCompute, mat).Build(adapter, …)` directly (the typed frozen helper can't take the adapter). Frozen engine files untouched.
- **Prop render-driving seam to preserve:** `WorldPainter.DrivePropLayers()` (editable `WorldPainter.cs`) reads `scatterLayers[i] as InstanceScatterLayer` for impostor-LOD driving; the unified path must drive its `PropLayer` adapters equivalently. Collider/tilt/indirect draws live inside the frozen `InstancedPropEngine.Submit` (layer-agnostic — fed by the adapter, no change).
- **Frozen-file caveat (Phase 5):** dropping `DensityScatterLayer`/`InstanceScatterLayer` requires retiring the legacy build path in `WorldPainter.Scatter.cs` (its header says "FROZEN"). The protected items are the ENGINE files (GrassGpuEngine/GrassCpuEngine/InstancedPropEngine/InstanceBatchPool/IGrassEngine/IScatterPlacement/GrassScatter); the orchestrator partial must be edited to retire `RebuildScatter`. Flag at Phase 5.

## Phase 1 — Unified `PropLayer` + adapter (render-compatible)
- `WorldPainterLayer.LayerKind`: add `Prop`.
- New `PropLayer : WorldPainterLayer` — carries the prop config currently on `InstanceScatterLayer` (render/wind/deform/bounds/placement/tilt + authored-instances ref + collider/pool/cull config).
- New transient adapter `PropLayerScatterLayer : ScatterLayer, IInstancePlacementSource` (mirrors `GrassVariantScatterLayer`).
- Lifecycle `AddPropLayer(map, name)` — creates the `PropLayer` + `AuthoredInstancesData` sub-asset.
- `RebuildSurfaceLayers`: build prop engines from `PropLayer` via the adapter + the frozen `TryBuildScatterInstancedPropEngine`/`SelectAndBuildScatterEngine`. Wire `StepSurfaceLayers`/`SubmitSurfaceLayers`/dispose for prop engines. Keep collider/impostor drivers (`DrivePropLayers`) working against the unified path.
- **Gate:** props render + collide identically to the legacy path (frozen engines untouched).

## Phase 2 — Inspector reroute (main layer stack → unified SurfaceLayers)
- Add-menu creates unified types: "Splat" → `AddSplatLayer` (unified, exists), "Grass" → `AddGrassLayerWithBlades` (unified, exists), "Props" → `AddPropLayer` (Phase 1).
- Layer stack lists `map.SurfaceLayers` (splat/grass/prop) instead of inline `splatLayers[]` + `map.Layers`.
- Detail cards bind unified layers (rewrite/replace `WorldPainterSplatLayerCard`, `WorldPainterScatterLayerCard`, `WorldPainterPropLayerCard`). Reconcile/remove `WorldPainterSplatPaletteView`.

## Phase 3 — Paint routing reroute
- `WorldPainterState` active-layer model keys off `SurfaceLayers` (kind from `WorldPainterLayer.Kind`).
- Splat paint → `SplatLayer`/`TerrainLayerSet` weight channels; grass → per-tile (done); props → `PropLayer` authored instances.

## Phase 4 — Runtime reroute + splat binding
- Confirm `GpuTerrainEngine` binds the unified `TerrainLayerSet`/`splatSet` (so splat edits reach runtime). Fix the binding if it still reads inline data.
- `map.Layers` no longer populated → `RebuildScatter` retires; `RebuildSurfaceLayers` is the sole scatter builder (grass + props). Edit-mode preview + play-mode both build/submit surface layers.

## Phase 5 — Drop legacy
Pre-delete reference sweep, then remove: `DensityScatterLayer`, `SplatLayerDef`/inline `splatLayers[]` (+ `MAX_SPLAT_LAYERS` if now unused), `InstanceScatterLayer`, `AddDensityLayer`/`AddInstanceLayer`, the legacy cards/paths. **Keep:** `ScatterLayer` base, `IDensityPlacementSource`/`IInstancePlacementSource`, `AuthoredInstancesData`, `DensityPlacement`/`InstancePlacement`, all frozen engines.

## Phase 6 — Tests + verify + review
Migrate all tests off the dropped types; one compile + full `WorldPainter.Tests` pass; independent code review; per-phase commits.

---

## Status: COMPLETE (functional) — 2026-06-13

| Phase | Commit | Tests |
|---|---|---|
| 1 — unified `PropLayer` + adapter (frozen `InstancedPropEngine`) | `92ad937` | 387 |
| 2 — main inspector authors unified `SurfaceLayers` | `8b9c7db` | 387 |
| 3 — paint routing (splat channel + prop stamping) | `8bb5491` | 401 |
| 5 — drop legacy classes/cards/lifecycle-adds | `c55b55c` | 357/357 ✅ |

Phase 4 (runtime/splat binding) was already satisfied (splat runtime was always unified). Final: compile clean, **357/357 EditMode pass** (count dropped from 401 because ~44 legacy-type tests were removed with the legacy systems).

### Phase 6 — review + fixes (DONE)
Independent code review verdict: **ship (light fixes)**, no blockers. Resolved:
- `37a26f1` — sampler-staleness regression (null `scatterSampler` on teardown).
- `4646131` — re-ported the scene-gizmo prop transform editor to `PropLayer` (HUD mode toggle in `WorldPainterSculptTool.DrawHud`, `T` key); hid the dead biome grass-density toggle.
- Demo `WorldMap.asset` restored (user cleans orphaned legacy sub-assets in-Editor; not committed).
Final: **357/357 EditMode pass**, compile clean. Remaining NITs (pre-existing dead collider config, stale doc cref, monotonic diagnostic `propImpostorLods`) deferred — non-gating.
NOTE: the "PARTIAL legacy drop" caveat below is now SUPERSEDED — `8f92bb0` removed the dormant container fields; the drop is complete (only the unrelated `TerrainScatterConfig`/`ScatterField` system remains, out of scope).

### IMPORTANT — this is a PARTIAL legacy drop
Removed: `DensityScatterLayer`, `InstanceScatterLayer`, the 3 legacy detail cards, dead `WorldPainterSplatPaletteView`, `WorldPainterPropTransformEdit`, legacy `AddDensityLayer`/`AddInstanceLayer`/`RemoveLayer`, and legacy-only tests. **KEPT** (still compiled-in, no longer used for authoring): the `WorldPainter` MonoBehaviour's inline `splatLayers[]`/`scatterLayers[]` fields + `SplatLayers`/`ScatterLayers` properties, `SplatLayerDef`, `MAX_SPLAT_LAYERS`, `WorldMapAsset.Layers`. Fully excising these dormant container fields (and the few display files that still read them) is a clean follow-up — they don't affect the unified authoring path but are dead weight.

### Forced frozen-file edits (flag)
Deleting `InstanceScatterLayer` forced 3 type swaps in the otherwise-frozen `InstancedPropEngine.cs` + `InstanceTiltSimulator.cs` (`as InstanceScatterLayer` / ctor param → `PropLayerScatterLayer`). Render logic unchanged. A cleaner version casts to `IInstancePlacementSource` (add `Tilt`/`AnyRecordWantsCollider` to that interface) — optional polish.

### Manual in-Editor verification still owed (cannot be unit-tested)
1. Inspector "+" → Splat/Grass/Props create unified sub-assets on the WorldMap.
2. Paint grass across 2 tiles → 2 per-tile density textures, no seam bleed.
3. Select a splat albedo slot → paint → correct channel changes.
4. Props: select layer → stamp → instances appear.
5. Press Play → grass + props render.

## Risks
- **Large blast radius** across editor + runtime; dropping 3 types needs a careful reference sweep (`development-principles.md` pre-delete check).
- **Render parity** for grass + props is the hard gate — adapters only, frozen engines never edited.
- **Per-asset migration:** existing demo `WorldMap.asset` authored on legacy types will lose those layers (consistent with the earlier "reset grass" choice — re-add layers in the unified inspector).
- Multi-phase, multi-session-scale; each phase ships compiling + tested + committed before the next.

## Open interpretation (confirm at approval)
- "Migrate props like grass" = same *unification pattern* (unified layer + adapter), **not** per-tile density (props are authored instances). If you actually want per-tile prop bucketing surfaced too, say so.
- Splat stays a 4-channel `TerrainLayerSet` (R/G/B/A albedos), albedo-only (normals still deferred).
