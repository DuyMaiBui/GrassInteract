# Phase 9 — Fresh demo scene via Unity MCP + validation

**Effort:** M · **Wave:** F (terminal) · **Depends on:** P1–P8 · **Blocks:** —

## Goal

Stand up a **fresh demo scene via Unity MCP** (NO migration tool — authored data is disposable per design) and validate that **WorldPainter alone** renders terrain + grass + props from a `WorldMapAsset`. This is the end-to-end acceptance gate.

## File-ownership group (terminal, single subagent — uses Unity MCP, not file edits)

**G9.1 — Build + validate (MCP-driven, no .cs ownership conflicts)**
- Build a new scene `Assets/WorldPainter/Demo/WorldPainterDemo.unity` via `manage_scene` (include Camera + Directional Light per MCP scene-setup convention).
- Use the **P4 factory** ("Create World Map") to create the `WorldMapAsset` + `Tile_0_0` + `WorldPainter` in the new scene — proving the no-scene-switch path.
- Grow a 2×2 (and one negative-coord) tile set via the **P4 ghost-quad overlay**.
- Add a splat layer, a meadow/density layer, and a prop layer via the **P5 palette**; allocate channels.
- Paint height/splat/density across a tile seam via the **P6 world-space brush**; verify seam continuity.
- Scatter + transform-edit a few props via **P7**.
- (Optional) run a **P8 bake** and confirm per-tile assets emit.

## Validation steps (MCP)

1. **Compile gate:** `read_console` clean (zero errors) before entering Play.
2. **Render validation:** enter Play (`manage_editor` play) → `rendering_stats` / `read_console` → confirm terrain mesh + grass blades + prop instances are all submitted by the single `WorldPainter` (no `GpuTerrainRenderer`/`ScatterField`/`GpuTerrainScatterGround` in scene — grep + scene-hierarchy check).
3. **Visual check:** capture SceneView/GameView via MCP; route any image analysis through human-mcp per `image-analysis-routing.md`.
4. **Full EditMode suite:** `run_tests` → zero failures (design success-gate: "project compiles; WorldPainter renders terrain + grass + props alone").
5. **MCP discipline:** MCP timeout ≠ disconnect — diagnose (process/workers) before escalating; **never kill/restart the editor**.

## Verification

- All 8 design success criteria re-checked against the live scene (criteria 1–8 in the design report).
- `read_console` clean + `run_tests` zero failures in one pass.

## Success criteria (maps to design success criterion 8 + overall acceptance)

1. Fresh demo scene built via Unity MCP renders correctly.
2. WorldPainter is the **sole** renderer (terrain + grass + props) — duplicates confirmed absent.
3. Create-in-current-scene, ghost-quad grow (incl. negative coord), tile-agnostic seam-safe paint, 3-section palette with channel alloc, dual-mode props, and per-tile bake/stream all demonstrated end-to-end.
4. Project compiles; entire EditMode suite green.
