# GPU Terrain — Multi-Tile Renderer + Inspector Sculpt (Design v2)

Date: 2026-06-11 · Branch: plan/gpu-terrain-cdlod · Status: APPROVED → plan

## Problem statement

Two linked asks:

1. **Move the GPU terrain sculpt UI into the `GpuTerrainRenderer` Inspector** — not the
   `TerrainTileAsset` ScriptableObject inspector.
2. **Brush strokes don't visibly change the mesh.** Port the live-update pattern from
   `ScatterStudioWindow` / `DensityPaintTool` (paint live target → immediate rebuild).

Plus three refinements raised mid-brainstorm:

3. **One `GpuTerrainRenderer` renders multiple tiles** (currently a single `tileAsset`).
4. **One shared LOD setup** across all tiles.
5. **Hide all raw terrain data + infra fields** in inspectors.

## Root cause of "mesh doesn't change"

The sculpt path (`TerrainSculptTool` + `TerrainTileAssetEditor`) builds its **own**
`TerrainTileGpuResources` + `heightRT`, separate from the renderer's. It edits a texture
nothing renders. Two compounding faults:

- **Wrong target** — editor's GPU copy ≠ the `_HeightTex` the vertex shader samples (VTF).
- **Stale rebind** — `TerrainTileGpuResources.Upload` allocates a *new* `Texture2D`, so the
  patch material's `_HeightTex` binding points at the old object even after re-upload.

`ScatterStudio` avoids this: it paints the live density RT and calls
`ScatterRebuildScheduler.RebuildImmediate`. We port that model.

## Key technical facts (verified)

- Renderer height = `Texture2D` (R16, RHalf fallback), bound to `_HeightTex`. A compute shader
  **cannot** write to a `Texture2D`. The brush uses a separate `RenderTexture` (RFloat, 512²,
  `enableRandomWrite`) as compute target.
- **Decode parity:** working RT stores normalized `[0,1]`; R16 sampling also yields `[0,1]`;
  `SampleHeightVTF` = `_MinHeight + raw*(_MaxHeight-_MinHeight)` either way. So temporarily
  binding the working RT as `_HeightTex` during a stroke is **visually correct** — instant
  feedback, no per-frame readback.
- `GpuTerrainRenderer` exposes neither `engine` nor `gpuResources`; needs an internal seam.
- No renderer registry today. With a renderer-owned tile list, none is needed — the inspected
  renderer is the sculpt target; tile resolved per-stroke by cursor.
- Tile grid: `TILE_SIZE_M=256`, default `heightRes=257`, origin = `tileCoord*256`.
  `TerrainWorldGrid.WorldToTileCoord` selects tile; `TerrainPaintTargetResolver` maps world→UV
  (and `Resolve` returns all tiles a brush circle overlaps).
- Writeback already resamples 512→257 (nearest, proportional) in
  `TerrainSculptRtWriteback.WriteHeightToAsset`.

## Approaches considered

| Live-update approach | Verdict |
|---|---|
| Bind working RT as `_HeightTex` during stroke, commit to Texture2D on writeback | **Chosen** — instant, cheap, no per-frame readback; decode parity confirmed |
| Keep separate RT, force renderer rebuild each throttle tick | Rejected — rebuild cost + indirection |

| Multi-tile model | Verdict |
|---|---|
| Renderer owns explicit `List<TerrainTileAsset>`; streaming separate | **Chosen (default)** |
| Renderer supersedes `TerrainStreamingManager` | Deferred — larger refactor |
| Auto-discover tiles by folder/coord | Deferred |

## Recommended solution

### Renderer refactor (single → multi-tile)
```
GpuTerrainRenderer
  tiles : List<TerrainTileAsset>   // VISIBLE — terrains to render
  lodRangesM : float[]             // VISIBLE — the ONE shared LOD setup
  cullCompute  : ComputeShader     // HIDDEN [HideInInspector], auto-resolved if null
  patchMaterial: Material          // HIDDEN [HideInInspector], auto-resolved if null
  └ runtime: one GpuTerrainEngine per tile
       • share cullCompute + lodRangesM
       • each clones patchMaterial → binds its own _HeightTex
```
- Build/submit/dispose loops iterate `tiles`.
- **Migration:** old single `tileAsset` (and the 2-tile validation scene) fold into `tiles`.
- **Hidden-field safety (errors-over-silent-fallback):** custom editor auto-resolves
  `cullCompute`/`patchMaterial` from package defaults; if unresolved, show a clear error — never
  silently render nothing.

