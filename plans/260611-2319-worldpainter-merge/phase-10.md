# Phase 10 — Final Full-Suite Verify + Cleanup + Commit (FINAL GATE)

**Effort:** S · **Blocked by:** P9 · **Compiles at boundary:** YES

## Goal
Prove the merge is complete and clean: 3 asmdefs, zero old namespaces, full test count green, both demo scenes gone, authoring absent from player build, GUIDs preserved, coach-marks bootstrap still works. Commit + push.

## Steps
1. Pin instance; resolve packages (guard stale-lock truncation).
2. `refresh_unity(force)` → `read_console` clean → `run_tests` → assert FULL expected count (301 minus any itemized, user-confirmed obsolete straddle tests dropped in P6/P7).
3. Cleanup: remove stray folder `.meta` for emptied old dirs (`Assets/GpuTerrain.meta`, `Assets/GrassInteract.meta`, old `Demo`/`Editor`/`Tests` dirs — BOTH `Demo` folders are now empty after P7's scene sweep); `_pending-delete/` folder removed.
4. **Build-isolation check (SC6):** confirm `WorldPainter.Editor` asmdef `includePlatforms:[Editor]`; grep that no authoring type (`WorldPainterSculptTool`, `*Authoring`, UI views, `TerrainValidationSceneBuilder`) is referenced from any runtime `.cs`; optionally trigger a player-build-target compile and confirm authoring symbols absent.
5. **GUID-preservation check (SC7):** `git log --stat --follow` / `git diff --summary` shows moves as renames (R≈100).
6. **Coach-marks bootstrap smoke-check (SC8 — E5 regression):** with no tiles registered, confirm the WorldPainter empty-state "Create 1×1 tile" button still invokes `TerrainValidationSceneBuilder` and generates a tile under the retargeted (new-tree/temp) path without error. (Math/logic is test-covered; this is the one editor-side smoke-check the merge introduces — surfaced because E5 deleted the pre-baked scene the button used to rely on.)

## Verification — FINAL GATE (all SC must pass)
- SC1: exactly 3 asmdefs (`find Assets -name '*.asmdef'`).
- SC2: zero old namespaces/usings (`grep -rn`).
- SC3: full test count green.
- SC4: all §3.2 deletes gone — incl. BOTH demo scenes + the 4 TerrainValidation assets; `read_console` clean. (`find Assets -name '*.unity'` → neither old scene remains.)
- SC5: no file >200 lines without justification (`wc -l`).
- SC6: authoring absent from runtime/player build.
- SC7: moves are renames (GUIDs preserved).
- SC8: coach-marks "Create 1×1 tile" bootstrap still works post-merge.

## Commit + push
Per `.claude/rules/agent-completion-discipline.md` — commit each phase as it lands; final commit here. Conventional: `refactor(worldpainter): merge GpuTerrain+GrassInteract into unified WorldPainter assembly`. Push to `plan/gpu-terrain-cdlod`.

## Rollback
Whole effort revertible phase-by-phase; final state is one clean fast-forward of the mechanical phases.
