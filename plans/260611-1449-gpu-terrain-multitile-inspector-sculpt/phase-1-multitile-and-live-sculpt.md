# Phase 1 — Multi-tile renderer + inspector sculpt + live-update tool

**Effort: L** · Solves both core asks + refinements 3/4/5. **Blocked by:** nothing. **Blocks:** P2.

## Goal

`GpuTerrainRenderer` renders a `List<TerrainTileAsset>` (one `GpuTerrainEngine` per tile, sharing one `lodRangesM` + `cullCompute` + `patchMaterial`). `cullCompute`/`patchMaterial`/raw tile data are hidden and auto-resolved. A new `GpuTerrainRendererEditor` draws the approved foldout sculpt UI. `TerrainTileAssetEditor` becomes a notice. The sculpt tool retargets to the tile under the cursor's LIVE engine resources with instant VTF preview, and `Upload` reuses the `Texture2D` so the binding stays valid after commit.

## Internal ordering (file-ownership; do NOT parallelize across these groups — later groups call earlier APIs)

```
G1 data model + seam  →  G2 Upload reuse  →  G3 editor (renderer editor, tool, state, notice)  →  G4 scene migration
   Runtime/GpuTerrainRenderer.cs            Runtime/                Editor/GpuTerrainRendererEditor.cs (NEW)   Editor/TerrainValidationSceneBuilder.cs
   Runtime/GpuTerrainEngine.cs              TerrainTileGpuResources Editor/TerrainSculptTool.cs                Demo/TerrainValidation.unity (data)
                                            .cs                     Editor/TerrainSculptState.cs
                                                                    Editor/TerrainTileAssetEditor.cs
```
Per the batch-compile rule: implement G1→G4 blind, then ONE compile + test gate at the end of the phase. Within a group, edits are blind.

---

## Tasks → files

### T1 (G1) — Renderer: single `tileAsset` → `List<TerrainTileAsset> tiles`; hidden infra; auto-resolve-or-error
**Owns:** `Runtime/GpuTerrainRenderer.cs`

- Replace `[SerializeField] private TerrainTileAsset? tileAsset` with `[SerializeField] private List<TerrainTileAsset> tiles = new()`.
- Mark infra fields hidden + serialized: `[HideInInspector] [SerializeField] private ComputeShader? cullCompute` and `[HideInInspector] [SerializeField] private Material? patchMaterial`. Keep `lodRangesM` visible (shared).
- Runtime state: single `engine`/`gpuResources` → **parallel lists** keyed by tile index, e.g. `private readonly List<GpuTerrainEngine> engines = new()` and `private readonly List<TerrainTileGpuResources> gpuResources = new()`, plus a `Dictionary<Vector2Int,int>` coord→index lookup for the seam.
- `TryBuild`: iterate `tiles`; per tile build one `TerrainTileGpuResources` + one `GpuTerrainEngine(cullCompute, patchMaterial)` (each engine already clones `patchMaterial` — `GpuTerrainEngine.Build` line 154 — so they share the source material and bind their own `_HeightTex`). Skip + `Debug.LogWarning` per-tile on invalid height; keep building the rest. Populate the coord→index map. SelfTest stays per-engine.
- **Auto-resolve-or-error (errors-over-silent-fallback, per `development-principles`):** when `cullCompute`/`patchMaterial` are null, resolve from package defaults via `AssetDatabase.LoadAssetAtPath` under `#if UNITY_EDITOR` (same convention `TerrainSculptTool.OnActivated` uses for `Assets/GpuTerrain/Shaders/TerrainBrush.compute`). If still null after resolve → `Debug.LogError` with a CLEAR message naming the missing asset and **do not build** (never silently render nothing). Also invoked from the custom editor (T6) so a freshly-added component self-heals.
- `OnEnable`/`OnDisable`/`LateUpdate`/`OnBeginCameraRenderingEdit`/`SubmitForCamera`/`DisposeEngine`/`Rebuild`: iterate the engine list (Submit each; dispose all + clear lists + map).
- `SubmitForCamera`: `foreach (var e in engines) e.Submit(cam, camPos)`.
- File ≤200 lines: if list plumbing + auto-resolve pushes over, extract the per-tile build body into a private helper in the SAME file (do not add a new file unless unavoidable).

