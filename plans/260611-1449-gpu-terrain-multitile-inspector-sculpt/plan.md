# Plan: GPU Terrain — Multi-Tile Renderer + Inspector Sculpt (live-update)

**Branch:** `plan/gpu-terrain-cdlod` · **Engine:** Unity 6 · custom GPU CDLOD terrain (NOT Unity Terrain, NOT DOTS)
**Source of truth:** `plans/reports/gpu-terrain-inspector-multitile-sculpt-design.md` (APPROVED Design v2). All design decisions are settled — this plan encodes them, it does not re-decide them.
**Conventions:** `.claude/rules/code-conventions-unity.md` — camelCase private fields (no `_`), `this.` mandatory, PascalCase public, files ≤200 lines, `#nullable enable`.
**Verify-once discipline:** `.claude/rules/ai-velocity-batch-compile-unity.md` — compile gate = `read_console` (all errors) + `run_tests` (all failures) in ONE pass per phase; never per-edit.

All code under `Assets/GpuTerrain/`. Two phases: **P1** solves both core asks (inspector relocation + the mesh-not-updating bug); **P2** completes cross-tile strokes.

---

## Two user asks → root cause → fix (from Design v2)

1. **Move sculpt UI into the `GpuTerrainRenderer` Inspector** (not the `TerrainTileAsset` SO inspector).
2. **Brush strokes don't visibly change the mesh.** Root cause: the sculpt path (`TerrainSculptTool` + `TerrainTileAssetEditor`) builds its **own** `TerrainTileGpuResources` + working RT, separate from the renderer's — it edits a texture nothing renders (wrong target), and `TerrainTileGpuResources.Upload` allocates a **new** `Texture2D` each call so the material's `_HeightTex` binding goes stale (stale rebind). Fix = operate on the LIVE engine resources, bind the working RT during the stroke for instant VTF feedback, and reuse the `Texture2D` on commit so the binding stays valid.

Decode parity (verified in Design v2): working RT normalized `[0,1]` and R16 sampling both yield `[0,1]` through `SampleHeightVTF` → temporarily binding the working RT as `_HeightTex` is visually correct.

---

## Phase summary

| Phase | Name | Solves | Effort |
|---|---|---|---|
| **P1** | Multi-tile renderer + hidden infra + inspector sculpt UI + live-update tool + Upload reuse + scene migration | Both core asks (1 + 2) + refinements 3/4/5 | **L** |
| **P2** | Cross-tile strokes via `TerrainPaintTargetResolver.Resolve` → per-tile dispatch + per-tile undo | Multi-tile auto-target across borders | **M** |

P2 is **blocked by** P1 (needs the renderer tile-list + the per-tile engine seam). P1 has internal ordering (data model → seam → editor) detailed in its file-ownership graph below.

---

## Out of scope (explicit)

- `TerrainStreamingManager` streaming path — separate system, **non-sculptable**, untouched by this plan. `TerrainPaintTargetResolver.Resolve` is called with `residencySet: null` (sculpt operates on the renderer's explicit `tiles` list, not the streaming resident set).
- No new tile auto-discovery, no renderer registry (the inspected renderer IS the sculpt target — Design v2 §"No renderer registry today").
- Reused unchanged in substance: `TerrainBrushStroke`, `Shaders/TerrainBrush.compute`, `TerrainSculptRtWriteback` (incl. 512→257 resample), `TerrainSculptUndo`, `TerrainBrushPreview`. Only the *target* changes.

---

## Success criteria (mirrors Design v2 §"Success criteria")

- [ ] **SC1** — Selecting a `GpuTerrainRenderer` shows the foldout sculpt inspector; `cullCompute`, `patchMaterial`, and raw tile data are **not visible anywhere**.
- [ ] **SC2** — One renderer renders **≥2 tiles** with a single shared `lodRangesM`.
- [ ] **SC3** — Dragging the sculpt brush changes the **rendered** terrain in real time (no manual rebuild).
- [ ] **SC4** — After mouse-up the change persists to `tile.heightData` and survives domain reload.
- [ ] **SC5** — `TerrainTileAsset` inspector shows only the managed-by notice.

---

## Phase files

- [`phase-1-multitile-and-live-sculpt.md`](phase-1-multitile-and-live-sculpt.md)
- [`phase-2-cross-tile-strokes.md`](phase-2-cross-tile-strokes.md)
</content>
