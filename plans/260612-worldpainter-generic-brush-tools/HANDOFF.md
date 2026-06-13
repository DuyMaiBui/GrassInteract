# WorldPainter — Session Handoff (2026-06-12)

Branch: `feat/worldpainter-ssot-consolidation`. All work below is committed (not pushed).

## Done this session (committed)

**`3366021`** — feat: generic `IBrushTool` interface + contextual tool palette
- Replaced hardcoded `BindAndDispatch` if/else with `IBrushTool` + `BrushToolRegistry`
  (`Assets/WorldPainter/Editor/Brush/Tools/`).
- Contextual TOOLS palette in brush dock per layer kind: Height(Raise/Lower/Smooth/Flatten),
  Splat(Paint/Erase), Density(Paint/Erase/Smooth), Instance(Place/Erase/Single).
- Wired previously-dead kernels + `_SplatMode` erase in `TerrainBrush.compute`.
- (Bundled some unrelated SSOT-consolidation work per user "commit everything".)

**`6bc6853`** — fix: type-based scatter classification + live edit-mode scatter preview
- `WorldPainterState.ScatterLayerKind(layer)` → `is InstanceScatterLayer ? Props : Grass`
  (was a fragile `name.Contains("prop")` heuristic). Used in `ActiveLayerType` +
  `WorldPainterLayerStackView`. **Fixes:** instance layer now shows its prop detail card
  and routes to instance brush tools regardless of name.
- Edit-mode scatter preview: `WorldPainter.Render.cs` `OnBeginCameraRenderingEdit` now builds
  (`RebuildScatter`) + `SubmitScatter` per camera; `WorldPainterSculptTool.HandleMouseUp`
  calls `painter.RebuildScatterPreview()` + `SceneView.RepaintAll()` after a Grass/Props stroke;
  `TryBuild` + OnDisable manage the `editScatterBuilt` flag.
  **Key fact:** `RebuildScatter()` was never called in production — grass never rendered in
  edit OR play mode. This wiring renders it for the first time (edit mode).
- `GrassRenderer.cs` sets `material.enableInstancing = true` (CPU tier `RenderMeshInstanced` requires it).

All 378 `WorldPainter.Tests` EditMode tests pass. Compiles clean.

## PENDING VERIFICATION (do first next session)

- **Confirm grass actually renders** when painting a density layer in the Scene view
  (user had not yet visually confirmed). Watch for the `RenderMeshInstanced` instancing
  error recurring. If grass still doesn't show: check the tier — Auto probe may fall to CPU
  when `scatterCullCompute`/`scatterIndirectMat` (GrassCull.compute / IndirectGrass.mat) are
  unassigned; GPU tier uses `RenderMeshIndirect` (no instancing flag needed).

## Known follow-ups (not blocking)
- Per grass/prop stroke rebuilds ALL scatter layers (`RebuildScatter`) — acceptable per user,
  but a per-layer rebuild would be cheaper. `RebuildScatter` also logs per layer → console spam.
- Play-mode scatter is still never built (`LateUpdate` calls Step/SubmitScatter but no
  `RebuildScatter`). Edit-mode preview is wired; play mode is a separate gap if needed.
- Pre-existing: `ActiveLayerType` stack-index math assumes ALL layers present; filter chips
  hiding rows can desync the index→type mapping (not introduced here).

## Requested features for the FRESH session (both confirmed with user)

### Feature A — Multi-texture splat terrain (moderate; self-contained)
**Want:** painting splat layer A vs B shows each layer's OWN ground albedo, blended by the
painted splat weights. Today only ONE texture shows for all splat layers.
**Entry points:**
- Terrain material: `Assets/WorldPainter/Materials/TerrainPatch.mat` (resolved in
  `WorldPainter.Render.cs::ResolveInfra` ~line 123); find its shader.
- Splat map already exists per tile: `TerrainTileGpuResources` uploads `splat=512² RGBA32`
  (4 channels = 4 splat layers). Confirmed in console: "Uploaded tile ... splat=512² (RGBA32)".
- Per-layer albedo: `WorldPainter.SplatLayers[i].albedo` (see `WorldPainterSplatLayerCard` /
  stack `elem.FindPropertyRelative("albedo")`).
**Approach (to scout/plan):** bind the 4 splat-layer albedos to the terrain (patch) material;
modify the terrain shader to sample the splat RGBA map and blend the 4 albedos by channel
weight. Need to find where `patchMaterial` is bound per tile (`GpuTerrainEngine`) to inject
the albedo array + splat texture.

### Feature B — Multi-variant grass per density layer (larger; FROZEN-engine constraint)
**Want:** ONE density layer config holds MULTIPLE grass meshes/textures; painting that one
layer scatters a random mix of all variants.
**Entry points / constraint:**
- Config: `DensityScatterLayer` → `Render` (`ScatterRenderConfig`) → `Lods`/`LodMeshes`/`Material`
  (currently ONE grass appearance). Need a variant list (meshes + materials).
- Scatter build: `GrassScatter.Build` assigns blades from the density map.
- Render: `GrassGpuEngine` (RenderMeshIndirect), `GrassCpuEngine`→`GrassRenderer` (RenderMeshInstanced).
- ⚠️ `WorldPainter.Scatter.cs` declares `GrassGpuEngine, GrassCpuEngine, InstancedPropEngine,
  InstanceBatchPool, IGrassEngine, IScatterPlacement` **FROZEN** ("never touch this file" /
  engines untouched). `GrassRenderer.cs` is NOT in that list (editable — already edited for
  instancing). Plan a variant path that EXTENDS rather than edits frozen engines (e.g. multiple
  engine instances per layer — one per variant — or a new variant-aware renderer), and confirm
  the freeze scope with the user before touching engine internals.

## Environment notes
- `mcp__UnityMCP__execute_code` is BROKEN on this machine (mono "filename or extension too long"
  on CodeDom; Roslyn not installed). Use `Debug.Log` diagnostics + `read_console` for runtime
  introspection instead. Unity 6000.3.13f1.
- Verify loop: edit → `refresh_unity(force, scripts)` → `read_console(errors)` → `run_tests(EditMode, WorldPainter.Tests)`.
