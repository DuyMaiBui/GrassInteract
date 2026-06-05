# Phase 4: Delete trample path + demo rebuild + README

**Effort: M** | **Blocked by: Phase 3 (new path proven live)** | **Blocks: Phase 5**

## Goal

Now that the C# bend path is verified live, delete the entire old GPU-trample path, rebuild the demo
scene without the GrassTrampleMap component (via the editor builder, NOT hand-edited YAML), and update
the README to describe the C# bend architecture + document the wind-in-shader escape hatch. Leave zero
dangling references.

## File ownership

- DELETE: `Runtime/GrassTrampleMap.cs` (+ `.meta`), `Runtime/GrassChunk.cs` (+ `.meta`),
  `Runtime/ChunkGrid.cs` (+ `.meta`), `Shaders/GrassInteractDeform.hlsl` (+ `.meta`),
  `Shaders/TrampleUpdate.shader` (+ `.meta`).
- REWORK: `Editor/GrassInteractDemoBuilder.cs` (remove GrassTrampleMap creation/wiring + its
  configure method; rebuild the scene without it).
- REGENERATE (not hand-edit): `Demo/GrassInteractDemo.unity` (rebuilt by running the demo builder).
- REWORK: `README.md` (architecture + escape hatch).
- TOUCH (comment-only): `Runtime/GrassLayer.cs` (its doc-comment references ChunkGrid - update to
  GrassScatter; no logic change).

## Concrete steps

1. **Pre-delete reference re-sweep.** Re-run the Phase 0 grep. Confirm the ONLY remaining references to
   the doomed symbols are inside the five files about to be deleted (plus the demo scene YAML + the
   GrassLayer doc-comment). If any runtime/editor code still references them, STOP and fix that first
   (it means an earlier phase left a dangle).
2. **Update GrassLayer.cs doc-comment** from "ChunkGrid turns this into instances" to "GrassScatter
   turns this into instances". No logic change.
3. **Rework GrassInteractDemoBuilder.cs:** remove `ConfigureTrampleMap`, the `trampleGo`/`GrassTrampleMap`
   creation block, and any `using`/reference to GrassTrampleMap. The effector keeps its `GrassInteractor`
   + `GrassInteractDemoEffector` (those drive the simulator now). Update the closing `Debug.Log`
   message (drop "trample trail"; say "the effector leans the swaying grass aside and it recovers").
4. **Delete the five files + their `.meta` files** using `manage_asset(action=delete, ...)` via Unity
   MCP (so the asset DB + GUIDs are cleaned correctly), NOT raw filesystem rm. Order: delete the two
   shaders first, then the three .cs files. After each, `read_console`.
5. **Rebuild the demo scene:** run `Tools > GrassInteract > Build Demo Scene` (the reworked builder).
   This regenerates `GrassInteractDemo.unity` with NO GrassTrampleMap component - the only correct way
   to remove the component (no hand-YAML). Save.
6. **README.md update:** describe the new architecture (dumb instanced shader + C# GrassBendSimulator
   bakes wind+bend into per-instance matrices; flat instance list + global LOD + one field-wide bounds;
   placement unchanged via GrassLayer + Grass Painter). Add an "Escape hatch" subsection: if mobile
   frame budget is tight, move ONLY wind back into the dumb shader (a one-line `_Time` sway in `vert`);
   bend stays in C#. Note the 50k-blade soft ceiling.

## In-editor verification gate

1. Post-delete `read_console`: ZERO compile errors and ZERO "missing script"/"can not find type"
   warnings. The re-sweep grep returns ZERO hits for the doomed symbols anywhere in `Assets/`.
2. The rebuilt demo scene opens with NO GrassTrampleMap GameObject/component; grass renders + the
   effector leans+recovers it in BOTH views, edit + play (same as Phase 3, now with the old path gone).
3. README reads correctly and documents the escape hatch + soft ceiling.

## Rollback

Back up the five to-be-deleted files + `GrassInteractDemoBuilder.cs` + the current
`GrassInteractDemo.unity` into `plans/grass-cpu-bend/_backup/phase-4/` BEFORE deleting. If a dangling
reference surfaces, restore the deleted files from the backup (and their .meta) to recompile, fix the
dangle, then re-delete. Because this is not a git repo, the `_backup/` copies ARE the only safety net -
make them before step 4.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| A dangling reference to a deleted type breaks compile | 3 | 3 | 9 | Step 1 re-sweep BEFORE deleting; delete via manage_asset (GUID-clean); read_console after each delete; _backup restore if a dangle appears. |
| Demo scene rebuild fails or loses wiring (asset-GUID quirk) | 2 | 3 | 6 | Reuse the existing builder's synchronous-import + second-pass re-assign pattern (already handles the GUID-commit quirk). Verify the field references the layer after rebuild. |
| Deleting .shader leaves an orphaned material referencing it | 2 | 2 | 4 | Only TrampleUpdate.shader is deleted (its material is created at runtime by GrassTrampleMap, also deleted). The grass material references the kept GrassInteractInstanced.shader. Verify no asset references TrampleUpdate. |
| Hand-editing the scene YAML to remove the component (anti-pattern) | 1 | 3 | 3 | Forbidden - the component is removed by REBUILDING via the editor builder (step 5), never by YAML surgery. |
