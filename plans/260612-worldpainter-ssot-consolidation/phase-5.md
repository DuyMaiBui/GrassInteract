# Phase 5 — Layers (3-section palette + per-tile channel allocation)

**Effort:** L · **Wave:** D (PARALLEL fan-out) · **Depends on:** P1 (lifecycle/alloc API), P2 (container read) · **Blocks:** P6 (active-layer API), P7 (layer defs)

## Goal

Activating a grass/scatter layer creates the `ScatterLayer` sub-asset, appends to the map layer list, and **allocates an empty R8 256×256 density channel on every existing tile**. New tiles auto-allocate channels for all active layers. Remove frees channels + sub-asset (disciplined). UI: 3-section palette (Splat / Meadow / Prop), each with its own `+` and LOD0 thumbnails; click a square = active paint layer (never selects a tile).

## File-ownership group (this phase = ONE concurrent subagent in WAVE D)

**G5.1 — Channel allocation (Editor, via P1 API)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldMapAssetLifecycle.cs` *(extend — P1 owns the file but the alloc bodies land here; P1 ships stubs `AllocLayerChannels`/`FreeLayerChannels`, P5 fills them)*. **Coordination:** P1 must ship these stubs so P5 fills bodies without racing P1. If P1 cannot pre-stub, this sub-step moves to WAVE E.
  - On `AddLayer`: for each tile, allocate empty R8 256×256 density channel keyed by `layerId`.
  - On `RemoveLayer`: `RemoveObjectFromAsset` + `DestroyImmediate` def; free each tile's matching channel.
  - On `AddTile` (new tile): auto-allocate channels for all active density layers.

**G5.2 — 3-section palette UI (Editor, palette/card files — NON-overlapping with P4/P7)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterSplatPaletteView.cs` *(edit)* — Splat section + `+`; albedo thumbnails.
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterScatterLayerCard.cs` *(edit)* — Meadow section + `+`.
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterSplatLayerCard.cs` *(edit)* — splat layer rows.
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterPreviewCache.cs` *(edit)* + `Assets/WorldPainter/Editor/WorldPainter/WorldPainterLodPreviewPanel.cs` *(edit)* — LOD0 mesh+material thumbnails via custom `PreviewRenderUtility` for grass/prop squares.
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterState.cs` *(edit)* — **active-paint-layer** selection state (the API P6 + P7 consume). Click a square sets active layer; never selects a tile.

> **Prop section** (3rd section) UI shell is created here (the `+` and square grid); its dual-mode placement behavior is P7. P5 ships the prop *layer list + activation*; P7 ships *placement*.

## Non-overlap proof (WAVE D safety)

- P5 owns `WorldPainterSplatPaletteView`, `WorldPainterScatterLayerCard`, `WorldPainterSplatLayerCard`, `WorldPainterPreviewCache`, `WorldPainterLodPreviewPanel`, `WorldPainterState`.
- P7 owns `WorldPainterPropLayerCard`, `WorldPainterPropStampEmitter` (disjoint).
- P4 owns factory/overlay/inspector-root (disjoint). P8 owns `Runtime/Terrain` (disjoint).
- Shared `WorldMapAssetLifecycle.cs` resolved by the pre-stub rule above (P1 ships stubs).

## Parallelizable vs sequential

**Parallel** with P4/P7/P8. Internally: G5.1 (alloc) before G5.2 demos (palette activation calls alloc), but both owned by one subagent.

## Verification

1. **Compile:** `read_console` + `run_tests` in one pass.
2. **New test:** `WorldMapLayerAllocTests.cs` — with 3 existing tiles, activate a density layer → assert each tile gained an R8 256×256 channel for that `layerId`; remove → assert channels freed and zero orphan sub-assets.
3. **Preview test (light):** `PreviewRenderUtility` produces a non-null thumbnail texture for a grass LOD0 mesh+material.
4. Palette interaction: clicking a square sets active layer in `WorldPainterState` and does NOT change Selection (no tile selected).

## Success criteria (maps to design success criterion 5)

- Layer palette: 3 sections (Splat/Meadow/Prop) each with its own `+` and LOD0/albedo previews.
- Activating a density layer allocates per-tile R8 256×256 channels on **every** existing tile; new tiles auto-allocate.
- Remove frees channels + sub-asset with zero orphans.
- Clicking a square sets the active paint layer (consumed by P6/P7) and never selects a tile.
