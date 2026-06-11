# Phase 10 — Final Full-Suite Verify + Cleanup + Commit (FINAL GATE)

**Effort:** S · **Blocked by:** P9 · **Compiles at boundary:** YES

## Goal
Prove the merge is complete and clean: 3 asmdefs, zero old namespaces, 301 green, authoring absent from player build, GUIDs preserved. Commit + push.

## Steps
1. Pin instance; resolve packages (guard stale-lock truncation).
2. `refresh_unity(force)` → `read_console` clean → `run_tests` → assert FULL 301 (or the user-confirmed itemized count if any obsolete straddle tests were dropped in P6).
3. Cleanup: remove stray folder `.meta` for emptied old dirs (`Assets/GpuTerrain.meta`, `Assets/GrassInteract.meta`, old `Demo`/`Editor`/`Tests` dirs); `_pending-delete/` folder removed (its files deleted in P6/P7).
4. **Build-isolation check (SC6):** confirm `WorldPainter.Editor` asmdef `includePlatforms:[Editor]`; grep that no authoring type (`WorldPainterSculptTool`, `*Authoring`, UI views) is referenced from any runtime `.cs`; optionally trigger a player-build-target compile and confirm authoring symbols absent.
5. **GUID-preservation check (SC7):** `git log --stat --follow` / `git diff --summary` shows moves as renames (R≈100).

## Verification — FINAL GATE (all SC must pass)
- SC1: exactly 3 asmdefs (`find Assets -name '*.asmdef'`).
- SC2: zero old namespaces/usings (`grep -rn`).
- SC3: full test count green.
- SC4: all §3.2 deletes gone, `read_console` clean.
- SC5: no file >200 lines without justification (`wc -l`).
- SC6: authoring absent from runtime/player build.
- SC7: moves are renames (GUIDs preserved).

## Commit + push
Per `.claude/rules/agent-completion-discipline.md` — commit each phase as it lands; final commit here. Conventional: `refactor(worldpainter): merge GpuTerrain+GrassInteract into unified WorldPainter assembly`. Push to `plan/gpu-terrain-cdlod`.

## Rollback
Whole effort revertible phase-by-phase; final state is one clean fast-forward of the 8 mechanical phases.
