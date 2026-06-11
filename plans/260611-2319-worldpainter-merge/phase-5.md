# Phase 5 — Move + Merge Tests (GATE: 301 green)

**Effort:** M · **Blocked by:** P4 · **Compiles at boundary:** YES

## Goal
Create the single `WorldPainter.Tests` asmdef and `git mv` all test KEEP files from both old test assemblies into `WorldPainter/Tests/Editor/`. The old cross-asm test references collapse to intra-assembly. Old namespaces retained.

## File ownership
- CREATE: `Assets/WorldPainter/Tests/Editor/WorldPainter.Tests.asmdef` (references `WorldPainter`, `WorldPainter.Editor`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Unity.Collections`; precompiled `nunit.framework.dll`; `overrideReferences:true`, `autoReferenced:false`; defineConstraint `UNITY_INCLUDE_TESTS`).
- `git mv` ALL test `.cs` from `GpuTerrain/Tests/Editor` + `GrassInteract/Tests/Editor` → `WorldPainter/Tests/Editor/`.
- The 4 straddle tests (E3 `DensityBrushMathTests`, E4 `TerrainSculptUndoTests`/`TerrainSculptRtWritebackTests`/`TerrainBrushPreviewTests`) MOVE here unchanged for now — they still compile because their DELETE-target classes are in `_pending-delete/` (P4) and the `TerrainBrush*` math contract is KEEP. Their migrate/drop decision is P6/P7.
- Remove `GpuTerrain.EditorTests.asmdef`, `GrassInteract.EditorTests.asmdef` + metas.

## Steps (ATOMIC)
1. Pin instance; resolve packages (guard against stale `packages-lock.json` truncating discovery).
2. Create `WorldPainter.Tests.asmdef`.
3. `git mv` all test files; remove old test asmdefs.
4. `refresh_unity(force)` → DLL watch → `read_console` → `run_tests`.

## Verification — GATE
`run_tests` reports the **FULL 301** (assert the count, not "green" — a truncated 89-discovery is the stale-lock trap). Exactly 3 asmdefs: `WorldPainter`, `WorldPainter.Editor`, `WorldPainter.Tests`. No old asmdef remains.

## Rollback
`git reset --hard <P4 sha>`.

## Risk
R7 (stale-lock truncation) — assert full count. R2 (.meta).
