# Plan: Scatter LOD — explicit cull distance + culling bug fix + draggable distance bar

Date: 2026-06-10 · Source brainstorm: `plans/reports/scatter-lod-cull-bar-brainstorm.md` (design approved)
Owner library: `Assets/GrassInteract/` (the grass/scatter library IS the deliverable).

## Goal

Replace the hidden, derived cull formula with an **explicit per-layer `renderCullDistance`** on `ScatterRenderConfig`, fix the
"typed-500-culls-elsewhere" bug uniformly across all three render engines + the shared compute shader (edit-mode MUST
equal play-mode), and add a Unity-LODGroup-style **distance bar** in the editor (read-only render first, then draggable
handles) using **closeness %** labels. No density/thinning — pure distance visualization.

> Field name is `renderCullDistance` (not `cullDistance`) to avoid colliding with the EXISTING collider-culling
> `cullDistance` field already on `InstanceScatterLayer`. The new field is the render/LOD far-cull boundary only.

## Non-goals (explicit)

- No per-LOD density / thinning feature. The bar is distance-only.
- No change to `ScatterLod` shape (`{ mesh, maxDistance }` stays).
- No change to the unrelated collider-culling `cullDistance` on `InstanceScatterLayer` (different concept; do not touch).

## Bands (single source of truth for all engines + bar)

```
[0 .. d0)     LOD0
[d0 .. d1)    LOD1
[d1 .. cull)  LOD2        (d1 = LodMaxDistances[last]; last LOD bounded by cull, no longer "covers all remaining")
[cull .. ∞)   CULLED
```
`d0 = LodMaxDistances[0]`, `d1 = LodMaxDistances[1]`, `cull = RenderCullDistance`. Squared comparisons in the hot path.

## Phases

- **Phase 1 — Data model + migration** | Effort: **S**
  Add `renderCullDistance` field + `RenderCullDistance` accessor to `ScatterRenderConfig`. Add `OnValidate` migration on both
  concrete layers defaulting `renderCullDistance = max(2 * secondLastLODdistance, 500)` so existing assets keep current look.
  Files: `ScatterRenderConfig.cs`, `DensityScatterLayer.cs`, `InstanceScatterLayer.cs`.

- **Phase 2 — Cull fix across 3 engines + compute uniform** | Effort: **M**
  Replace `Mathf.Max(lod1MaxSqrDist * 4f, minCullSqr)` with `RenderCullDistance * RenderCullDistance` in `InstancedPropEngine`,
  `GrassGpuEngine`, and give `GrassRenderer` an explicit cull boundary. Both GPU engines feed `maxCullSqrDistance`
  uniform of the shared `GrassCull.compute` from `RenderCullDistance²`. Edit == play. Success: layer with cull=500 culls at 500m.
  Files: `InstancedPropEngine.cs`, `GrassGpuEngine.cs`, `GrassRenderer.cs` (compute uniform path already wired — only the value source changes; `GrassCull.compute` itself needs no edit).

- **Phase 3 — Distance bar drawer** | Effort: **M**
  Read-only segmented bar first (LOD0/LOD1/LOD2 colored + red Culled), closeness % labels, metres on hover. THEN add
  draggable handles editing transition distances + renderCullDistance via `SerializedProperty` (SSOT), mark dirty, repaint.
  Files: `TerrainScatterConfigEditor.cs` + new `Editor/ScatterStudio/LodDistanceBar.cs` (drawer), wired into
  `LayerPanelView.cs` Render card.

- **Phase 4 — Validation** | Effort: **S**
  EditMode boundary tests (one instance straddling each band edge; cull=500 → present at 499, culled at 501; edit==play).
  Migrate one existing layer asset and confirm visual parity. Files: `Tests/EditMode/ScatterLodCullTests.cs` (new).

## Feasibility

- **Reuse check:**
  - `ScatterRenderConfig` / `ScatterLod` / `LodMaxDistances` — EXISTING, extend in place (no new model).
  - Cull uniform `maxCullSqrDistance` + `SetComputeFloatParam` wiring — EXISTING in both GPU engines (`GrassGpuEngine.cs:619`, `InstancedPropEngine.cs:537`). Only the *value source* changes; the shader and the param-set call are reused unchanged.
  - CPU LOD path `GrassRenderer.SelectLod` — EXISTING; add an explicit far-cull boundary.
  - Scatter Studio render card host (`LayerPanelView.AddFoldoutCard("Render","render")`) — EXISTING; inject bar here.
