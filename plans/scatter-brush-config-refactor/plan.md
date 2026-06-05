# Plan — Scatter Brush + Config Refactor

**Created:** 2026-06-04 · **Source brainstorm:** `plans/reports/brainstorm-scatter-brush-config-refactor-20260604.md` (READ FIRST)
**Project:** GrassInteract — Unity 6000.3.13f1, URP 17.3, Mono. Reusable **library** deliverable.
**Mode:** interactive, sequential. Single live Unity editor, data deps between phases, git:false (no worktree). Not `--team`, not `--parallel`.

## Goal

Consolidate the scatter system into a **single asset hierarchy** owned by `TerrainScatterConfig` (layers + density textures + brush stamps as sub-assets), drive **all editor inspectors with Odin Inspector**, and make every property change (brush stroke, slider drag, texture swap, layer field edit) reflect in the scene **within one editor frame** without a domain reload. Add **Photoshop-style brush stamps** with a **WYSIWYG scene preview** that bakes opacity + falloff into the cursor shape.

## Locked decisions (from brainstorm — do NOT re-litigate)

1. **Texture storage** = density `Texture2D` is a sub-asset of `TerrainScatterConfig`. `ScatterLayer` is also a sub-asset. One `.asset` file per scatter project.
2. **Realtime triggers** = brush drag live preview + texture swap + any layer field edit + any config field edit, all routed through Odin `[OnValueChanged]` → direct fast-path rebuild (no `delayCall`, no domain reload).
3. **Legacy inline path** = deprecated. `ScatterField` keeps only `[config, boundTerrain]`. Inline `layers/cullCompute/indirectMaterial` are deleted.
4. **Inspector layout** = Odin `TabGroup` per layer with `[InlineProperty]` ScatterLayer; separate "Brushes" tab for the stamp library; brush controls live inside each layer's tab.
5. **Brush import scope** = grayscale stamp `Texture2D`s stored as sub-assets (`BrushStamp` SO wraps each). Procedural circular falloff stays as fallback (= null stamp).
6. **Migration** = auto on first inspector open of a legacy `ScatterField`; confirmation dialog; copies pixels into fresh sub-asset textures; leaves old loose assets untouched on disk.

## Architecture (target)

```
TerrainScatterConfig.asset
├── TerrainScatterConfig (main)
│    GPU resources, wind defaults, Layers[], BrushStamps[]
├── ScatterLayer "Grass_Tall"            (sub-asset)
│   └── R8 Texture2D densityMap          (sub-asset)
├── ScatterLayer "Flowers"               (sub-asset)
│   └── R8 Texture2D densityMap          (sub-asset)
└── BrushStamp "Soft Round"              (sub-asset)
    BrushStamp "Hard Round"              (sub-asset)
    BrushStamp "Noise"                   (sub-asset)

ScatterField (scene component)
 ├─ Required: TerrainScatterConfig config
 └─ Optional: Terrain boundTerrain
    → exposes RebuildLayer(int idx) fast-path beside full Rebuild()
```

## Naming charter (library mandate)

Generic / engine-agnostic: `BrushStamp`, `ScatterAssetMigrator`, `ScatterAssetPostprocessor`, `TerrainScatterConfigEditor`. No demo or game tokens. Stays under `GrassInteract` namespace (deliverable identity, project mandate).

## Non-regression invariants (enforced every phase)

- `GrassInteractDemo` renders **byte-stable** before vs after migration (same instance counts, same look). Verified with Unity MCP screenshot + `read_console` after each phase.
- `GrassCull.compute` cull kernels **untouched**.
- Grass GPU + CPU tier paths **unchanged** (only the data wiring changes).
- Per-phase gate = **live-editor evidence via Unity MCP** (`set_active_instance GrassInteract@<hash>`, screenshots, console clean).
- `ScatterField.Rebuild()` semantics preserved for structural changes (layer add/delete, tier swap); `RebuildLayer(int)` is the additive fast path.

## Known gotchas (carry into every phase)

