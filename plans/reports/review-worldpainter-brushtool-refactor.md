# Code Review: WorldPainter IBrushTool refactor

Scope reviewed: brush-tool refactor (BindAndDispatch → IBrushTool registry + contextual palette).
Note: the working tree also contains UNRELATED changes (WorldMapAssetLifecycle SSOT, scatter-card
rewrite, LOD-preview-panel deletion, grass/scatter layer lifecycle moved to map sub-assets). Those
are out of the stated review scope; reviewed only for non-breakage (no dangling refs; compiles).

## Critical
1. **`_SplatMode` stale-uniform regression** — TerrainBrush.compute + WorldPainterBiomeStamp.cs:96-101.
   The new Splat-erase tool sets `_SplatMode=1` on the shared ComputeShader; the uniform persists
   across dispatches. `WorldPainterBiomeStamp`'s PaintSplat dispatch never sets `_SplatMode`, so after
   the user erases a splat layer once, any subsequent Biome stamp runs PaintSplat in ERASE mode
   (subtract) instead of paint. Repro: erase a splat layer, then paint with a Biome layer → biome
   splat channel subtracts. Fix: set `_SplatMode=0` in the biome splat dispatch (or reset it in
   BindAndDispatch's common-uniform block).

## High
2. **EffectiveLayerType precedence divergence (Grass vs Props)** — WorldPainterState.cs:103-113.
   New precedence is unconditional `legacy==Grass → Grass`, dropping the original's
   `kind==None` guard on the legacy-Grass branch. Reachable desync: the layer STACK
   (RowHelpers.SelectLayer:25) sets only `ActiveLayerIndex`, never `ActiveLayerKind`, so a stale
   P5 `Prop` kind survives. State `legacy==Grass, p5==Prop`:
   - Original routed → Props (prop emit).
   - New routes → Grass (density paint).
   This is a behavior change for that combination. The new result (paint the stack-selected grass
   row) is arguably *more* intuitive, but it is NOT behavior-preserving as the brief requires —
   flag for an explicit accept/reject decision.

## Low / pre-existing (not introduced by this refactor)
3. `EffectiveLayerType`/`ActiveLayerType` distinguishes Props from Grass by layer NAME
   (`.Contains("prop")`, State.cs:85) while dispatch resolves by TYPE (`InstanceScatterLayer` cast).
   A prop layer not named "*prop*" → classified Grass → DensityDispatch → ResolveDensityLayer
   returns null → silent no-op. Pre-existing (original used the same `ActiveLayerType`); the refactor
   neither fixes nor worsens it.

## Verified correct (no regression)
- Falloff LUT bound before EVERY Dispatch in all GPU tools (HeightSmooth:36, HeightFlatten:51,
  RaiseLower:75, SplatDispatch:32, DensityDispatch:45). Instance tools use no kernel. #1 risk clear.
- Splat channel resolution preserved (same `ActiveLayerType(out channel)` source, same `<0 → 0`
  + clamp).
- Density writeback exact: `_DensityMode` set per tool (default tool = mode 0), `RequestAsync`
  guarded by `activeDensityLayer != null` which `GetOrCreateDensityRT` sets to the resolved layer.
- Instance undo: `InstanceUndo.PushOnce` replicates `CanUndoRecords`-guarded per-layer-per-stroke
  push for all 3 instance tools.
- Shader paint mode 0 (add + renormalize) is byte-identical; erase is a prepended early-return.
- Density default (DensityPaintTool, mode 0) and Height default (HeightRaiseTool, sign +1) match the
  original always-mode-0 / always-sign-+1 dispatchers.
- Biome branch unchanged (same `biomeStamp.Stamp(...)` args).
- EmitSingle: try/finally restores `DensityPerStamp`; `surfaceSampler:null` skips slope-rejection so
  the single instance is never culled; radius 0.01 keeps it at the cursor. Reliable single placement.
- Event leak: subscribe/unsubscribe use the SAME captured delegate instances; DetachFromPanelEvent
  fires on inspector rebuild (dock built once per CreateInspectorGUI). No leak.
- Deleted WorldPainterLodPreviewPanel has zero remaining references.

## Score: 7/10
Two real regressions (one Critical splat-mode bleak, one High precedence change), both narrow but
reproducible. The mechanical refactor (LUT binding, channel resolution, undo, writeback, shader math)
is faithfully preserved.
