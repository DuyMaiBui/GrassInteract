# Phase 3 — Deletes + repoint seams

**Effort:** M · **Wave:** C (sequential) · **Depends on:** P2 (green compile) · **Blocks:** P4

## Goal

Delete the duplicate renderer, the absorbed scatter MonoBehaviour, the bridge, the scene-hijacking validation-scene builder, and all stale/legacy files tied to them. Repoint the sculpt-tool seam accessors + `WorldPainterMigration` to `WorldPainter` (or delete if legacy-only). **No migration converter** — authored data is disposable.

## Pre-delete reference grep (MANDATORY before any delete)

For EACH type below, run `grep -rln "\bTypeName\b" Assets/WorldPainter --include=*.cs | grep -v .meta` and confirm only doc-comment mentions remain (no live `using`/instantiation/field). Update every live reference BEFORE deleting. (Per `development-principles.md` § Pre-Delete Reference Check.)

## Files to delete

| File | Why dead | Verified consumer to repoint first |
|------|----------|-----------------------------------|
| `Runtime/Render/GpuTerrainRenderer.cs` | dup renderer; WorldPainter.Render is sole | `TerrainTileAssetEditor.cs`, `WorldPainterMigration.cs`, `TerrainValidationSceneBuilder.cs` |
| `Runtime/Scatter/ScatterField.cs` | absorbed into WorldPainter.Scatter (P2) | `WorldPainterScatterLayerCard.cs`, `WorldPainterMigration.cs`, `WorldPainter.cs` (done P2) |
| `Runtime/Render/GpuTerrainScatterGround.cs` | grounding internalized (P2) | doc-comment mentions only after P2 |
| `Editor/Import/TerrainValidationSceneBuilder.cs` | scene-hijack culprit (`NewScene Single` @ line 205); replaced by P4 factory | `WorldPainterCoachMarks.cs` (legacy "Create 1x1 tile" button) |
| validation scene asset (the `.unity` it builds) | net-new fresh demo replaces it (P9) | none |
| stale/legacy files tied to the above (grep-driven) | dead after repoints | enumerate during grep pass |

## File-ownership groups (sequential within phase — order matters)

**G3.1 — Repoint authoring seams (do BEFORE deletes)**
- `Assets/WorldPainter/Editor/Brush/TerrainPaintTargetResolver.cs` + `WorldPainterSculptTool*.cs` — point seam accessors at `WorldPainter` + `WorldMapAsset` (not `GpuTerrainRenderer`).
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterScatterLayerCard.cs` — drop `ScatterField` references; read layers from `WorldMapAsset`.
- `Assets/WorldPainter/Editor/Inspector/TerrainTileAssetEditor.cs` — drop `GpuTerrainRenderer` reference.
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterCoachMarks.cs` — remove legacy "Create 1x1 tile" button (factory replaces it in P4).
- `Assets/WorldPainter/Editor/Migration/WorldPainterMigration.cs` — repoint to WorldPainter, **or delete if legacy-only** (decide by grep: if it only converts old `GpuTerrainRenderer` scenes, delete).

**G3.2 — Deletes (only after G3.1 green)**
- Delete the 4+ files in the table (+ `.meta`s) + the validation `.unity` + grep-identified stale files.

## Parallelizable vs sequential

**Sequential within phase:** G3.1 (repoint) MUST land + compile green BEFORE G3.2 (delete). Deleting before repoint breaks the build. Single subagent owns the whole phase.

## Verification

1. **Pre-delete grep** per type → zero live references.
2. **Compile after G3.1** (repoint) — `read_console` clean + `run_tests`.
3. **Compile after G3.2** (delete) — `read_console` clean + `run_tests`. `refresh_unity(force, all)` if only `.meta`/asmdef changed.
4. Confirm no `MissingReferenceException` / broken `.meta` GUID warnings in console.

## Success criteria

- `GpuTerrainRenderer.cs`, `ScatterField.cs`, `GpuTerrainScatterGround.cs`, `TerrainValidationSceneBuilder.cs` + validation scene + stale files **deleted**.
- Sculpt-tool seam + scatter card repointed to WorldPainter/`WorldMapAsset`.
- `WorldPainterMigration` repointed or deleted (grep-justified).
- Project compiles; all EditMode tests green; no missing-reference console errors.
