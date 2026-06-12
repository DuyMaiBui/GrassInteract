# Handoff — WorldPainter merge (in progress)

## Status: P3–P7 COMPLETE + green (296 tests). P8–P10 remaining.

### P7 done (commit `4fc1d2a`) — 296/296 green
Deleted ALL scatter authoring (ScatterStudio + DensityPaint* + Scatter* editors) + both demo scenes (GrassInteractDemo + TerrainValidation + assets). KEEP: ScatterField/GrassScatter runtime + TerrainValidationSceneBuilder. Ported `DensityPaintGPU.ComputeStampPositions` → new `WorldPainterStampMath` (Editor/Brush, KEEP); repointed its 5 tests. Dropped 5 GPU/decal tests of deleted code (documented). 301→296.

### P8 remaining work (consolidation + SOLID + reorg) — DETAILED
1. **Parallel-island delete** (`_pending-delete/` now holds ONLY `TerrainBrushStroke.cs`, `TerrainSculptState.cs`, `TerrainSculptUndo.cs`): migrate the shared `SculptMode` enum (defined in `TerrainBrushStroke.cs`) + `TerrainSculptState.ModeColor` into a KEEP home (`WorldPainterState` has NO equivalent — add `SculptMode`+`ModeColor` there, OR keep a slimmed mode-only file). Repoint the 6 `ModeColor`/`SculptMode` cases in `TerrainBrushMathTests` (lines ~219-266). Then **drop `TerrainSculptUndoTests`** (11 cases, fully redundant — `WorldPainterUndoTests` already mirrors them vs KEEP `WorldPainterUndo`). Then `git rm` the 3 island files. Net test delta: -11 (TerrainSculptUndoTests) → ~285; verify ModeColor cases survive repointed.
2. **GpuTerrainRenderer consolidation** (§3.3): `WorldPainter.Render.cs` "mirrors GpuTerrainRenderer". Decide keep-both-with-delegation vs fold-in. Consumers: `WorldPainter.Render`, `TerrainTileAssetEditor`, `TerrainValidationSceneBuilder` (AddComponent), `WorldPainterMigration`. ALSO fix the DEAD hardcoded path in `GpuTerrainRenderer.cs:117` `AssetDatabase.LoadAssetAtPath<Material>("Assets/GpuTerrain/Materials/TerrainPatch.mat")` — that material does NOT exist anywhere (pre-existing missing-asset LogError, NOT a regression); update/remove the path.
3. **Oversized-file splits** (plan §3.4): `WorldPainterSculptTool.Stroke.cs` (349), `WorldPainterUndo.cs` (269), `WorldPainterLayerStackView.Mutations.cs` (256), `WorldPainterSculptTool.cs` (247), `WorldPainterLayerStackView.cs` (246), `WorldPainterBrushDock.cs` (243), `WorldPainterLodPreviewPanel.cs` (231), `WorldPainterMigration.cs` (230), `WorldPainterBiomePaletteView.cs` (222), borderline 212s.
4. **Re-home leftover KEEP assets + kill old trees**: `Assets/GrassInteract/Meshes/*` (GrassBlade_LOD0-2, ScatterPropRock meshes — referenced-by-GUID KEEP runtime assets) → move under `Assets/WorldPainter/` (git mv + meta). `Assets/GrassInteract/README.md`+`MIGRATION.md` → move or delete. Then the OLD `Assets/GpuTerrain/` + `Assets/GrassInteract/` trees are empty shells (stray dirs + folder `.meta`s, incl `GpuTerrain/Editor/WorldPainter`, `*/Shaders`, `*/Demo`) — remove them (P10 also covers stray-meta cleanup). `TerrainValidationSceneBuilder` generator path retarget (E5/P8): currently writes under `Assets/GpuTerrain/Demo` — retarget to new tree.

