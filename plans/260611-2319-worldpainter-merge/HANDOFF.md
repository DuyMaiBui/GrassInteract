# Handoff — WorldPainter merge (in progress)

## Status: P3–P5 COMPLETE + green. P6–P10 remaining.

**Branch:** `plan/gpu-terrain-cdlod` (not pushed yet). Working tree clean after each phase commit.

## Done (committed, each gated 301/301 EditMode green)
- `3821887` docs: fixed plan R2 .meta-pairing guidance (attempt-#1 lost GUIDs).
- `7940546` **P3** — created `WorldPainter` runtime asmdef; moved 71 runtime .cs + shaders (GUIDs preserved via paired .cs/.meta git mv); InternalsVisibleTo consolidated into `WorldPainter/Runtime/AssemblyInfo.cs`; old runtime asmdefs gone.
- `95cb9aa` **P4** — `WorldPainter.Editor` assembly; editor KEEP files → `WorldPainter/Editor/{Brush,Inspector,Import,Migration,WorldPainter,Resources}`; old authoring delete-targets shunted to `WorldPainter/Editor/_pending-delete/` (NOT yet deleted — that's P6/P7); old editor asmdefs gone.
- `38b6f79` **P5** — `WorldPainter.Tests` assembly; all 36 tests merged in; old test asmdefs gone. **SC1 reached: exactly 3 asmdefs.**

## Key state facts
- Target 3-assembly structure achieved: `WorldPainter` / `WorldPainter.Editor` / `WorldPainter.Tests`.
- **Namespaces still OLD** (`GpuTerrain`/`GrassInteract`) — rename is P9 (atomic).
- `WorldPainter/Editor/_pending-delete/` holds the old authoring to delete in P6 (terrain) + P7 (scatter). Contents: old terrain sculpt editor (`GpuTerrainRendererEditor*`, `TerrainSculpt{Tool,State,Config,Undo}`, `TerrainBrush{Stroke,Preview}`) + ScatterStudio + scatter authoring + `DensityPaintBrush.shader` + `ScatterBrushLibrary.asset`.
- `Assets/GrassInteract/Demo/GrassInteractDemoEffector.cs` is now orphaned into `Assembly-CSharp` (compiles via auto-ref WorldPainter) — P7 deletes it with `GrassInteractDemo.unity`.
- Test assembly for `run_tests` is now **`WorldPainter.Tests`** (301 tests).

## Remaining
- **P6** — delete old terrain authoring from `_pending-delete`. FIRST: E3 port `DensityPaintGPU.ComputeStampPositions` math into WorldPainter + repoint 8 `DensityBrushMathTests` cases; E4 migrate `TerrainSculptUndoTests` + `TerrainBrushPreviewTests` to WorldPainter equivalents (note: `TerrainSculptRtWriteback` is KEEP — its test stays). Per-target ref check before each delete. Gate 301 (minus only genuinely-obsolete, itemized).
- **P7** — delete legacy scatter authoring + both demo scenes (`GrassInteractDemo.unity` + `TerrainValidation.unity` + 4 validation assets + GrassInteractDemoEffector). KEEP `ScatterField`/`GrassScatter` runtime (E1) + `TerrainValidationSceneBuilder` (E5).
- **P8** — consolidate `GpuTerrainRenderer` vs `WorldPainter.Render` parallel classes; split oversized files (§3.4, esp. `WorldPainterSculptTool.Stroke.cs` 349 lines); retarget `TerrainValidationSceneBuilder` generator output path to new tree.
- **P9** — ATOMIC namespace rewrite `GpuTerrain`/`GrassInteract` → `WorldPainter` across all surviving files + asmdef `rootNamespace`. Single batch, verify once.
- **P10** — final full-suite + SC checks (SC2 no old namespaces, SC6 player-build authoring-isolation, SC8 coach-marks button smoke) + push.

## Execution notes (IMPORTANT)
- Delegated `t1k-unity-developer` agents TRUNCATED twice mid-task on P3 (tail-of-thought stop before commit). Reliable pattern: **orchestrator drives mechanical edits via scripted git mv (paired .cs+.meta) + self-runs the Unity gate via MCP**. Use that for remaining phases.
- Unity gate: `set_active_instance("GrassInteract@de203215")` → `refresh_unity(force)` → background-poll `Library/ScriptAssemblies/*.dll` mtime until stable → `read_console` (errors) → `run_tests(assembly_names=["WorldPainter.Tests"])` → assert 301 full count.
- File move discipline: ALWAYS move `.cs` AND `.cs.meta` as a pair BEFORE any refresh_unity (else Unity regenerates GUIDs — this broke attempt #1).