### Inspector — foldout sections (approved layout)
```
▼ Tiles
   Size [N];  Element i = TerrainTileAsset field + read-only "coord res min..max" label
   [ + Add Tile ] [ - Remove ]
▼ LOD Setup (shared)
   Ranges (m) [ ... ]
▼ Sculpt
   Mode (● Sculpt)( Paint )
   Sculpt → [Raise][Lower][Smooth][Flatten]  (+ Target Height when Flatten)
   Paint  → Paint Layer [ ▾ ]
   Size / Strength sliders
   [ Undo ] [ Save ]
   [ ▶ Activate Sculpt Tool ]   (toggle, reflects ToolManager state)
```
- **`GpuTerrainRendererEditor` (NEW)** `[CustomEditor(typeof(GpuTerrainRenderer))]` — draws the
  above; never draws `cullCompute`/`patchMaterial`.
- **`TerrainTileAssetEditor` (REPLACE)** — notice only:
  *"Managed by GpuTerrainRenderer. Select the renderer to sculpt."* No fields/summary/brush UI.
  (`heightData`/`splatData` already `[HideInInspector]`.)

### Live sculpt brush (the fix)
```
Stroke → WorldToTileCoord → find tile in renderer.tiles → that tile's engine
  → compute → working RT (RFloat)
  → engine.BeginSculptPreview(rt):  _HeightTex = rt        (VISIBLE next frame)
  → throttled 0.15s: readback → resample 512→257 → tile.heightData → CommitHeight()
  → mouseUp: final commit → engine.EndSculptPreview()       (rebind Texture2D)
```
- **Seam (NEW, internal) on `GpuTerrainRenderer`/`GpuTerrainEngine`:**
  `BeginSculptPreview(tileCoord, rt)`, `EndSculptPreview(tileCoord)`,
  `CommitHeight(tileCoord)`, per-tile `HeightTexture` accessor, engine-by-coord lookup.
- **`TerrainTileGpuResources.Upload` (MODIFY):** reuse the existing `Texture2D` when
  res/format match (`LoadRawTextureData`+`Apply` on the same object) → binding stays valid.
- **`TerrainSculptState`:** `ActiveTile` → `ActiveRenderer`; tile resolved per-stroke.
- **`TerrainSculptTool` (RETARGET):** operate on the resolved tile's engine resources;
  seed working RT from current height; live preview; throttled commit; final commit on mouse-up.

### Reuse (DRY, unchanged in substance)
`TerrainBrushStroke`, `TerrainBrush.compute`, `TerrainSculptRtWriteback` (incl. 512→257
resample), `TerrainSculptUndo`, `TerrainBrushPreview`. Only the *target* changes.

## Phasing

- **P1** — Renderer multi-tile refactor + shared LOD + hidden infra fields + notice-only asset
  inspector + `GpuTerrainRendererEditor` sculpt UI + retarget tool to tile-under-cursor with
  live preview + `Upload` Texture2D reuse + scene migration. **Solves both core asks.**
- **P2** — Cross-tile strokes via `TerrainPaintTargetResolver.Resolve` → dispatch per
  intersected tile (completes multi-tile auto-target across borders).

## Risks

- Renderer single→multi refactor touches build/submit/dispose + scene wiring — largest risk;
  covered in P1 with migration.
- Hidden infra fields must auto-resolve or scenes break — explicit error path required.
- Per-tile engine cost scales with tile count (each culls independently).
- `TerrainStreamingManager` streaming path is **out of scope** (separate, non-sculptable).
- Undo stays per-tile; cross-tile strokes (P2) push per affected tile.

## Success criteria

1. Selecting a `GpuTerrainRenderer` shows the foldout sculpt inspector; `cullCompute`,
   `patchMaterial`, and raw tile data are not visible anywhere.
2. One renderer renders ≥2 tiles with a single `lodRangesM`.
3. Dragging the sculpt brush changes the **rendered** terrain in real time (no manual rebuild).
4. After mouse-up the change persists to `tile.heightData` and survives domain reload.
5. `TerrainTileAsset` inspector shows only the managed-by notice.

## Next step

`/t1k:plan` — phased implementation plan (P1 then P2) from this design.