- **Complexity:** moderate. The cull fix is mechanical (3 sites, one shared semantic). The draggable bar is the only fiddly part — de-risked by the read-only-first split inside Phase 3.

## Dependencies

- **Phase 1 blocks Phase 2 and Phase 3** — both consume `RenderCullDistance`. Nothing compiles against it until Phase 1 lands.
- **Phase 2 and Phase 3 are parallel-safe** (disjoint files: Runtime engines vs Editor). Sequence only if a single worktree.
- **Phase 4 blocked by Phase 2** (tests assert the cull boundary) and benefits from Phase 1 (migration parity check).
- Critical path: **1 → 2 → 4**. Phase 3 hangs off Phase 1 and can finish in parallel with 2/4.

## Backwards compatibility

- **Breaking-at-runtime risk:** existing serialized assets have `renderCullDistance == 0`. Without the Phase 1 migration default,
  EVERYTHING culls at 0 (all scatter vanishes). Migration via `OnValidate` is therefore a HARD gate before Phase 2 ships.
- **Additive otherwise:** new serialized field + new accessor + new editor drawer. No public signature removed.
- The last LOD changes meaning ("covers all remaining" → "bounded by cull"). Migration default `max(2*d1, 500)` keeps the
  effective far cull at-or-beyond the previous derived value, so visible LOD2 range does not shrink for migrated assets.

## Risk Assessment (cross-cutting)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Migration miss → existing assets cull at 0 (all scatter vanishes) | 4 | 5 | **20** | Phase 1 `OnValidate` default `max(2*d1,500)`; handle <2 LODs (fallback 500); EditMode parity test in Phase 4 BEFORE Phase 2 is declared done |
| Only one GPU engine sets `maxCullSqrDistance` → the other reads stale/garbage on the shared compute shader | 3 | 4 | **12** | Both `GrassGpuEngine` and `InstancedPropEngine` set it from `RenderCullDistance²` in the SAME commit; Phase 2 success check exercises both grass + prop layers |
| Draggable IMGUI hit-testing desyncs handle vs numeric field (SSOT break) | 3 | 3 | **9** | Read-only render FIRST; write back exclusively through `SerializedProperty` + `serializedObject.ApplyModifiedProperties`; never mutate the struct directly |
| Edit-mode still ≠ play-mode after fix (leftover `Application.isPlaying` branch) | 2 | 4 | **8** | Grep both engines for `isPlaying` / `minCullSqr` post-edit; assert in Phase 4 test that edit-built and play-built squared cull are identical |
| Odin vs UI-Toolkit host mismatch (bar drawn in wrong inspector path) | 2 | 2 | **4** | Bar is a self-contained IMGUI block hostable from both `TerrainScatterConfigEditor` (IMGUI) and `LayerPanelView` (via `IMGUIContainer`); decided in Phase 3 |
| Field-name collision with existing collider `cullDistance` on `InstanceScatterLayer` | 2 | 3 | **6** | New field named `renderCullDistance`; collider `cullDistance` left untouched; both visible in distinct Render vs Collider foldout cards |

No risk scores ≥ 15 except **migration (20)** — mandated mitigation: Phase 1 migration + Phase 4 parity check are a HARD gate; Phase 2 is not "done" until the parity test passes.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1 — Data model + migration | S | No blocker. Blocks 2 & 3. |
| Phase 2 — Cull fix (3 engines + uniform) | M | Blocked by 1. Parallel-safe with 3. |
| Phase 3 — Distance bar drawer | M | Blocked by 1. Parallel with 2; read-only-first split. |
| Phase 4 — Validation | S | Blocked by 2 (+ benefits from 1). |
| **Total** | **S+M+M+S ≈ M-L** | Critical path: **1 → 2 → 4** (Phase 3 off the critical path). |

## Unity verification note

Domain reloads are slow on this project. **Batch all edits per phase, then verify ONCE** per phase: compile (`read_console`
clean) → fresh domain reload → EditMode test or in-editor distance check. Do not edit→reload→edit per file. `GrassCull.compute`
needs no source edit (only the C# value feeding its existing uniform changes), so no shader recompile is forced by Phase 2.
