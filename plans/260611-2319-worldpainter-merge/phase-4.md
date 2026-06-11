# Phase 4 — Move + Merge Editor (ATOMIC)

**Effort:** M · **Blocked by:** P3 · **Compiles at boundary:** YES

## Goal
Create `WorldPainter.Editor` asmdef and `git mv` all editor KEEP files (both old editor assemblies) into `WorldPainter/Editor/<feature>/`. Old namespaces retained.

## File ownership
- CREATE: `Assets/WorldPainter/Editor/WorldPainter.Editor.asmdef` (references `WorldPainter`, `includePlatforms:[Editor]`).
- `git mv` all `WorldPainter*` + `WorldPainter/*` editor files → `Editor/WorldPainter/`.
- `git mv` `BrushFalloffLut`, `TerrainPaintTargetResolver`, `TileRtCache`, `TerrainSculptRtWriteback` → `Editor/Brush/`.
- `git mv` `WorldPainterInspector`, `TerrainTileAssetEditor` → `Editor/Inspector/`.
- `git mv` `TerrainTileImporter`, `TerrainValidationSceneBuilder` → `Editor/Import/`.
- `git mv` `WorldPainterMigration` → `Editor/Migration/`.
- `git mv` `WorldPainter.uss`/`WorldPainterLight.uss` → `Editor/Resources/`.
- Merge assembly-info files into one `Editor/AssemblyInfo.cs`.
- DO NOT move DELETE-target editor files yet (they die in P6/P7) — but they currently live in the old `GpuTerrain.Editor`/`GrassInteract.Editor` assemblies. Move the KEEP editor files OUT, leaving the delete-targets behind in the (still-present) old editor asmdefs.
- Remove old editor asmdefs ONLY after their remaining (delete-target) files are also relocated OR the old asmdef is repointed. Simplest: move delete-targets into a temporary holding under the new `WorldPainter.Editor` too (they compile, then get deleted in P6/P7), then drop the 2 old editor asmdefs.

## Steps (ATOMIC)
1. Pin instance.
2. Create `WorldPainter.Editor.asmdef`.
3. `git mv` editor KEEP files per ownership; `git mv` the delete-target editor files into `WorldPainter/Editor/_pending-delete/` (still compiles under old namespaces; flagged for P6/P7).
4. Remove `GpuTerrain.Editor.asmdef`, `GrassInteract.Editor.asmdef` + metas.
5. Repoint the 2 test asmdefs (still old) to reference `WorldPainter.Editor` instead of the gone editor asmdefs.
6. `refresh_unity(force)` (touch a `.cs` — asmdef-only edits no-op) → DLL watch → `read_console` → `run_tests` (301).

## Verification
301 green; only `WorldPainter` + `WorldPainter.Editor` + the 2 (soon-dead) test asmdefs exist; moves are renames.

## Rollback
`git reset --hard <P3 sha>`.

## Risk
R2 (.meta), R3 (namespace untouched → safe). `_pending-delete/` holding folder makes P6/P7 deletions obvious and keeps P4 compiling.
