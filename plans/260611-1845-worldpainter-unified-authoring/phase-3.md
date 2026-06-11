# Phase 3 — Grass: Scatter Layers in the Stack + LOD0 Preview + LOD Band-Ruler + Density Fold-In

**Effort:** L · **Blocked by:** P1 (gate), P2 · **Blocks:** P4, P5

## Goal

Bring grass into the unified tool: scatter (grass) layers in the layer stack, the **LOD0 orbit preview** on layer-select + a **Scatter-Studio-style LOD band-ruler editor**, and fold the **density payload into the shared compute** — retiring `DensityPaintGPU`'s duplicate path and extending `TerrainSculptRtWriteback` with a density encoder.

## File ownership (concrete paths)

### New
| Path | Responsibility | ≤200 lines |
|---|---|---|
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterScatterLayerCard.cs` | selected scatter-layer card (density slider, slope, align-normal, jitter, live blade count) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLodPreviewPanel.cs` | 220px LOD0 orbit preview — wraps `AnchorPreviewPanel`'s `PreviewRenderUtility` path (drag=orbit, scroll=zoom) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLodBandRuler.cs` | horizontal LOD thumb strip + draggable distance-band ruler → `ScatterLod.maxDistance` | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterPreviewCache.cs` | cached `PreviewRenderUtility` thumbnails (render once, invalidate on mesh/mat change) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterDensityEncoder.cs` | density RT → density-map bytes encoder on the 0.15s writeback pipeline | yes |

### Modified
| Path | Change |
|---|---|
| `Assets/GpuTerrain/Shaders/TerrainBrush.compute` | add **Density** kernel as a one-liner over `BrushMask.hlsl` (folds in `DensityPaintGPU` logic) |
| `Assets/GpuTerrain/Editor/TerrainSculptRtWriteback.cs` | add density encoder branch on the same throttled 0.15s pipeline + mouse-up `ExecuteSync` flush |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLayerStackView.cs` | scatter rows expand to the card; collapsed rows show cached 24px LOD0 thumb |
| `Assets/GpuTerrain/Runtime/WorldPainter.cs` | LateUpdate submit drives `ScatterField` per scatter layer; wire `HeightmapSurfaceSampler` grounding internally (replaces `GpuTerrainScatterGround` wiring) |

### Retire (after fold-in, per design §5.3)
| Path | Action |
|---|---|
| `Assets/GrassInteract/Editor/ScatterStudio/DensityPaintGPU.cs` | logic folded into `TerrainBrush.compute` Density kernel; remove duplicate path AFTER `DensityBrushMathTests` re-pass through the shared compute (pre-delete reference grep first) |

### Reuse unchanged (cite — frozen SSOT)
`ScatterLayer.cs`, `DensityScatterLayer.cs`, `ScatterLod` (frozen), `ScatterField.cs`, `GrassTierProbe.cs` (auto-tier above ~50k blades), `GrassCull.compute`, `AnchorPreviewPanel.cs`, `ScatterBrushPreview.cs`, `HeightmapSurfaceSampler.cs`/`ISurfaceSampler.cs` (seam), `BrushMask.hlsl` (P1).

## Tasks (each with verify-check)

1. **Scatter layer rows** — add Grass via `+▾` with smart defaults (density/slope/jitter); stack row + mode-color lime. → verify: new grass layer renders blades in scene through `ScatterField`.
2. **LOD0 orbit preview** — `WorldPainterLodPreviewPanel` wraps `AnchorPreviewPanel`'s `PreviewRenderUtility` (`BeginPreview`/`DrawMesh(LodMeshes[0])`/`EndPreview` → `GUI.DrawTexture`). → verify: selecting a grass layer shows the 220px orbit preview; drag orbits, scroll zooms; collapsed = cached 24px thumb.
3. **LOD band-ruler editor** — thumb strip (each LOD its own cached preview) over a draggable distance ruler writing `ScatterLod.maxDistance`. → verify: dragging a band changes that LOD's cull distance live; `run_tests` `ScatterLodCullTests` (7) green.
4. **Preview cache** — `WorldPainterPreviewCache`: render once, invalidate on mesh/material change; never per-repaint (design §6). → verify: inspector repaint does NOT re-render previews (profile: zero `PreviewRenderUtility.Render` on idle repaint).
5. **Density kernel fold-in** — Density kernel over `BrushMask.hlsl`; same falloff LUT + stamp + spacing as sculpt/splat. → verify: `run_tests` `DensityBrushMathTests` (13) green through the shared compute.
6. **Density writeback encoder** — RT → density bytes on the 0.15s pipeline + mouse-up `ExecuteSync`. → verify: painted density persists to `DensityScatterLayer` map; survives domain reload.
7. **Retire `DensityPaintGPU`** — pre-delete reference grep; remove only after task 5 green. → verify: grep shows zero live refs; full suite green.
8. **Live blade count** — async GPU counter (`AsyncGPUReadback`) on the 0.15s tick, never CPU recount (design §6). → verify: count updates during stroke; no per-frame readback stall.

## Risk Assessment (L×I)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Density fold-in diverges from `DensityPaintGPU` behavior | 4 | 4 | **16** | `DensityBrushMathTests` (13) is the contract; fold-in then test BEFORE deleting old path |
| Per-repaint `PreviewRenderUtility` tanks inspector FPS | 4 | 4 | **16** | `WorldPainterPreviewCache` mandatory; invalidate only on mesh/mat change; profile idle repaint |
| Removing `DensityPaintGPU` breaks a hidden caller | 3 | 4 | 12 | Pre-delete reference grep across Runtime+Editor+Tests; delete only after suite green |
| Internal grounding wiring drops `ISurfaceSampler` decoupling | 2 | 5 | 10 | Interface unchanged; `HeightmapSurfaceSamplerTests` (7) green; seam preserved per library rule |

## Test plan

- `run_tests`: `DensityBrushMathTests` (13), `ScatterLodCullTests` (7), `HeightmapSurfaceSamplerTests` (7), `AuthoredInstancesDataBlobTests` (4) green.
- New: density-encoder round-trip (RT→bytes→map), band-ruler→`maxDistance` mapping, preview-cache invalidation.
- Manual: LOD0 orbit preview interaction; density paint persists; blade count live.