### T2 (G1) — Engine + renderer: internal sculpt seam
**Owns:** `Runtime/GpuTerrainEngine.cs`, `Runtime/GpuTerrainRenderer.cs`

On `GpuTerrainEngine` (`internal`, like the existing `TileOriginWS`):
- `internal void BeginSculptPreview(RenderTexture rt)` — `this.patchMaterial.SetTexture(ID_HeightTex, rt)` (binds the working RT for instant VTF; decode parity confirmed in Design v2).
- `internal void EndSculptPreview()` — rebind the committed `Texture2D`: `this.patchMaterial.SetTexture(ID_HeightTex, this.gpuResources.HeightTexture)`.
- `internal Texture2D? HeightTexture => this.gpuResources?.HeightTexture` (per-tile accessor).
- `internal TerrainTileGpuResources? GpuResources => this.gpuResources;` (tool seeds working RT + commits through this).

On `GpuTerrainRenderer` (internal seam keyed by tile coord, used by `TerrainSculptTool`):
- `internal GpuTerrainEngine? EngineForCoord(Vector2Int coord)` — via the coord→index map (null if not in `tiles`).
- `internal TerrainTileGpuResources? ResourcesForCoord(Vector2Int coord)`.
- `internal void BeginSculptPreview(Vector2Int coord, RenderTexture rt)` / `EndSculptPreview(Vector2Int coord)` / `CommitHeight(Vector2Int coord)` — delegate to the matching engine. `CommitHeight` re-binds the (reused) `Texture2D` after the writeback's `gpu.Upload` (a no-op rebind is harmless; the real fix is T3's same-object reuse).
- `internal IReadOnlyList<TerrainTileAsset> Tiles => this.tiles;` (editor reads for the tile foldout).

### T3 (G2) — `TerrainTileGpuResources.Upload`: reuse `Texture2D` when res/format match
**Owns:** `Runtime/TerrainTileGpuResources.cs`

