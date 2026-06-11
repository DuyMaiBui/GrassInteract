# WorldPainter — Unified Terrain + Grass + Props Authoring Tool (Design Report)

**Date:** 2026-06-11
**Branch context:** `plan/gpu-terrain-cdlod`
**Type:** Brainstorm / design report (feeds a phased implementation plan)
**Status:** Direction approved by user across 6 decision rounds.

---

## 1. Problem statement

Two mature systems exist in the repo with **divergent authoring UIs**:

- **GPU Terrain** — CDLOD GPU-indirect renderer, multi-tile, in-inspector sculpt + splat paint via an **IMGUI** component inspector (`GpuTerrainRendererEditor`) plus a scene-view `EditorTool` (`TerrainSculptTool`, multi-tile GPU brush, async writeback, per-tile undo).
- **GrassInteract** — GPU/CPU grass + prop scatter, authored through a dockable **UIToolkit "Scatter Studio" EditorWindow** (tabs: Layer / Brushes / Paint / Place), with a density paint tool and instance placement tool.

They are already one-way decoupled (`GpuTerrainScatterGround` feeds terrain height into grass grounding via `HeightmapSurfaceSampler : ISurfaceSampler`).

**Goal:** combine both into a single **Editor authoring tool** — terrain sculpt + splat paint + grass paint layers + prop placement — all edited in **one MonoBehaviour's UIToolkit inspector**, with a premium, fast, discoverable UX.

---

## 2. Approved decisions

| # | Decision | Choice |
|---|---|---|
| Hosting | Where the UI lives | **Inspector-only (strict)** — one MonoBehaviour, UIToolkit; retire the Scatter Studio window |
| Runtime scope | What "runtime" means | **Editor-only authoring** — runtime = the existing render systems that already ship in builds |
| Component model | How systems unify | **One merged component** (`WorldPainter`) |
| UX north-stars | Where to spend design effort | **Visual polish / "wow" + Speed & ergonomics + Beginner discoverability** (explicitly *not* slavish Unity-Terrain mimicry) |
| Core IA | Inspector paradigm | **Layer Stack (A2)** — Photoshop-style; the layer type *is* the mode |
| Biome brush | Centrality | **Headline feature** |
| **D1** | Merge internals | Flat merged component at the API surface… |
| **D2** | Bulk data storage | …but **flat facade + asset references** for bulk data (keeps streaming, keeps scene light) |

D1+D2 together: **one component, one inspector, one inline config object — that references bulk tile/layer/biome assets on disk.** Flat to the user; asset-backed underneath where the streaming engine requires it.

---

## 3. Core thesis

All four activities are the **same gesture**: drag a falloff brush over the terrain surface and modify a per-texel or per-instance payload.

- Sculpt → writes height RT
- Splat → writes splat RT
- Grass → writes density RT
- Props → emits instance records

So: **one brush engine, four payloads.** This is the engineering and UX backbone of the whole tool, and the reason a single merged component is coherent.

---

## 4. UI/UX design

### 4.1 Information architecture — Layer Stack (A2)

Photoshop-style vertical stack; selecting a layer sets the active brush payload. No mode toolbar — the layer type is the mode.

```
 LAYERS                              [+ ▾]
 👁  ◧🌳  [▣ LOD0]  Trees            🔒 ◉
 👁  ◧🌿  [▣ LOD0]  Meadow grass
 👁  ◧🎨  [▓ albedo] Rock
 👁  ◧🎨  [▓ albedo] Grass
 🔒  ◧⛰   [⛰]       Height (base)
```

- **eye** = show/hide in scene · **lock** = exclude from brush · **solo** (alt-click) = isolate.
- **drag to reorder** = splat blend priority / scatter draw order.
- Selected row tint = `rgba(63,127,210,.35)` (exact Scatter Studio token).
- **Add-layer guided menu** (`+ ▾`): Height · Splat · Grass · Prop · Biome — each opens with smart defaults, never an empty row.
- **Multi-select** (ctrl/shift) for batch eye/lock/delete; the brush still acts only on the single active (◉) layer to keep strokes unambiguous.
- Optional **hybrid filter chips** (All · ⛰ · 🎨 · 🌿 · 🌳) on top for beginner scope-narrowing (A3 fallback if vertical height becomes a problem).

