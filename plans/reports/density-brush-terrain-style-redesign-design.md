# Density Brush — Terrain-Style Redesign (Design)

Date: 2026-06-10 · Status: design, pending approval · Skill: t1k-brainstorm

## Problem

The density paint preview shows a **white plane** over the terrain and the paint feel is nothing like Unity's Terrain brush.

Root causes (evidence):
- `ScatterDensityOverlay.cs:204-239` — the heatmap overlay draws a full-field textured quad via `Graphics.DrawMeshNow` every frame. When `Hidden/GrassInteract/DensityHeatmap` is missing it falls back to `Sprites/Default`, which samples `_MainTex` (never bound — the code binds `_DensityTex`). Unbound `_MainTex` = **solid white** → the white plane.
- `DensityPaintTool.cs:172-240` — painting is a CPU per-pixel loop (`SetPixels`+`Apply` every `MouseDrag`). Slow on big brushes; fast drags leave **gaps** (only stamps the current hit, no interpolation).
- `ScatterGizmos.BrushDisc/FalloffRing` — preview is thin wire discs, not a Terrain-style projected brush-mask cursor.

## Goal (user-confirmed)

1. **No white plane.** Bare terrain/plane visible; only a brush cursor on it.
2. **Brush cursor = normal-aligned textured disc/decal** — a quad tilted to `hit.normal`, textured with the active brush-mask stamp (reads like the Terrain brush).
3. **GPU brush splatting** for the actual write (no CPU pixel loops).
4. **Keep R8 PNG** as persistence SSOT — GPU RenderTexture is read back to the PNG on stroke-end.

## Design

### A. Kill the white plane
- Remove the always-on heatmap quad from the paint path. The `Sprites/Default` white-fallback is deleted outright (errors-over-silent-fallback: never render an unbound-texture quad).
- Heatmap becomes strictly opt-in (`OverlayVisible`, default off) AND only renders when the dedicated `DensityHeatmap` shader resolves; otherwise it logs once and draws nothing. The overlay binds the live paint RenderTexture so it stays correct during a stroke.

### B. Brush preview — `ScatterBrushPreview` (new)
- On the raycast hit, draw a single quad oriented to `hit.normal` via `Quaternion.LookRotation(tangent, normal)`, sized to brush radius, textured with the resolved brush-mask stamp (or a soft procedural falloff disc when stamp = None).
- Tinted by mode color (`ScatterGizmos.BrushColor` / `EraseColor`), alpha = falloff.
- Drawn with one cached unlit-transparent material + quad mesh via `Graphics.DrawMeshNow` inside the tool's repaint (no per-frame alloc, mirrors the overlay's resource pattern).
- Replaces the `BrushDisc`/`FalloffRing` wire discs in `DensityPaintTool.OnToolGUI`. (Wire helpers kept for the instance tool.)

### C. GPU splat pipeline — `DensityPaintGPU` (new) + `Hidden/GrassInteract/DensityPaintBrush.shader` (new)
- **BeginStroke:** allocate a linear `RenderTexture` (R8/RFloat, sRGB off) sized to the density map; blit current `densityMap` → RT (seed).
- **PaintAt → GPU splat:** render the brush-mask quad in UV space into the RT via the paint material. Blend per mode:
  - Paint = additive (clamped), Erase = subtractive, Smooth = separable blur pass toward neighbor average.
  - Strength = `opacity * flow * stampAlpha`. No CPU pixel loop, no per-event readback.
- **Continuous strokes:** track `lastPaintUv`; stamp along the `lastPaintUv → curUv` segment at fixed spacing (`radius * spacingFactor`) so fast drags are gap-free (Terrain behavior).
- **Live scatter feedback:** on the existing `ScatterRebuildScheduler` 0.15s debounce tick, `AsyncGPUReadback` RT → `Color[]` → assign to `densityMap` → `MarkDirty` rebuild. Readback is per-tick (~6–7/s), never per paint event.
- **EndStroke:** final readback → existing `DensityMapFactory.PersistPixels` (R8 PNG). One persistence path, unchanged contract.

### SSOT reused (no new mapping/state drift)
`GrassFieldSpace` (world↔UV), `ScatterAuthoringState` (size/opacity/falloff/flow/mode/stamp), `ScatterRebuildScheduler` (0.15s rebuild), stamp resolution (`ResolveStamp`), `DensityMapFactory.PersistPixels` (PNG).

## Files

| Action | File |
|---|---|
| New | `Editor/ScatterStudio/ScatterBrushPreview.cs` — normal-aligned brush decal |
| New | `Editor/ScatterStudio/DensityPaintGPU.cs` — RT splat + readback |
| New | `Editor/Resources/DensityPaintBrush.shader` — brush-mask blend (Paint/Erase/Smooth) |
| Modify | `Editor/DensityPaintTool.cs` — GPU PaintAt + stroke interpolation; swap wire-disc preview for decal |
| Modify | `Editor/ScatterDensityOverlay.cs` — delete white fallback; bind RT; strict shader gate |
| Modify | `Editor/DensityMapFactory.cs` — RT→Color[] readback helper (reused by tick + EndStroke) |

## Evaluation

- **Reusability:** `DensityPaintGPU` + brush shader are layer-agnostic; `ScatterBrushPreview` reusable by any surface brush.
- **Maintainability:** removes the CPU per-pixel kernel; one paint shader is the blend SSOT; deletes the white-fallback foot-gun.
- **Testability:** EditMode-test the UV mapping + stroke-interpolation spacing math and an RT-readback golden (paint center → expected R8). GPU blend visually verified.

## Risks

- **Live scatter needs CPU pixels** (`DensityPlacement` samples `GetPixelBilinear`). Mitigated: readback only on the 0.15s tick, not per event.
- **Smooth-mode parity:** GPU blur ≠ exact CPU neighbor-average. Acceptable; tune kernel.
- **Linear/sRGB:** RT must be linear, no sRGB write, to match R8 density semantics.
- **R8 RT format support:** fall back to RFloat if R8 RT unsupported on the editor target (log once).

## Out of scope
Instance-placement tool, brush library UI, layer rail — unchanged.
