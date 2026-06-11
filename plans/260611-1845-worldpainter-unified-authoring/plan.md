# Plan: WorldPainter — Unified Terrain + Grass + Props Authoring Tool

**Branch:** `plan/gpu-terrain-cdlod` · **Engine:** Unity 6 · custom GPU CDLOD terrain + GPU/CPU grass-prop scatter (NOT Unity Terrain, NOT DOTS)
**Source of truth:** `plans/reports/260611-worldpainter-unified-terrain-grass-props-design.md` (APPROVED across 6 decision rounds). This plan encodes the approved decisions; it does not re-decide them.
**Conventions:** `.claude/rules/code-conventions-unity.md` — `this.` mandatory, camelCase private fields (no `_`), PascalCase public, `UPPER_SNAKE_CASE` consts, files ≤200 lines → split partials, `#nullable enable` in new files.
**Verify-once discipline:** `.claude/rules/ai-velocity-batch-compile-unity.md` — per-phase compile gate = `read_console` (ALL errors) + `run_tests` (ALL failures) in ONE pass; never per-edit. `refresh_unity` no-ops on asmdef-only edits → force or touch a `.cs`.
**Editor-tool safety:** `.claude/rules/unity-forbidden-operations.md` — never kill/restart Unity, never `Reimport All`; MCP timeout ≠ disconnect (diagnose, wait, retry).

---

## 1. Thesis (from design §3)

All four authoring activities are the **same gesture**: drag a falloff brush over the terrain surface and modify a per-texel or per-instance payload.

| Activity | Payload | Sink |
|---|---|---|
| Sculpt | height | per-tile height RT → `TerrainTileAsset.heightData` |
| Splat | splat weights | per-tile splat RT → `TerrainTileAsset.splatData` |
| Grass | density | per-layer density RT → `DensityScatterLayer` density map |
| Props | instance records | `AuthoredInstancesData` records |

**One brush engine, four payloads.** This is the backbone and the reason a single merged `WorldPainter` component is coherent.

---

## 2. Locked decisions (do NOT re-open)

- **D1** — ONE flat merged `WorldPainter` MonoBehaviour: single user-facing component + single UIToolkit inspector.
- **D2** — flat facade + asset references for bulk data: Tier A config inline on the component; Tier B per-tile `TerrainTileAsset` and Tier C layer/biome/brush data stay as **disk assets** (keeps streaming, keeps scene light). Never inline bulk bytes.
- **Core IA** — Photoshop-style **Layer Stack** (layer type IS the mode). Hybrid filter chips (All/Height/Splat/Grass/Props) ship in **P1**.
- **Biome composite brush** — headline feature, **P5**.
- **Undo** — stroke-snapshot ring **depth 10, ~128 MB cap** (evict oldest); unified with Unity `Undo` (single Ctrl+Z).
- **Prop far-LOD** (impostor/billboard) — in scope for **P4**.
- **Pen pressure** — skipped (not planned).
- **UX north-stars** — visual polish + speed + beginner discoverability; NOT Unity-Terrain mimicry. Reuse Scatter Studio USS tokens + `AnchorPreviewPanel` `PreviewRenderUtility` LOD0 path.

---

## 3. Architecture

### 3.1 Component split (design §6 editor-only isolation — MANDATORY)

```
Assets/GpuTerrain/Runtime/WorldPainter.cs              ← runtime: serialized Tier-A data + LateUpdate submit scheduler. SHIPS in builds.
Assets/GpuTerrain/Runtime/WorldPainter.Data.cs         ← Tier-A inline schema (worldGrid, tile refs, splat/scatter/biome/brush refs). partial, ≤200 lines.
Assets/GpuTerrain/Editor/WorldPainter.Authoring.cs     ← #if UNITY_EDITOR: brush engine driver, previews, readback, histograms. NEVER ships / never runs in play.
```

`WorldPainter.cs` owns runtime submit only (drives the existing render engines per residency/visibility). All brush/preview/readback/histogram code lives under `#if UNITY_EDITOR` in the Editor assembly. This keeps authoring out of player builds and out of play mode (design §6).

### 3.2 Unified brush engine (design §5 — the shared spine)