### P9 (atomic namespace rewrite) — `GpuTerrain`/`GpuTerrain.Editor`/`GpuTerrain.Tests`/`GrassInteract`/`GrassInteract.Editor`/`GrassInteract.Tests` → `WorldPainter`/`WorldPainter.Editor`/`WorldPainter.Tests` across ALL surviving .cs (`namespace` decls + `using` stmts). Single batch, verify ONCE (long reload). Update asmdef `rootNamespace` (already set). SC2 = zero `GpuTerrain`/`GrassInteract` namespace/using remain. Note: shader names like `"GpuTerrain/BrushDecal"` (in TerrainBrushPreview `Shader.Find`) + `"Hidden/..."` are SHADER paths, not C# namespaces — decide whether to rename shader decls too (cosmetic).

### P10 — full-suite verify + SC checks (SC2 namespaces, SC5 ≤200 lines, SC6 player-build authoring-isolation, SC7 git renames, SC8 coach-marks "Create 1×1 tile" smoke) + push. NOT pushed yet.

### P6 done (commit `1053c16`)
Deleted the 4 superseded old terrain TOOLS: `GpuTerrainRendererEditor(.Sculpt)` + `TerrainSculptTool(.Stroke)`. Rescued shared KEEP utils `TerrainSculptConfig` + `TerrainBrushPreview` from `_pending-delete` → `Editor/Brush/`. 301 green.

### CRITICAL findings for P7/P8 (discovered via ref-check — do NOT re-derive)
- **`TerrainBrushPreview` is KEEP** (used by `WorldPainterSculptTool.cs:180` `.Set(...)`). NOT deletable.
- **`TerrainSculptConfig` is KEEP** (constants `BRUSH_RT_RES`/`THREAD_GROUP_SIZE`/`KERNEL_*`/`MAX_SPLAT_LAYERS` used across `WorldPainterSculptTool.*`, `TileRtCache`, `WorldPainterSculptTool.Density`).
- **`GpuTerrainRenderer` is KEEP runtime** (only the old *Editor* was deletable). Used by `TerrainValidationSceneBuilder.cs:235` (AddComponent) + `WorldPainterMigration`. P8 consolidation (§3.3) decides whether `WorldPainter.Render` folds it in.
- **P8 island** still in `_pending-delete/`: `TerrainBrushStroke.cs` (defines the shared `SculptMode` enum!), `TerrainSculptState.cs` (`ModeColor` + uses `TerrainSculptUndo`), `TerrainSculptUndo.cs`. These are a parallel-impl of the WorldPainter sculpt system, kept alive ONLY by `TerrainBrushMathTests`' 6 `ModeColor`/`SculptMode` cases. `WorldPainterState` has NO `ModeColor`/`SculptMode` equivalent (uses `LayerType`+`BrushSettings`). **P8 plan:** migrate `SculptMode`+`ModeColor` into a KEEP home (e.g. `WorldPainterState` or keep a slimmed `TerrainSculptState`), repoint the 6 ModeColor tests, THEN delete the island.
- **`WorldPainterUndoTests` already fully mirrors `TerrainSculptUndoTests`** (11 cases, +memory-cap) against the KEEP `WorldPainterUndo` → **drop `TerrainSculptUndoTests` in P8** (zero coverage loss; documented).
- **`DensityBrushMathTests` is a 15-test STRADDLE** (the P7 blocker): KEEP = `GrassFieldSpace`(4) + `WorldPainterDensityEncoder` round-trip(1, GPU-gated). P7-delete refs = `DensityPaintGPU.ComputeStampPositions`(5) + `DensityPaintGPU` GPUPaintSmoke(1, GPU-gated) + `DensityMapFactory.ReadbackToPixels`(1, GPU-gated) + `ScatterBrushPreview.ComputeDecalRotation`(3). **Before deleting `DensityPaintGPU`/`DensityMapFactory`/`ScatterBrushPreview` (P7): port `ComputeStampPositions` stamp-math into the KEEP WorldPainter stamping path (E3) + repoint the 5 tests; decide per-test on the GPU-gated ones (GPUPaintSmoke/ReadbackToPixels) and the 3 `ComputeDecalRotation` cases — migrate to KEEP equivalent or drop-with-justification. Split the KEEP halves of this test file out so they survive.** Run a full ref-check on every P7 scatter-authoring target vs KEEP runtime/editor first (some like `ScatterBrushPreview`/`DensityMapFactory` may be referenced by KEEP WorldPainter code, not just the test).

### Original status header (still accurate for P3-P5):

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
