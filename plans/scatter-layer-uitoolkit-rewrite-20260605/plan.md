# Plan -- Scatter Layer Editor: Pure UIToolkit Rewrite

- Date: 2026-06-05
- Mode: /t1k:plan --auto (single-developer cook target)
- Design contract (LOCKED): plans/reports/brainstorm-scatter-layer-uitoolkit-rewrite-20260605.md

---

## Goal

Replace the Odin-driven TerrainScatterConfig / ScatterLayer inspectors with a pure UIToolkit editor that splits per-concern fields between DensityScatterLayer and InstanceScatterLayer, drops ScatterKind (engine routes via InteractsWithDeform), collapses grass+mesh material to single material, auto-creates sub-assets on +Density/+Instance, presents layers as a tile grid, adds drag-prefab + scene-overlay tooling for Instance editing, branded USS theme, pooled MeshColliders, clean-break migration.

## Success criteria

- Editor compiles clean (zero errors, zero warnings introduced by rewrite).
- grep -r Sirenix.OdinInspector Assets/GrassInteract/Runtime/ returns ZERO hits.
- grep -r ScatterKind Assets/GrassInteract/ returns ZERO hits.
- grep -r grassMaterial Assets/GrassInteract/Runtime/ScatterLayer.cs returns ZERO hits.
- +Density click renders a layer with white R8 texture + Default_Material + LOD0 mesh in <= 1s.
- +Instance click renders a layer with empty AuthoredInstancesData + Default_Material + LOD0 mesh in <= 1s.
- Inspector renders identically in light + dark editor theme.
- Smoke: create 1 Density + 1 Instance, paint density, drop a prefab, Place mode click-drag, enter Play -> instances render through correct engine.
- EDITOR-UI-GUIDE.md exists at Assets/GrassInteract/Editor/UI/EDITOR-UI-GUIDE.md.

## Resolved decisions (final -- locked by orchestrator)

These 3 items were resolved by the user after the initial planning pass. Recorded here for traceability; the per-phase files reflect these decisions throughout.

| # | Topic | Final decision | Propagation |
|---|---|---|---|
| D1 | AuthoredInstanceRecord schema | STRICT brainstorm §2: position + rotation + scale + per-record collider (colliderOverride mesh ref, colliderScale, generateCollider, colliderConvex). RendererOverride REMOVED entirely. | Phase A: blob is rewritten V1->V2 one-shot (no Version-byte coexistence); V1 records' RendererOverride is dropped. Phase A: `MeshScatterEngine.BuildMaterialGroups` slow-path REMOVED. Phase E: record-row + detail-panel drop renderer override UI. |
| D2 | Default seed assets path | `Assets/GrassInteract/Editor/Defaults/` (editor-only seeds, copied into user space on layer creation) | Phase B: folder moves from Runtime path to Editor path. Phase C/D/F: every default-asset lookup loads from `Editor/Defaults/`. Seed assets are NOT shipped at runtime -- they are templates that the editor copies into user-owned sub-assets. |
| D3 | DensityPaintWindow backend | Working byte[] buffer + per-stroke Undo + commit on Apply/Close. Undo stack cap = 32 strokes. | No change from initial plan. Phase F implements as specified. |

D1 implies a data-loss event for any existing user assets that carried RendererOverride records. Mitigation: a one-shot console warning during migration enumerating affected records (logged once per layer in the V1->V2 readback path). Documented in CHANGELOG entry (Phase I).

## Phase index

