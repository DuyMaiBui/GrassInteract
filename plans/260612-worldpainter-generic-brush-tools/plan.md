# WorldPainter — Generic Brush-Tool Interface + Contextual Tool Palette

**Date:** 2026-06-12
**Mode:** /t1k:cook interactive
**Branch:** feat/worldpainter-ssot-consolidation

## Goal

Replace the hardcoded `BindAndDispatch` if/else chain with a generic `IBrushTool`
interface + registry, and surface a **contextual tool palette** in the brush dock
that shows the correct tools for the active layer type:

| Active layer (LayerType) | Tools |
|---|---|
| Height | Raise, Lower, Smooth, Flatten |
| Splat | Paint, Erase |
| Grass (density scatter) | Paint, Erase, Smooth |
| Props (instance scatter) | Place, Erase, Single |
| Biome | (none — BiomeStamp owns its own composite UI; palette hidden) |

## Locked requirements (user-confirmed)

1. **Expected output:** Selecting a layer shows that layer's tool set in the brush dock;
   each tool actually drives the matching GPU kernel / CPU emitter. Currently-dead
   kernels (Smooth, Flatten, density Erase/Smooth) and a new splat Erase are wired live.
2. **Acceptance:** Each tool paints correctly; switching layers swaps the palette;
   compiles clean; existing height-raise / splat-paint / density-paint / prop-place /
   biome strokes still work; no console errors.
3. **Scope boundary:** Brush dispatch only. `WorldPainterBiomeStamp` is NOT refactored
   (it composites multiple channels — separate path, left intact).
4. **Constraints:** Project C# conventions (`this.` prefix, camelCase private fields,
   `#nullable enable`, ≤200 lines/file, namespace `WorldPainter.Editor`). No new
   third-party deps. SSOT — one tool catalog, one active-tool selection.
5. **Touchpoints:** `BindAndDispatch` (Kernels.cs), brush dock UI, `WorldPainterState`
   (active-tool selection), `TerrainBrush.compute` (splat erase), prop emitter (single).

## Key facts discovered (drive the design)

- **Height == `PaintLayerKind.None`** by convention; there is no `Height` enum value.
  The reliable layer signal is `WorldPainterState.ActiveLayerType(painter)` (derived
  from `ActiveLayerIndex`, which the stack sets). The palette + dispatch key off this.
- Kernel-name constants `KERNEL_SMOOTH` / `KERNEL_FLATTEN` already exist.
- `PaintDensity` already supports modes 0/1/2 (paint/erase/smooth) — just wire `_DensityMode`.
- `PaintSplat` has **no erase** — add a `_SplatMode` uniform (0 add / 1 subtract).
- `Flatten` needs `_FlattenTarget` (normalized): capture world-Y under cursor at
  mouse-down, normalize per-tile `(y - tile.minHeight)/(tile.maxHeight - tile.minHeight)`.

## Architecture

```
IBrushTool { string Id; string Label; LayerType LayerType; void Apply(in BrushToolContext); }
BrushToolContext (readonly struct): Tool(back-ref), Painter, WorldPos, Tile,
    HeightRT, SplatRT, Compute, Groups  (common brush uniforms already set by caller)
BrushToolRegistry: static catalog LayerType -> IBrushTool[]; ToolsFor(kind);
    ResolveActiveTool(kind, activeId) -> matching tool or kind default (tools[0]).
BrushToolTargets: ResolveDensityLayer(painter), ResolveInstanceLayer(painter)
```

Active-tool selection in `WorldPainterState`: `ActiveBrushToolId` (string) +
`SetActiveBrushTool(id)` + `ActiveBrushToolChanged` event. Resolution maps the stored
id onto the current kind (falls back to kind default), so switching layers needs no reset.

`BindAndDispatch`:
1. Set common brush uniforms (unchanged).
2. If `activeType == Biome` → `biomeStamp.Stamp(...)` (unchanged).
3. Else resolve `kind = ActiveLayerType(painter)`, `tool = Registry.ResolveActiveTool(kind, ActiveBrushToolId)`,
   build context, `tool.Apply(ctx)`.

## File change set

**New (`Editor/Brush/Tools/`):**
- `IBrushTool.cs`, `BrushToolContext.cs`, `BrushToolRegistry.cs`, `BrushToolTargets.cs`
- `HeightBrushTools.cs` (Raise/Lower/Smooth/Flatten)
- `SplatBrushTools.cs` (Paint/Erase)
- `DensityBrushTools.cs` (Paint/Erase/Smooth)
- `InstanceBrushTools.cs` (Place/Erase/Single)

**Modified:**
- `WorldPainterState.cs` — active-tool state + event + reset.
- `WorldPainterSculptTool.Kernels.cs` — rewrite `BindAndDispatch`; remove the 3 private
  `Dispatch*Kernel` methods (logic moves into tools).
- `WorldPainterSculptTool.cs` — flatten-target stroke fields.
- `WorldPainterSculptTool.Stroke.cs` — capture flatten target on mouse-down; skip drag
  stamps for the Single instance tool.
- `WorldPainterBrushDock.cs` — replace `BuildModeToggle` with `BuildToolPalette` (contextual,
  rebuilds on `ActiveLayerChanged` + `ActiveBrushToolChanged`).
- `WorldPainterPropStampEmitter.cs` — add single-instance emit.
- `Shaders/TerrainBrush.compute` — `_SplatMode` uniform + erase branch in `PaintSplat`.

## Verify
1. Compile clean (Unity refresh + read_console: 0 errors).
2. Code review (t1k-code-reviewer): root behaviors preserved, no regression, contract intact.
3. Tester (t1k-tester): run WorldPainter EditMode tests (TerrainBrushMathTests / DensityBrushMathTests).
4. Manual: each palette switches per layer; tools dispatch the right kernel.

## Out of scope / notes
- BiomeStamp untouched (per decision).
- Tool memory across layer switches is "best-effort" (resolution falls back to kind
  default; stored id only changes on explicit click) — acceptable for v1.
- `Single` instance placement = one instance at cursor on click; drag does not repeat.