- Compute `chosenHeightFmt` (the `SupportsTextureFormat` probe, currently line 73) BEFORE the reuse check so the format comparison is correct.
- If `this.heightTex != null && this.heightTex.width == tile.heightRes && this.heightTex.height == tile.heightRes && this.heightTex.format == chosenHeightFmt`: **skip `this.Dispose()`** — `LoadRawTextureData(...)` + `Apply(...)` on the SAME `heightTex` object (this keeps the material's `_HeightTex` binding valid after commit — the stale-rebind fix). Same reuse branch for `splatTex` when `splatRes`/`SPLAT_FORMAT` match.
- Otherwise (first upload, or res/format changed): keep the existing allocate-new path (`Dispose()` first, then `new Texture2D`).
- Do NOT change `ConvertR16ToRHalf`, the decode formulas, `HeightFormat`/`IsUploaded` semantics, or the debug log shape — only the allocate-vs-reuse branch.

### T4 (G3) — `TerrainSculptState`: `ActiveTile` → `ActiveRenderer`
**Owns:** `Editor/TerrainSculptState.cs`

- Replace `public static TerrainTileAsset? ActiveTile` with `public static GpuTerrainRenderer? ActiveRenderer`. The tile is resolved per-stroke from the cursor via `TerrainWorldGrid.WorldToTileCoord` — state no longer holds a tile.
- Everything else (mode, sub-mode, sliders, `BrushColor`, `ModeColor`) unchanged. Add an optional `LastStrokedCoord` if the editor Undo/Save (T6) needs it.

### T5 (G3) — `TerrainSculptTool`: retarget to tile-under-cursor + live preview
**Owns:** `Editor/TerrainSculptTool.cs`

- Drop the tool-owned `activeTile`/`activeGpu` model that built its OWN `TerrainTileGpuResources` (the wrong-target root cause). Bind to `TerrainSculptState.ActiveRenderer`.
- Per **stroke** (mouse-down):
  1. cursor world XZ → `coord = TerrainWorldGrid.WorldToTileCoord(x, z)`; `engine = renderer.EngineForCoord(coord)`; null → ignore the stroke (empty space).
  2. `gpu = renderer.ResourcesForCoord(coord)` (the LIVE engine resources — the texture the renderer actually samples).
  3. **Seed the working RT from current height** so the stroke starts from real terrain, not zero: copy/blit `gpu.HeightTexture` (normalized R16/RHalf → `[0,1]`) into the RFloat working RT before the first dispatch (decode parity → straight normalized copy is correct). NEW helper.
  4. `renderer.BeginSculptPreview(coord, workingRT)` — `_HeightTex` now points at the working RT (visible next frame).
- Per **drag**: `stroke.Dispatch(...)` into the working RT (unchanged compute path); `stroke.ThrottledWriteback(...)` (0.15 s) → `TerrainSculptRtWriteback` readback → resample 512→257 → `tile.heightData` → `gpu.Upload` (now same-object reuse) → `renderer.CommitHeight(coord)`.
- Per **mouse-up**: final `stroke.EndStroke(...)`, then `renderer.EndSculptPreview(coord)` (rebind committed `Texture2D`).
- Keep `TryGetBrushWorldPoint` (physics-first, plane fallback); plane fallback now uses the resolved tile's mid-height. Keep `DrawHud`, `OnEditorUpdate` (`writeback.Tick`).
- Working RT lifetime: one RFloat + one ARGBFloat RT owned by the tool, created in `OnActivated`, released in `OnWillBeDeactivated`, sized `TerrainSculptConfig.BRUSH_RT_RES`; re-seeded per stroke from whichever tile is under the cursor.

### T6 (G3) — NEW `GpuTerrainRendererEditor` (the relocated sculpt UI)
**Owns:** `Editor/GpuTerrainRendererEditor.cs` (NEW), namespace `GpuTerrain.Editor`.

- `[CustomEditor(typeof(GpuTerrainRenderer))] public sealed class GpuTerrainRendererEditor : UnityEditor.Editor`.
- `OnEnable`: `TerrainSculptState.ActiveRenderer = (GpuTerrainRenderer)target`; trigger the renderer's auto-resolve-or-error (T1) so missing infra surfaces immediately; own a `TerrainSculptRtWriteback` + `TerrainSculptUndo`; subscribe `EditorApplication.update += writeback.Tick` (mirrors the old asset editor).
- `OnInspectorGUI` — approved foldout layout (`EditorGUILayout.Foldout` open-state in `SessionState`/`EditorPrefs`; serialized fields via `SerializedProperty`):
  - **▼ Tiles** — `SerializedProperty` list of `tiles`: per element an object field + a read-only `coord res min..max` summary label (read from each `TerrainTileAsset`); `+ Add Tile` / `- Remove` buttons (mutate the serialized list, `ApplyModifiedProperties()` → `Rebuild`).
  - **▼ LOD Setup (shared)** — the one `lodRangesM` array (serialized).
  - **▼ Sculpt** — Mode toolbar `Sculpt | Paint`; Sculpt → `Raise/Lower/Smooth/Flatten` (+ `Target Height` slider only when Flatten); Paint → layer dropdown; `Size`/`Strength` sliders; `Undo`/`Save` row; `Activate Sculpt Tool` toggle reflecting `ToolManager.activeToolType == typeof(TerrainSculptTool)`. **Move** (don't duplicate) the panel logic from the old `TerrainTileAssetEditor.DrawBrushUI/DrawSculptPanel/DrawPaintPanel/DrawActivateButton` — those bodies are deleted in T7.
  - **NEVER** draw `cullCompute`/`patchMaterial`; do NOT call `DrawDefaultInspector`.
- `OnDisable`: `writeback.Dispose()`; unsubscribe update.
- Undo/Save operate on the **last-stroked tile** (`TerrainSculptState.LastStrokedCoord`) to match the existing per-tile undo semantics. Cross-tile undo is P2.
- File ≤200 lines: split the three foldout sections into partial-class `GpuTerrainRendererEditor.Sculpt.cs` ONLY if measured over after writing.

### T7 (G3) — `TerrainTileAssetEditor`: replace body with notice-only
**Owns:** `Editor/TerrainTileAssetEditor.cs`

- Strip ALL brush UI, tile summary, RT management, undo/save, `OnEnable`/`OnDisable`/`EnsureRTs`/`ReleaseRTs`. Keep `[CustomEditor(typeof(TerrainTileAsset))]`.
- `OnInspectorGUI` draws only: `EditorGUILayout.HelpBox("Managed by GpuTerrainRenderer. Select the renderer to sculpt.", MessageType.Info)`.
- Remove now-unused fields/usings. Pre-delete reference check: confirm nothing else references `TerrainTileAssetEditor` members.

### T8 (G4) — Scene migration: 2-tile validation scene + builder
**Owns:** `Editor/TerrainValidationSceneBuilder.cs`, `Demo/TerrainValidation.unity` (data — re-author via the builder menu item)

**Verified current state** (`TerrainValidationSceneBuilder` lines 218–252): the builder creates **TWO** `GpuTerrainRenderer` components — `CreateTileRenderer("TerrainRenderer_A", tileA, …)` and `CreateTileRenderer("TerrainRenderer_B", tileB, …)` — each wired via `so.FindProperty("tileAsset").objectReferenceValue = tile`. That single→multi consolidation IS the largest-risk migration surface (the class doc at line 21 even calls out "TWO GpuTerrainRenderer components (one per tile)").

- Collapse the two `CreateTileRenderer` calls into ONE renderer (e.g. `CreateTerrainRenderer("TerrainRenderer", new[]{ tileA, tileB }, cullCompute, patchMat)`).
- The serialized write changes from a single object ref to an **array population**: `tileAsset` → `tiles` (`SerializedProperty`: set `arraySize = 2`, then `GetArrayElementAtIndex(i).objectReferenceValue`). Keep the `cullCompute`/`patchMaterial` writes (still serialized though `[HideInInspector]` now — `FindProperty` still resolves them); these are optional once auto-resolve (T1) lands, but keeping them keeps the scene deterministic. Apply via `ApplyModifiedPropertiesWithoutUndo`.
- Re-author `Demo/TerrainValidation.unity` by **running the builder menu item in-editor** (this IS the T8 verification step); never hand-edit `.unity` YAML. The old two-renderer GameObjects are replaced by the one-renderer build.
- Update the class-doc comment (lines 21–24) to reflect the single-renderer/multi-tile model.
- Pre-delete reference check: `grep` for `"tileAsset"` and `CreateTileRenderer` repo-wide before T1 deletes the field — the builder is the known caller; confirm there is no other.

---

## Verification (ONE compile + test gate at end of P1, per batch-compile rule)

1. **Compile gate:** `mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` then `read_console` (filter Error) — collect the **full** error set, fix in one batch, re-gate. Zero errors required. (`refresh_unity` no-ops on asmdef-only edits; T6 adds a `.cs` to the existing Editor asmdef so a recompile is triggered.)
2. **Test gate:** `mcp__UnityMCP__run_tests` (EditMode) — zero failures. The existing `GpuTerrainEngine.TileOriginWS` B1 regression tests must still pass (seam additions are internal, non-breaking).
3. **In-editor manual gates (the user-visible asks):**
   - Open `Demo/TerrainValidation.unity`. Select the `GpuTerrainRenderer` → the **foldout sculpt inspector** shows; `cullCompute`/`patchMaterial`/raw arrays are **not** visible (**SC1**). Both tiles render with one `lodRangesM` (**SC2**).
   - Select a `TerrainTileAsset` → only the managed-by notice shows (**SC5**).
   - `Activate Sculpt Tool`, drag on a tile → the **rendered** mesh deforms in real time, no manual rebuild (**SC3**).
   - Mouse-up → reselect / trigger a domain reload (recompile) → the change persists in `tile.heightData` (**SC4**).
   - Drag on each tile individually (each resolves under the cursor and updates) — cross-*border* single-stroke is P2.

---

## Risk assessment (P1)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|---|---|---|---|
| **Renderer single→multi refactor breaks build/submit/dispose + scene wiring** (largest risk; verified: builder makes TWO renderers today) | 4 | 5 | **20 — HIGH** | Migrate in G1 with parallel engine/resource lists + coord map; per-tile skip-on-invalid (build the rest); T8 re-authors the scene via the builder menu item (no hand YAML); compile+test gate before any manual check. **Mitigation mandatory before T8 ships.** |
| **Hidden infra fields (`cullCompute`/`patchMaterial`) unresolved → scenes silently render nothing** | 3 | 5 | **15 — HIGH** | Auto-resolve from package defaults in T1 + T6; explicit `Debug.LogError` naming the missing asset + do-not-build (errors-over-silent-fallback). **Mitigation mandatory before phase ends.** |
| Working-RT seed-from-current-height wrong (stroke starts from zero, terrain jumps) | 3 | 4 | 12 | T5 seeds the RT from `gpu.HeightTexture` (normalized copy; decode parity verified) before the first dispatch; SC3 manual check catches a jump immediately. |
| `Upload` reuse branch leaves stale data on the res/format-mismatch path | 2 | 4 | 8 | T3 computes `chosenHeightFmt` before the reuse check; reuse only when width/height/format ALL match, else fall back to allocate-new (existing path). |
| Stale binding NOT actually fixed (`CommitHeight` rebinds wrong object) | 2 | 4 | 8 | T3 same-object reuse is the real fix; `CommitHeight` rebind is belt-and-suspenders; SC3/SC4 manual gates confirm live update + persistence. |
| Per-tile engine cost scales with tile count (each culls independently) | 2 | 2 | 4 | Accepted per Design v2; validation scene is 2 tiles; optimization out of scope here. |
| Editor file >200 lines (renderer editor) violates convention | 3 | 1 | 3 | Split into partial-class `GpuTerrainRendererEditor.Sculpt.cs` only if measured over after writing. |

## Rollback (P1)

Group boundaries are coherent commit points (G1 data+seam, G2 upload, G3 editor, G4 scene). Revert via `git revert` of the phase commit(s). No migration is destructive to `TerrainTileAsset` data — only the renderer's serialized field shape changes; the SO assets are untouched, and the old per-tile values are re-homed into `tiles` by re-running the builder. If the scene migration is bad, re-run the builder menu item to regenerate `Demo/TerrainValidation.unity`.

## Timeline (P1)

| Task group | Effort | Notes |
|---|---|---|
| G1 — data model + seam (T1, T2) | M | Highest-risk; coord map + parallel lists + auto-resolve |
| G2 — Upload reuse (T3) | S | Single-branch change; the stale-rebind fix |
| G3 — editor (T4–T7) | M | New renderer editor + tool retarget + notice; reuses old panel logic |
| G4 — scene migration (T8) | S | Re-author via builder menu item, not YAML |
| **P1 total** | **L** | Critical path: G1 → G2 → G3 → G4 (strictly sequential; ONE gate at end) |
</content>
