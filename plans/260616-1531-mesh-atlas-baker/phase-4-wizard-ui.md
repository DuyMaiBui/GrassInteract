# Phase 4 — Wizard UI + UV guard + end-to-end bake

Effort: M · Blocks: nothing (final) · Blocked by: P1, P2, P3

## Goal

Implement `AtlasBakerWindow` (the `EditorWindow` wizard) that orchestrates the full pipeline (collect → pack → bake → remap → combine → emit), wire the menu item, integrate the out-of-0–1 UV guard, and run an end-to-end sample bake to prove visual parity and draw-call reduction.

## File ownership (exact paths)

- `Assets/MeshAtlas/Editor/UI/AtlasBakerWindow.cs`
- `Assets/MeshAtlas/Editor/UI/BakeOptions.cs` (serializable options struct: max atlas size, padding, channel toggles, output folder, mip toggle)
- `Assets/MeshAtlas/Editor/AtlasBakePipeline.cs` (orchestrator that chains P1–P3 components; window calls this — keeps UI thin, ≤200 lines per file)

## Implementation notes

- **Menu:** `[MenuItem("Tools/Mesh Atlas/Combine & Bake")]` opens the window.
- **UI fields:** current selection (renderers/meshes) display + count; max atlas size (default 4096); padding (default 4); 4 channel toggles (Albedo/Normal/Mask/Emission, all on); output folder picker (default a generic subfolder under `Assets/`); mipmap toggle; "Bake" button; a preview/log area.
- **UV guard (mandatory):** before packing, run P1's `UvRangeInspector` over each selected mesh's UV0. Any mesh with UV0 outside `[0,1]` → add to an offending list, show a clear warning listing each offending mesh by name, and **SKIP** those meshes from the bake (do not silently include — that would corrupt the atlas). If the skip leaves <2 meshes, warn the bake is pointless and abort.
- **Orchestration (`AtlasBakePipeline`):**
  1. Collect meshes + per-renderer material set + 4 source maps each.
  2. Filter via UV guard.
  3. `AtlasPacker` (from albedo source sizes) → layout `Dictionary<Material,Rect>`. On packer overflow → surface the failure in the UI, abort.
  4. `MapBaker` → 4 atlases (one layout).
  5. `UVRemapper` per mesh + `MeshCombiner` → combined mesh.
  6. `AtlasAssetWriter` → mesh + PNGs + material + prefab.
  7. Report output paths in the log area.
- Errors surface to the user (errors-over-silent-fallbacks): packer overflow, missing source maps, all-skipped selection.

## Success criteria

- Menu `Tools/Mesh Atlas/Combine & Bake` opens the window.
- Selecting N props + Bake produces mesh + 4 PNGs + material + prefab in the output folder.
- Out-of-0–1 UV meshes are listed + skipped, never silently baked.
- Dropping the prefab into a scene renders visually identical to the originals (tint/metal/smooth preserved) and collapses to **1 draw call** (verify via Frame Debugger / `rendering_stats`).

## Verification step

End-to-end in-editor bake (integration — not batch-unit-testable):
1. Open the window, select a known 3–4 prop sample (mix of tints + a normal map + one deliberately out-of-0–1 UV mesh).
2. Confirm the out-of-range mesh is listed + skipped.
3. Bake; confirm all 6 outputs written, `read_console` clean.
4. Drop prefab; compare against originals side-by-side (visual parity); confirm draw-call count drops to 1 via Frame Debugger / `rendering_stats`.
5. Re-run P1 EditMode suite (`run_tests`) to confirm no regression in the pure-C# core.

> asmdef/compile: if the window won't appear after edits, touch a `.cs` to force recompile (asmdef no-op gotcha), then `read_console`. MCP timeout during the bake ≠ editor crash — wait it out.

## Rollback

Delete `Assets/MeshAtlas/Editor/UI/` + `AtlasBakePipeline.cs`. P1–P3 remain usable as a programmatic API; only the wizard entry point is removed.
