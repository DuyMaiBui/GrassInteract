# Phase 2 — Target asmdef + Folder Design Lock + Reference-Check Pass

**Effort:** S · **Blocked by:** P1 · **Compiles at boundary:** n/a

## Goal
Lock the exact target tree (plan §2.2) and run the pre-delete reference check for EVERY delete target so P6/P7 are mechanical.

## Deliverable
1. Final folder map (confirm plan §2.2 against P1 inventory — adjust feature buckets if a file doesn't fit).
2. Per-delete-target ref-check record: for each file in §3.2, the exact list of callers and whether each caller is itself DELETE (no action) or KEEP (must sever/repoint the ref before delete).

## Steps
1. Pin instance.
2. For each DELETE target: `grep -rln "\bSymbol\b" Assets/` across runtime+editor+tests+`*.unity`+`*.prefab`. Record callers.
3. Classify each caller: DELETE-too (fine) vs KEEP (needs repoint — list the repoint target, e.g. `WorldPainterScatterLayerCard`'s `ScatterField` ref STAYS because E1 keeps ScatterField).
4. Confirm the 4 KEEP→ScatterField refs (`WorldPainter.cs`, `GpuTerrainScatterGround`, `WorldPainterMigration`, `WorldPainterScatterLayerCard`) all survive (E1).
5. Confirm `TerrainScatterConfig.asset` (demo) consumers — KEEP the type if WorldPainter migration reads it.

## Verification
Every DELETE target has a complete caller list with a KEEP/DELETE verdict per caller and a repoint plan for KEEP callers. No code change.

## Rollback
n/a (document only).