```
Assets/GpuTerrain/Shaders/BrushMask.hlsl       ← NEW include: per-texel weight = falloff-LUT × optional stamp × strength × sign
Assets/GpuTerrain/Shaders/TerrainBrush.compute ← refactored: height/splat/density kernels each a one-liner over BrushMask.hlsl
```

- **Falloff = curve** baked to a 256×1 `RFloat` LUT, re-uploaded on `CurveField` change (design §5.2).
- **Spacing-stamping stroke model** — interpolate drag path, stamp every `spacing` metres; unifies sculpt/splat/density AND props (a stamp = a scatter emit) AND biome (a stamp fans to N payloads) (design §5.4).
- `DensityPaintGPU` folds INTO the shared compute (retires the duplicate path) — **P3**.
- Writeback = extend `TerrainSculptRtWriteback` with a density encoder on the same throttled 0.15s pipeline + mouse-up `ExecuteSync` flush (design §5.6) — **P3**.

### 3.3 Frozen SSOT (design §7.5 — do NOT touch)

`TerrainTileAsset`, `TerrainWorldGrid`, `TerrainHeightFormat`, `CdlodQuadtree`, `ScatterLod`, `ScatterLayer`/`DensityScatterLayer`/`InstanceScatterLayer`, `AuthoredInstancesData`, `ChunkedInstanceBuffer`, `ISurfaceSampler`/`HeightmapSurfaceSampler`. Freezing these is what lets the bulk of the **216 EditMode tests** (178 GpuTerrain + 38 GrassInteract) survive. Only renderer/owner-level ownership tests re-home onto `WorldPainter`.

### 3.4 Decoupling seam preserved (library rule)