| # | Phase | Effort | Owner | Parallel-safe? |
|---|---|---|---|---|
| A | Runtime types cleanup + migration deletion | M | runtime/* + ScatterField + delete legacy editor files | No (root) |
| B | Editor foundations -- UI asmdef, USS root, components scaffold, Editor/Defaults/ seed assets | M | Editor/UI/* (new) + Editor/Defaults/ | After A |
| C | TerrainScatterConfigEditor -- header + tile grid + layer creation flow | M | TerrainScatterConfigEditor + tile components | After B |
| D | DensityScatterLayerEditor -- sections | M | DensityScatterLayerEditor + section UXML | Parallel E after C |
| E | InstanceScatterLayerEditor -- list + drop + detail + defaults | L | InstanceScatterLayerEditor + record components | Parallel D after C |
| F | LOD section + DensityPaintWindow | M | LodDistanceBar/LodCard + DensityPaintWindow | Parallel G after D/E |
| G | Scene overlay + Mode toolbar + Place/Erase | L | InstancePlacementOverlay + ScatterBrush rewrite | Parallel F after E |
| H | Instance runtime -- Pool + FrustumCuller | S | Runtime/Instance*.cs + wiring | Parallel F/G after A |
| I | Polish + EDITOR-UI-GUIDE.md + final validation | S | docs + theme audit | Terminal |

## Dependency graph

A is root. B depends on A. C depends on B. D and E depend on C (parallel-safe). F depends on D and E. G depends on E. F and G are parallel-safe. H depends on A only. I depends on everything.

Critical path: A -> B -> C -> E -> G -> I.

## File ownership map (per-phase, no overlap)

| File | Phase |
|---|---|
| Runtime/ScatterLayer.cs | A |
| Runtime/DensityScatterLayer.cs | A |
| Runtime/InstanceScatterLayer.cs | A |
| Runtime/AuthoredInstancesData.cs | A |
| Runtime/TerrainScatterConfig.cs | A |
| Runtime/MeshScatterEngine.cs | A |
| Runtime/ScatterField.cs | A (D4) |
| Runtime/GrassCpuEngine.cs | A (read-only verify) |
| Runtime/GrassGpuEngine.cs | A (read-only verify) |
| Runtime/InstanceColliderPool.cs (NEW) | H |
| Runtime/InstanceFrustumCuller.cs (NEW) | H |
| Editor/ScatterAssetMigrator.cs | A (DELETE) |
| Editor/MigrateScatterLayerTypes.cs | A (DELETE) |
| Editor/MigrateDeformModeToWindInteract.cs | A (DELETE) |
| Editor/ScatterFieldRebuildLayerHarness.cs | A (DELETE) |
| Editor/ScatterAssetPostprocessor.cs | A (extend) + C (naming convention; sequenced) |
| Editor/TerrainScatterConfigEditor.cs | C (full rewrite) |
| Editor/ScatterFieldEditor.cs | C (full rewrite) |
| Editor/ScatterBrush.cs | G (full rewrite) |
| Editor/DensityScatterLayerEditor.cs (NEW) | D |
| Editor/InstanceScatterLayerEditor.cs (NEW) | E |
| Editor/UI/Components/*.cs (NEW) | B scaffold -> C/D/E/F/G fill |
| Editor/UI/UXML/*.uxml (NEW) | B root -> C/D/E/F/G per-section |
| Editor/UI/USS/*.uss (NEW) | B |
| Editor/UI/Icons/*.png (NEW) | B |
| Editor/UI/DensityPaintWindow.cs (NEW) | F |
| Editor/UI/InstancePlacementOverlay.cs (NEW) | G |
| Editor/Defaults/Default_Material.mat (NEW) | B |
| Editor/Defaults/Default_LOD0_Grass.mesh (NEW) | B |
| Editor/Defaults/Default_LOD0_Prop.mesh (NEW) | B |
| Editor/Defaults/Default_DensityMap_512_white.png (NEW) | B |
| Demo/TerrainScatterConfig.asset + sub-assets | A (DELETE) |
| Editor/UI/EDITOR-UI-GUIDE.md (NEW) | I |

Note: ScatterAssetPostprocessor is the only file touched by two phases. A and C edits are SEQUENCED (A first). A extends type detection, C adds a separate naming-convention method.

## Risk Assessment (MANDATORY)

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| AuthoredInstancesData V1->V2 one-shot blob rewrite drops RendererOverride from existing assets (intentional data loss per D1) | 3 | 4 | 12 | Strict V2 reader logs one console warning per layer listing dropped RendererOverride record indices. CHANGELOG entry (Phase I) flags as BREAKING. EditMode test in Phase A covers fresh V2 round-trip + V1->V2 readback path (verifies position/rotation/scale/colliderOverride preserved; RendererOverride dropped without exception). |
| V1->V2 migration collapses non-uniform record scale to uniform average (per D1 strict-V2 §2 -- record `scale` is `float`, not `Vector3`) | 3 | 3 | 9 | Phase A.4 step 5 emits one-shot per-layer warning enumerating affected record indices. Phase A.0 test covers a non-uniform V1 case. Documented as BREAKING in CHANGELOG (Phase I). |
| MeshScatterEngine.BuildColliders removal breaks Play-mode collision | 3 | 4 | 12 | Phase H ships pool + culler BEFORE Phase A removes the in-engine collider spawn. If H lands after A, gate on Play-mode raycast hit smoke. |
| UIToolkit binding pitfalls (stale binds, GC leaks) | 4 | 3 | 12 | Phase B ships BindablePanel base wiring SerializedObject lifetime. Phase I runs Memory Profiler 1-minute capture. |
| Scene-view Overlay quirks (mode persistence across domain reload) | 3 | 3 | 9 | Phase G uses Overlay attribute + Tools.current shadow-mode + named EditorPref keys. |
| Light-theme contrast violations | 4 | 2 | 8 | Phase B ships USS tokens with light/dark from day 1. Phase I screenshots every panel in both themes. |
| Drag-prefab import depth ambiguous (deep hierarchies) | 3 | 3 | 9 | Phase E hard-caps at 1024 transforms with warning dialog. Document in EDITOR-UI-GUIDE. |
| ScatterField.cs (D4) missed -> compile failure after Phase A | 5 | 5 | **25** | D4 resolution: ScatterField is in Phase A list. Exit criterion = GrassInteract*.dll rebuilt zero errors. |
| Per-stroke Undo on DensityPaintWindow accumulates memory | 2 | 3 | 6 | Cap Undo stack at 32 strokes; collapse oldest into Paint baseline. |
| LOD switch handles cross each other on drag | 3 | 2 | 6 | Clamp handle N min to (N-1).value + 0.5m epsilon, max to (N+1).value - epsilon. |
| Serializable removal from runtime types breaks scene refs | 2 | 4 | 8 | Keep Serializable on data structs. FormerlySerializedAs shims on every moved field. |

**Risk score >= 15 -> mandate mitigation before phase starts.** After D1 strict-V2 resolution, only ONE risk >= 15 remains: ScatterField cascade (25), addressed by Phase A.7 with a compile-clean exit criterion. Former 15-rated schema-migration risk drops to 12 because the alternative-data-preservation requirement is gone (RendererOverride loss is intentional). Phase A still opens with the V1->V2 readback smoke test.

## Validation gates (every phase)

1. Compile clean -- refresh_unity returns compile_succeeded=true.
2. No Odin in runtime -- grep -rln Sirenix Assets/GrassInteract/Runtime/ empty.
3. Scene re-author flag -- if scenes break, log [t1k:lesson] marker + CHANGELOG.md note.
4. Light + dark theme parity -- screenshots in both themes (Phase I).
5. Smoke-test steps -- each phase file lists explicit reproducible commands.
6. 150K commit checkpoint -- commit before summary, per agent-completion-discipline.md.

## Timeline

| Phase | Effort | Notes |
|---|---|---|
| A | M | Root; HIGH-RISK gates. |
| B | M | Pure scaffolding. |
| C | M | Tile grid + creation flow. |
| D | M | Parallel-safe with E. |
| E | L | Parallel-safe with D. |
| F | M | Parallel-safe with G. |
| G | L | Parallel-safe with F. |
| H | S | Independent of B-G after A. |
| I | S | Terminal gate. |

Critical path A->B->C->E->G->I. Total wall-time: 2L + 4M + 1S serial. Single-dev assumed serial.

## Behavioral checklist (orchestrator verifies)

- [x] Data flows traced (record -> blob -> engine; click -> list -> blob).
- [x] Dependency graph -- A blocks all; B blocks C-G; H independent.
- [x] Risk assessment -- two >=15 risks with in-phase mitigations.
- [x] Backwards compatibility -- clean break documented; FormerlySerializedAs shims preserved.
- [x] Test matrix -- every phase file has smoke steps + validation section.
- [x] Rollback plan -- per-phase commits; revert is git revert HEAD.
- [x] File ownership -- no overlap (ScatterAssetPostprocessor sequenced).
- [x] Success criteria -- reproducible commands or measurable outcomes.

## References

- Brainstorm (contract): plans/reports/brainstorm-scatter-layer-uitoolkit-rewrite-20260605.md
- Predecessor brainstorms: 20260604 (3 files)
- Mono spawn rule: .claude/rules/mono-pool-spawn-unity.md (Phase H pool MUST use IObjectPoolManager pattern)

## Phase files

- phase-A-runtime-cleanup.md
- phase-B-editor-foundations.md
- phase-C-config-editor-and-tile-grid.md
- phase-D-density-editor.md
- phase-E-instance-editor.md
- phase-F-lod-section-and-paint-window.md
- phase-G-scene-overlay-and-mode-toolbar.md
- phase-H-instance-runtime-pool-and-culler.md
- phase-I-polish-docs-and-final-validation.md