- **Sub-asset hideFlags:** `AssetDatabase.AddObjectToAsset` requires the SO to NOT have `HideFlags.DontSave`. Set hideFlags to `None` before adding, then optionally `HideFlags.HideInHierarchy` after.
- **Sub-asset deletion:** must `AssetDatabase.RemoveObjectFromAsset` + `Object.DestroyImmediate`. `AssetDatabase.DeleteAsset` on a sub-asset path **silently no-ops**.
- **Texture2D sub-asset readability:** `new Texture2D(w, h, R8, mips=false)` is readable by default — DO NOT call `Apply(true)` (uploads mips and loses readability if `makeNoLongerReadable=true`). Always `Apply(updateMipmaps:false, makeNoLongerReadable:false)`.
- **Odin `[OnValueChanged]` on serialized refs:** fires AFTER deserialization, but `EditorUtility.SetDirty` on the changed SO is still required for the asset to actually save.
- **`OnValidate` ↔ Rebuild recursion:** Rebuild can SetDirty layers; that re-enters OnValidate. Use a `bool rebuilding` re-entry guard. The current `delayCall` chain is replaced with a synchronous guarded call.
- **`AssetPostprocessor.OnPostprocessAllAssets` runs for EVERY import** — filter by asset path / sub-asset parent before scanning fields, or it becomes a per-import perf hog.
- **Odin `[TabGroup]` + `[InlineProperty]`:** the inner ScatterLayer must use `[HideLabel]` to avoid double-headers; tab title comes from `layer.name`.
- **`Handles.DrawTexture` on per-frame scene paint:** allocate the tinted material once and reuse; allocating each `OnSceneGUI` causes editor frame stutter.

## Phase index

| Phase | Title | Delivers | File |
|---|---|---|---|
| 1 | Sub-asset infra + BrushStamp + ScatterLayer Odin attrs | Foundation: data model only, no UX yet | `phase-1.md` |
| 2 | ScatterField slim-down + RebuildLayer fast-path | Hot-loop rebuild path; inline fields gone | `phase-2.md` |
| 3 | Odin TerrainScatterConfigEditor (tabs + InlineProperty) | Tabbed UX, brush controls per layer, ScatterField inspector trimmed | `phase-3.md` |
| 4 | Brush stamps + WYSIWYG preview + AssetPostprocessor | Photoshop-style stamps with falloff preview; external texture edits propagate | `phase-4.md` |
| 5 | Auto-migration tool | One-click conversion of legacy demo + scenes | `phase-5.md` |

**Critical path:** 1 → 2 → 3 → 4 → 5. Each phase strictly depends on the prior. Phase 5 cannot ship until the new pipeline is the only path; running migration against an incomplete editor would corrupt assets.

## Risk Assessment (plan-level)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|---|---|---|---|
| Migration corrupts demo density data | 3 | 5 | 15 | Phase 5 isolates migration behind explicit confirmation dialog + console "rollback steps" print; runs against a Library backup of demo scene first; never edits original loose assets |
| Sub-asset pixel copy via GetPixels/SetPixels loses precision | 2 | 4 | 8 | R8 → R8 round-trip; assert pixel-equality on the copy in Phase 5 self-test; if mismatch, retry via `Graphics.CopyTexture` fallback |
| `RebuildLayer` desyncs from full `Rebuild` (e.g. tier choice baked at full but not per-layer) | 3 | 4 | 12 | Extract tier-selection into a single helper called by both paths; Phase 2 ships a fast-path harness that diffs engine count + bounds between `Rebuild` and N×`RebuildLayer` |
| Odin `[InlineProperty]` on sub-asset refs forbids deep nesting | 2 | 4 | 8 | Phase 3 prototype on a throwaway SO first; fall back to `[ShowInInspector] ScatterLayerProxy` wrapper if attribute combination breaks |
| AssetPostprocessor rebuild storm on bulk reimport | 3 | 3 | 9 | Phase 4 batches rebuild calls inside `EditorApplication.delayCall` deduplicated by ScatterField instance ID; postprocessor only marks fields dirty, doesn't call Rebuild directly |
| Removing inline `cullCompute/indirectMaterial` breaks any non-demo scenes using ScatterField | 2 | 3 | 6 | Migration step covers it; surface a clear `[Required]` HelpBox if `config == null` on a ScatterField; document migration in plan and in HANDOFF.md |
| Brush stamp WYSIWYG preview hits a per-frame allocation regression | 2 | 3 | 6 | Phase 4 caches `Material` + tinted `Texture2D` per stamp; Profiler check at end of phase |

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Sub-asset infra + BrushStamp + ScatterLayer Odin attrs | M (3d) | Foundation; data model only |
| Phase 2: ScatterField slim-down + RebuildLayer fast-path | M (3d) | Risk: tier-selection extraction |
| Phase 3: Odin TerrainScatterConfigEditor | L (1wk) | Largest UX surface; Odin attr discovery |
| Phase 4: Brush stamps + WYSIWYG preview + postprocessor | M (3d) | Scene-view textured preview is the unknown |
| Phase 5: Auto-migration | S (1d) | Surgical; gated by full pipeline working |
| **Total** | **~2.5 weeks** | Critical path: 1 → 2 → 3 → 4 → 5 |

## Cook Handoff

After approval, run:

```
/t1k:cook plans/scatter-brush-config-refactor/plan.md
```

Single-agent sequential execution. Approval gate between every phase. Verify with Unity MCP after each phase before unlocking the next.