### 4.2 Selected scatter layer → LOD0 preview + LOD editor (user-requested)

Selecting a scatter layer expands its row into a card (radius `5px`, BG `rgb(55,55,55)`):

```
┌─ Meadow grass ─────────────────────────── ◧ scatter ─┐
│  ┌──────────────────────┐  density ●━━━━ 850/m²       │
│  │  LOD0 orbit preview   │  slope ≤ ●━━ 30°            │
│  │  (PreviewRenderUtility│  align-normal ☑  jitter ●━  │
│  │   yaw130 pitch-18)     │  [▦ density heatmap]       │
│  └──────────────────────┘  live: 12,402 blades        │
│  ── LODs (ScatterStudio style) ───────────────────────│
│  [▣LOD0][▣LOD1][▣LOD2] (+)   each: thumb + mesh        │
│  dist 0───15m────40m────80m  ◀ draggable band ruler    │
└────────────────────────────────────────────────────────┘
```

- **LOD0 preview at select** = the 220px orbit preview reused verbatim from `AnchorPreviewPanel` (`PreviewRenderUtility` → `BeginPreview`/`DrawMesh(LodMeshes[0])`/`EndPreview` → `GUI.DrawTexture`; drag=orbit, scroll=zoom). Collapsed rows show a cached 24px LOD0 thumb.
- **LOD editor in Scatter Studio style** = horizontal LOD thumbnail strip (each LOD = its own `PreviewRenderUtility` thumb) over a **draggable distance-band ruler** that sets each `ScatterLod.maxDistance`. Card chrome, fonts (13px bold / 12px), accent borders all mirror Scatter Studio.

### 4.3 Biome brush (headline feature — the payoff of merging)

```
┌─ BIOME BRUSHES ──────────────────────────┐
│  [FOREST] [CLIFF] [MEADOW] [PATH] [+]      │  ← big thumbnail cards
│  "Forest" = +height noise · dirt splat     │
│            · scatter ferns+trees · grass   │
│  contribution: ⛰▣ 🎨▣ 🌿▣ 🌳▣ (per-stroke) │
└────────────────────────────────────────────┘
```

A **`BiomePreset`** ScriptableObject bundles `{height-delta rule, splat layer+weight, grass layer+density, prop palette+scatter rule}`. One stroke paints all channels; per-stroke toggles mute any channel. Impossible with three separate windows; trivial once the brush stroke loop is payload-agnostic (§5.3). Massive level-blockout speed win.

### 4.4 Shared brush dock (constant across every layer)

```
BRUSH  size ●━━━━━ 12m  ⟲0°±15°   falloff ╱▔╲ [⌣][╱][⎍][▢][◎]
strength ●━━ 0.4  flow ●━ 0.8  spacing ●━ 2m  stabilizer ●━ 0.3
stamp [▦soft][▦noise][▦rocks](+)  presets [F1★][F2][F3](+)  X=swap
```

Inspector zones **header / brush dock / footer never move** between layers — only the stack + selected-layer card change. Constant muscle memory for size / strength / undo / save is the biggest ergonomic win over today's split tools.

### 4.5 Scene-view HUD & gizmos (eyes-on-terrain)

- **Overlays-API toolbar** (`UnityEditor.Overlays`): active-layer chip (+ LOD0 mini-thumb), brush size readout, mode-color dot — switch layers without leaving the scene.
- **Brush gizmo**: mode-colored disc (reuse `ScatterBrushPreview` decal pattern), inner+outer falloff rings, strength = fill opacity, live number floating at cursor.
- **Radial scrub**: modifier+drag → horizontal=size, vertical=strength.
- **`Shift` = universal inverse** (raise→lower / paint→erase / scatter→delete) · **`Alt` = eyedropper** (sample layer/biome under cursor) · **`Ctrl` = smooth** while sculpting.
- **Prop ghost** on hover reuses `InstanceGhostPreview` (green=valid / red=blocked).
- Optional **symmetry / mirror axis** for sculpting.
- **Active-layer label at cursor** so a stroke's effect is never ambiguous.

### 4.6 Visual theme & USS

One `WorldPainter.uss` + `WorldPainterLight.uss`, `.pro`/`.light` on root via `EditorGUIUtility.isProSkin`. **Reuse the exact Scatter Studio tokens** so the tools feel like one product:

