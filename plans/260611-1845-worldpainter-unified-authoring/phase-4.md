# Phase 4 — Props: Prop Layers + Scatter-Paint Stamps + Ghost Preview + Incremental Bake + Impostor Far-LOD

**Effort:** L · **Blocked by:** P1, P3 · **Blocks:** P5

## Goal

Add prop layers to the stack, painted via the **same spacing-stamp stroke** (each stamp emits jittered instance records — no RT), with `InstanceGhostPreview` on hover, **incremental** `ChunkedInstanceBuffer` bake (append affected chunks only), and **impostor/billboard far-LOD** for dense props.

## File ownership (concrete paths)

### New
| Path | Responsibility | ≤200 lines |
|---|---|---|
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterPropLayerCard.cs` | selected prop-layer card (mesh, scale/rotation jitter, slope mask, density-per-stamp) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterPropStampEmitter.cs` | spacing-stamp → jittered `InstanceRecord` emit (Shift=delete) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterIncrementalBake.cs` | append affected chunks into `ChunkedInstanceBuffer` (no full rebuild per stamp) | yes |
| `Assets/GpuTerrain/Runtime/WorldPainterImpostorLod.cs` | impostor/billboard far-LOD selection for prop instances (runtime, ships) | yes |

### Modified
| Path | Change |
|---|---|
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLayerStackView.cs` | prop rows (mode-color teal) expand to prop card |
| `Assets/GpuTerrain/Runtime/WorldPainter.cs` | submit drives prop instance buffers + far-LOD; chunk cull early-out |
| `Assets/GpuTerrain/Editor/TerrainSculptUndo.cs` | snapshot instance records (P1 unified undo extended to records) |

### Reuse unchanged (cite — frozen SSOT)
`AuthoredInstancesData.cs` + `InstanceRecord` (frozen), `ChunkedInstanceBuffer.cs` (frozen — extend via the bake driver, not the type), `InstanceColliderPool.cs` (visibility-culled), `InstanceGhostPreview.cs` (green=valid/red=blocked), `InstanceScatterLayer.cs`, `GrassCull.compute` chunk cull, `HeightmapSurfaceSampler` grounding.

## Tasks (each with verify-check)

1. **Prop layer model** — Grass-style `+▾` add Prop with smart defaults; `InstanceScatterLayer` ref + `AuthoredInstancesData`. → verify: prop layer renders placed instances.
2. **Spacing-stamp emit** — each stamp emits jittered `InstanceRecord`s (position/rotation/scale per jitter); `Shift` deletes under brush (design §5.4/§5.5). → verify: drag deposits evenly-spaced props regardless of mouse speed; Shift removes.
3. **Ghost preview** — `InstanceGhostPreview` on hover, green valid / red blocked (slope/overlap). → verify: ghost tints correctly on valid vs blocked surface.
4. **Incremental bake** — `WorldPainterIncrementalBake` appends only affected chunks into `ChunkedInstanceBuffer` per stamp (no full rebuild) (design §6). → verify: `run_tests` `ChunkedInstanceBufferTests` (3) green; profile shows per-stamp bake touches only affected chunks.
5. **Records undo** — instance-record snapshots in the unified undo ring (depth 10 / 128 MB). → verify: Ctrl+Z reverts a prop stroke; `InstanceVisibilityColliderDriverTests` (11) green.
6. **Impostor/billboard far-LOD** — `WorldPainterImpostorLod` selects impostor/billboard beyond a distance for dense props (runtime, ships in build). → verify: distant props swap to billboard; near props full mesh; manual perf check shows draw-call drop at distance.
7. **Collider pooling** — `InstanceColliderPool` visibility-culled for placed props. → verify: collider count tracks visible set, not total.

## Risk Assessment (L×I)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Incremental bake corrupts `ChunkedInstanceBuffer` chunk ranges | 4 | 4 | **16** | Append-only affected chunks; `ChunkedInstanceBufferTests` (3) gate; assert chunk-range invariants post-bake |
| Dense-prop stamping floods dispatch/instance count | 3 | 4 | 12 | Spacing-stamping bounds count; per-frame dispatch cap + queue; impostor far-LOD relieves render |
| Impostor LOD is runtime code → must ship cleanly (not authoring) | 2 | 4 | 8 | `WorldPainterImpostorLod` in Runtime asmdef, no `#if UNITY_EDITOR`; build-isolation check (P1 task 12 pattern) |
| Records undo desyncs from RT-snapshot undo | 3 | 4 | 12 | One `Undo` group per stroke spans both record + RT snapshots; test interleaved Ctrl+Z |

## Test plan

- `run_tests`: `ChunkedInstanceBufferTests` (3), `InstanceVisibilityColliderDriverTests` (11), `AuthoredInstancesDataBlobTests` (4) green.
- New: spacing→record-count math, incremental-bake affected-chunk-only assertion, impostor distance-threshold selection.
- Manual: ghost validity tint; far-LOD swap; Shift-delete.