`HeightmapSurfaceSampler : ISurfaceSampler` is unchanged. The merged component wires terrain height → scatter grounding **internally** (replacing the `GpuTerrainScatterGround` bridge MonoBehaviour's wiring), interface untouched. See `.claude/rules/library-third-party-decoupling.md`.

---

## 4. Phase index

| Phase | Name | Effort | Blocked by |
|---|---|---|---|
| **P1** | Vertical slice: shell + layer stack + filter chips + unified brush engine + Sculpt + migration + test re-home + **GATE** | **L** | — |
| **P2** | Splat — multi-layer painting + palette swatches | **M** | P1 |
| **P3** | Grass — scatter layers in stack + LOD0 preview + LOD band-ruler + density fold-in | **L** | P1 (gate), P2 |
| **P4** | Props — prop layers + scatter-paint stamps + ghost preview + incremental bake + impostor far-LOD | **L** | P1, P3 |
| **P5** | Biome brush — `BiomePreset` schema + card palette + per-channel contribution | **M** | P2, P3, P4 |
| **P6** | Polish & discoverability — animations, readouts, mini-map, perf badge, scene HUD, coach marks | **M** | P1–P5 |

Phase detail files: `phase-1.md` … `phase-6.md`.

**Critical path:** P1 → (GATE) → P3 → P4 → P5 → P6. P2 parallelizable with early P3 work after P1 (shares the brush engine but distinct payload kernel + UI).

---

## 5. Cross-cutting constraints (apply to EVERY phase)

1. **Freeze SSOT data/math types** (§3.3). Any change to a frozen type is a STOP-and-ask. Test-migration is a tracked task (P1) with explicit blast-radius: re-home only `GpuTerrainRenderer`/owner-level tests onto `WorldPainter`; keep the 216 data/math tests green.
2. **Preserve `HeightmapSurfaceSampler : ISurfaceSampler`** — wiring moves inside `WorldPainter`, interface unchanged.
3. **Runtime/authoring split** — `WorldPainter.cs` runtime-only; `WorldPainter.Authoring.cs` under `#if UNITY_EDITOR`. CI/build must not pull authoring into player builds.
4. **Unified brush engine** — one `BrushMask.hlsl` consumed by height/splat/density kernels; spacing-stamping; fold `DensityPaintGPU` into the shared compute. Reuse `TerrainSculptRtWriteback` (+density encoder) and `TerrainPaintTargetResolver` (multi-tile).
5. **Files ≤200 lines** — split partials by responsibility (data / authoring / brush / preview).
6. **Verify-once per phase** — `read_console` (all errors) + `run_tests` (all failures) in one pass; fix the batch; re-verify once. Never per-edit.
7. **Editor-tool forbidden ops** — never kill/restart Unity, never `Reimport All`; MCP timeout = wait+retry.

---

## 6. Cross-phase risk register (L×I, score ≥15 = high → mitigate before phase starts)

| # | Risk | L | I | Score | Mitigation | Owner phase |
|---|---|---|---|---|---|---|
| R1 | Flat-merge regresses the 216 EditMode tests (ownership/test re-home breaks data tests) | 4 | 5 | **20** | Freeze SSOT types (§3.3); re-home ONLY owner-level tests; run full suite before/after each phase; bound blast-radius in P1 test-migration task | P1 |
| R2 | Brush-engine refactor (`TerrainBrush.compute` → `BrushMask.hlsl`) breaks live sculpt that already works on `plan/gpu-terrain-cdlod` | 4 | 4 | **16** | P1 ships Sculpt end-to-end through the new include BEFORE adding payloads; keep `TerrainBrushMathTests` (22) + `DensityBrushMathTests` (13) green as the contract | P1 |
| R3 | Editor authoring code leaks into player build (split discipline fails) | 3 | 5 | **15** | Strict `#if UNITY_EDITOR` + Editor-asmdef ownership; P1 build-isolation verify task (compile a player build target, grep authoring symbols absent) | P1 |
| R4 | Authoring responsiveness collapses at many layers × many tiles (per-repaint previews / CPU recounts) | 4 | 4 | **16** | Cache `PreviewRenderUtility` thumbnails (invalidate on mesh/mat change); async GPU counters; per-frame dispatch cap + queue; spacing-stamping bounds dispatch — enforced P3/P4, measured at P1 gate | P3/P4 |
| R5 | Unified stroke-undo ↔ Unity Undo collapse corrupts history (interleaved snapshot + structural) | 3 | 4 | 12 | Extend `TerrainSculptUndo` (depth 10, 128 MB cap, evict oldest) behind one `Undo` group per stroke; keep `TerrainSculptUndoTests` (11) green; add density+records snapshot tests | P1 (frame) / P3-P4 |
| R6 | Tile stream-out mid-stroke corrupts a multi-tile/biome edit | 3 | 4 | 12 | `TerrainPaintTargetResolver` takes residency set; pin touched tiles against `TerrainStreamingManager`/`TerrainResidencyRing` stream-out for the stroke; grey non-resident tiles | P2/P5 |
| R7 | Migration from `GpuTerrainRenderer` + `TerrainScatterConfig` loses or mis-maps data | 3 | 4 | 12 | One-time migration menu reusing `TerrainValidationSceneBuilder`; dry-run report before write; keep originals until user confirms | P1 |

---

## 7. Timeline

| Phase | Effort | Notes / blocker |
|---|---|---|
| P1 | L | none — vertical slice + manual GATE (ergonomics + flat-merge cost) before P2 |
| P2 | M | blocked by P1 |
| P3 | L | blocked by P1 gate + P2 |
| P4 | L | blocked by P1, P3 |
| P5 | M | blocked by P2, P3, P4 |
| P6 | M | blocked by P1–P5 |
| **Total** | **L+M+L+L+M+M ≈ 4L-equivalent** | Critical path: P1 → P3 → P4 → P5 → P6 (P2 overlaps early P3) |

---

## 8. Success criteria (design §11)

- [ ] **SC1** — A designer drops ONE `WorldPainter` component and sculpts, paints splat, paints grass, places props, and paints biomes — all from the inspector + scene view, no separate window.
- [ ] **SC2** — One brush vocabulary (size/strength/falloff/spacing/flow) works identically across every layer type.
- [ ] **SC3** — Selecting a scatter layer shows a live LOD0 preview + a Scatter-Studio-styled LOD band-ruler editor.
- [ ] **SC4** — Editor stays responsive at many layers × many tiles (cached previews, async counters, dispatch cap) — measured at the P1 gate, enforced P3/P4.
- [ ] **SC5** — Existing streaming + the bulk of the 216 EditMode tests survive the merge.
- [ ] **SC6** — Visual parity with Scatter Studio's token system; premium feel (mode colors, live readouts, animations).

---

## 9. Cook handoff

`/t1k:cook plans/260611-1845-worldpainter-unified-authoring/plan.md --phase 1`