| Token | Pro | Light |
|---|---|---|
| Root BG | `rgb(45,45,45)` | `rgb(196,196,196)` |
| Card BG | `rgb(55,55,55)` | `rgb(210,210,210)` |
| Selected row | `rgba(63,127,210,.35)` | `rgba(63,127,210,.28)` |
| Accent / active border | `rgb(77,153,255)` | `rgb(35,95,175)` |
| Card radius / font / title | `5px` / `12px` / `13px bold` | · |
| Row h / chip | `28px` / `8px round` | · |

**Mode-color layer (new):** Height=green · Splat=orange · Grass=lime · Props=teal · Biome=violet — tints type-chip, active-layer ring, scene brush ring, and a 3px inspector edge strip. Makes wrong-layer mistakes nearly impossible.

**Premium touches:** 120ms ease on expand/collapse + tab underline slide · hover card elevation · **live readout strip** (height histogram / density heatmap / instance counts animating during a stroke) · header **mini-map** of loaded tiles + camera dot · brush-stamp `72×92` wrap-grid with 2px blue selected border.

### 4.7 Onboarding & discoverability

- **Empty states**: no tiles → "No terrain yet · [Create 1×1 tile] [Import heightmap]" (wraps `TerrainValidationSceneBuilder`); empty stack → "Add your first layer ▾".
- **Coach marks**: first selection of each layer type → one-line dismissible tip, `EditorPrefs`-gated, shown once.
- **`?` header popover**: compact cheat-sheet (hotkeys, modes, biome how-to).
- **Guided "first world" flow** (optional): create tile → sculpt hill → paint biome → done.
- **Smart defaults** (12m / 0.4 / smooth falloff / sensible density) so the first stroke looks good.
- Tooltips everywhere; biome cards show channel contribution on hover.

---

## 5. Brush engine (the shared spine)

### 5.1 One `BrushSettings` (SSOT; also a saveable `BrushPreset` asset)

`size(m)`, `strength[0..1]`, `falloff(AnimationCurve)`, `hardness`, `stamp(Texture2D?)` + rotation + jitter, `spacing(m)`, `flow[0..1]`, `stabilizer[0..1]`, `symmetry`. Old params map straight in (terrain `strength` ≡ density `Opacity`; density `falloff` scalar → curve), so no behavior is lost — one vocabulary learned once.

### 5.2 Falloff = curve, not scalar

Editable `CurveField` baked to a **256×1 RFloat LUT** uploaded to the compute on change; kernels sample by normalized distance. Presets: smooth / linear / sharp / constant / ring. Enables profiles impossible with today's scalar.

### 5.3 Shared brush-mask kernel (the real SSOT win)

Refactor `TerrainBrush.compute` so one HLSL include (`BrushMask.hlsl`) computes the per-texel weight (`falloff LUT × optional stamp × strength × sign`), and each payload kernel is a one-liner over it:

- Height raise/lower/smooth/flatten · Splat blend · **Density** (folds `DensityPaintGPU` into the same compute, retiring the duplicate path).

One mask, one falloff, one stamp system — applied identically to height, splat, and grass density.

### 5.4 Stroke model: spacing-stamping unifies all payloads

Interpolate the drag path and **stamp every `spacing` metres** (instead of speed-dependent per-frame dispatch):

- Sculpt/splat/density: consistent regardless of mouse speed; `flow` accumulates per stamp.
- **Props for free**: a stamp at spacing intervals *is* scatter-painting — each stamp emits jittered instance records instead of writing an RT.
- **Biome for free**: a stamp fans out to multiple payloads at once.

### 5.5 Modifiers / stabilizer / symmetry

All uniform-driven, no per-payload code: `Shift`→`_Sign=-1`, `Ctrl`→smooth kernel, `Alt`→eyedrop; **stabilizer** = lazy-mouse leash (pure C# input filter); **symmetry** = dispatch the same stamp at mirrored/rotated UVs in one stroke.

### 5.6 Writeback

Extend async `TerrainSculptRtWriteback` (height+splat) with a **density encoder** (RT → density-map bytes) on the same throttled 0.15s pipeline + mouse-up `ExecuteSync` flush. Props need no readback (records mutate directly, undo-snapshotted). One commit pipeline, three encoders.

---

## 6. Performance & scale

**Two budgets:** runtime render (engines unchanged — low merge impact) vs **editor authoring** (where the tool lives or dies).

- **Runtime**: one `WorldPainter.LateUpdate` submit scheduler early-outs per-system on residency/visibility; existing culls (`TerrainNodeCull`, `GrassCull` Chunk→Blade, prop chunk cull) unchanged. Grass GPU tier required above ~50k blades (`GrassTierProbe` auto-selects).
- **Authoring responsiveness (dominant):**
  - **Cache `PreviewRenderUtility` thumbnails** — render once, invalidate on mesh/material change; never per-repaint.
  - **Counts/histograms via async GPU counters** (`AsyncGPUReadback`), never CPU recount; update on the 0.15s tick.
  - **Heatmap/slope overlays** computed GPU→display RT, no per-frame readback.
  - **Per-frame dispatch cap + queue** for multi-tile × multi-payload (biome) stamps; spacing-stamping bounds dispatch count.
- **Tiles at scale**: edit only resident tiles (`TerrainPaintTargetResolver` takes the residency set); non-resident tiles greyed with "load to edit"; **pin touched tiles against stream-out for the stroke** (interlock with `TerrainStreamingManager`/`TerrainResidencyRing` ~5×5).
- **Dense props**: **incremental** bake into `ChunkedInstanceBuffer` (append affected chunks, no full rebuild per stamp); one shared ghost; far-LOD impostor/billboard; `InstanceColliderPool` visibility-culled.
- **Memory/RT pooling**: per-tile height `257²R16` + splat `512²RGBA32`; working brush RTs `512²×2` released on tool deactivate; `Texture2D` reuse on commit (stale-rebind fix). Surface resident VRAM in the perf badge.
- **Editor-only isolation (flat-merge discipline):** split `WorldPainter.cs` (runtime submit + serialized data) vs `WorldPainter.Authoring.cs` under `#if UNITY_EDITOR` (brush, previews, readback, histograms) so authoring never bloats builds or runs in play.
- **Profiling as a feature**: header perf badge (draw calls · dispatches · instance count · VRAM via `rendering_stats`/`ProfilerRecorder`) + per-layer cost readout in each stack row.

---

## 7. Data model & save

### 7.1 Three tiers (D2: flat facade + asset refs)

| Tier | Content | Storage | Rationale |
|---|---|---|---|
| **A — Config** | layer metadata, LOD distances, density targets, scatter rules, splat defs, biome/brush refs, world grid, tile coord→ref table | **Inline on `WorldPainter`** | small, version-controllable, honors flat |
| **B — Tile bulk** | `TerrainTileAsset` (R16 height + RGBA32 splat bytes) | **Disk assets (unchanged)** | streaming + residency need disk + async load |
| **C — Layer bulk** | density maps, `AuthoredInstancesData` records, `BrushPreset`, `BiomePreset` | **Disk assets** | large/binary, reusable, AssetPreview-able |

Tier A holds **references** to B/C. (Truly inlining bulk bytes — rejected — would bloat the scene to tens of MB and kill streaming.)

### 7.2 Schema

```
WorldPainter (MonoBehaviour) — Tier A inline
  worldGrid    : { tileSizeM=256, heightRes=257, splatRes=512 }
  tiles        : List<{ coord, TerrainTileAsset ref }>                  → B
  splatLayers  : List<{ name, albedo, normal, tiling }>  (≤4 → RGBA32)
  scatterLayers: List<ScatterLayer ref>  (LODs + densityMap + records)  → C
  biomes       : List<BiomePreset ref>                                  → C
  brushPresets : List<BrushPreset ref>                                  → C
```

### 7.3 Disk layout

```
Assets/Worlds/<WorldName>/
  Tiles/Tile_0_0.asset …          (B, streamable)
  Layers/Grass_Meadow.asset …     (C)
  Biomes/Forest.asset …           (C)
  Brushes/Soft.asset …            (C)
```

### 7.4 Undo / save

- **Stroke undo**: per-tile byte snapshots (`TerrainSculptUndo`, extend to density + records); bounded, memory-capped, user-set cap.
- **Structural undo**: `Undo.RecordObject` (config) + `Undo.RegisterCreatedObjectUndo` (new assets).
- **Unify both through Unity `Undo`** so one Ctrl+Z walks an interleaved history (today's stroke undo is *separate* from Unity undo — collapsing them is a deliberate fix).
- **Save**: existing async writeback `SetDirty`→`SaveAssetIfDirty` on mouse-up; footer "● live" = unsaved; Save batches `AssetDatabase.SaveAssets`. Bulk-as-assets means scene Ctrl+S can't lose edits; `ExecuteSync` final flush guarantees RT bytes are written.

### 7.5 Migration & test blast-radius (cost of flat-merge)

- **One-time migration menu**: read `GpuTerrainRenderer.tiles` + `TerrainScatterConfig.layers` → build `WorldPainterData` + world folder (reuse `TerrainValidationSceneBuilder`).
- **Freeze SSOT data/math types** (`TerrainTileAsset`, `TerrainWorldGrid`, `TerrainHeightFormat`, `CdlodQuadtree`, `ScatterLod`) so most of the **209 EditMode tests survive**; only renderer/owner-level tests re-home onto `WorldPainter`. This bounds migration to the ownership layer.
- **Decoupling preserved**: `HeightmapSurfaceSampler : ISurfaceSampler` stays (library rule); the merged component wires terrain height → scatter grounding internally; interface unchanged.

---

## 8. Reuse map (low waste)

| New piece | Reuses |
|---|---|
| Brush engine | `TerrainBrush.compute`, `DensityPaintGPU`, `TerrainSculptRtWriteback`, `TerrainPaintTargetResolver`, `BrushStamp`, `TerrainSculptConfig` |
| Shared state | `TerrainSculptState` pattern → `WorldPainterState` |
| Layer stack / palette | `LayerRailView`, `LayerPanelView`, `BrushLibraryView`, `DensityPaintPanel` (port to inspector) |
| LOD0 preview | `AnchorPreviewPanel` (`PreviewRenderUtility`), `InstanceGhostPreview`, `ScatterBrushPreview` |
| Theme | `ScatterStudio.uss` / `…Light.uss` tokens → `WorldPainter.uss` |
| Grounding | `HeightmapSurfaceSampler : ISurfaceSampler` (unchanged) |
| Grass/props/terrain runtime | `ScatterField`, `DensityScatterLayer`, `InstanceScatterLayer`, `GpuTerrainEngine`, CDLOD types (unchanged) |

---

## 9. Suggested phasing (for the plan)

- **P1 — Vertical slice**: `WorldPainter` shell (UIToolkit inspector + tokens) · layer stack (Height + one Splat) · unified brush engine (mask LUT + falloff curve + spacing-stamping) · **Sculpt** working end-to-end · migration from `GpuTerrainRenderer`. *Gate: validate inspector-only ergonomics + flat-merge cost before continuing.*
- **P2 — Splat**: multi-layer splat painting on the unified brush; palette swatches.
- **P3 — Grass**: scatter layers in the stack · LOD0 preview + LOD band-ruler editor · density payload folded into the shared compute.
- **P4 — Props**: prop layers · scatter-painting via spacing-stamps · ghost preview · incremental buffer bake.
- **P5 — Biome brush**: `BiomePreset` schema · card palette · per-channel contribution.
- **P6 — Polish & discoverability**: USS animations, live readouts, mini-map, perf badge, scene-view HUD/overlay, coach marks, empty states, hotkeys.

---

## 10. Open decisions carried into the plan

- Stroke-snapshot memory cap default (MB) and undo-ring depth.
- Whether to ship A3 hybrid filter chips in P1 or defer to P6.
- Impostor/billboard far-LOD: in-scope for P4 or a later perf pass.
- Pen-pressure support (stretch; Editor pen API is limited).

---

## 11. Success criteria

1. A designer drops **one** `WorldPainter` component and sculpts, paints splat, paints grass, places props, and paints biomes — all from the inspector + scene view, no separate window.
2. One brush vocabulary (size/strength/falloff/spacing/flow) works identically across every layer type.
3. Selecting a scatter layer shows a live **LOD0 preview** and a Scatter-Studio-styled LOD editor.
4. Editor stays responsive at many layers × many tiles (cached previews, async counters, dispatch cap).
5. Existing streaming + the bulk of the 209 EditMode tests survive the merge.
6. Visual parity with Scatter Studio's token system; premium feel (mode colors, live readouts, animations).
