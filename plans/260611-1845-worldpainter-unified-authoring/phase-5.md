# Phase 5 — Biome Brush: `BiomePreset` Schema + Card Palette + Per-Channel Contribution

**Effort:** M · **Blocked by:** P2, P3, P4 · **Blocks:** P6

## Goal

The headline feature (design §4.3): a `BiomePreset` ScriptableObject that bundles `{height-delta rule, splat layer+weight, grass layer+density, prop palette+scatter rule}`. One stroke paints all channels via the payload-agnostic spacing-stamp loop; per-stroke toggles mute any channel. Big thumbnail card palette. Trivial only because the stroke loop is already payload-agnostic after P1–P4.

## File ownership (concrete paths)

### New
| Path | Responsibility | ≤200 lines |
|---|---|---|
| `Assets/GpuTerrain/Runtime/BiomePreset.cs` | ScriptableObject: height-delta rule, splat layer+weight, grass layer+density, prop palette+scatter rule (Tier-C disk asset) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterBiomePaletteView.cs` | big thumbnail card palette (FOREST/CLIFF/MEADOW/PATH/+) | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterBiomeContributionToggles.cs` | per-stroke ⛰/🎨/🌿/🌳 channel toggles + hover contribution readout | yes |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterBiomeStamp.cs` | one stamp fans out to height/splat/density/prop emitters with per-channel mute | yes |

### Modified
| Path | Change |
|---|---|
| `Assets/GpuTerrain/Runtime/WorldPainter.Data.cs` | `biomes : List<BiomePreset ref>` (Tier-A refs to Tier-C) |
| `Assets/GpuTerrain/Editor/WorldPainter/WorldPainterLayerStackView.cs` | Biome layer type (mode-color violet) |
| `Assets/GpuTerrain/Editor/TerrainPaintTargetResolver.cs` | biome multi-tile + residency-pin (touch may span tiles × payloads) |

### Reuse unchanged (cite)
Height/splat kernels + density encoder + prop stamp emitter from P1–P4 (the stamp fans to these), `BrushMask.hlsl`, `TerrainStreamingManager`/`TerrainResidencyRing` (stream-out pin), `AssetPreview` for card thumbnails.

## Tasks (each with verify-check)

1. **`BiomePreset` schema** — SO with the four channel rules (design §4.3); Tier-C disk asset under `Assets/Worlds/<name>/Biomes/`. → verify: create a `Forest` preset; serializes; AssetPreview thumbnail renders.
2. **Biome stamp fan-out** — `WorldPainterBiomeStamp`: one spacing-stamp invokes height-delta + splat-write + density-write + prop-emit in one dispatch group (design §5.4). → verify: a single biome stroke modifies all enabled channels in one stroke; per-tile undo captures all four.
3. **Card palette** — big thumbnail cards (Scatter-Studio chrome) with `+` to add; hover shows channel contribution. → verify: clicking a card sets the active biome; hover tooltip lists contributing channels.
4. **Per-channel toggles** — ⛰/🎨/🌿/🌳 mute per stroke (uniform-driven, no per-payload code, design §5.5). → verify: muting 🌳 yields a stroke that paints height/splat/grass but no props.
5. **Multi-tile + stream pin** — biome stroke resolves across tiles via `TerrainPaintTargetResolver` and pins touched tiles against stream-out for the stroke (design §6). → verify: cross-seam biome stroke is consistent; no stream-out mid-stroke corruption.
6. **Unified biome undo** — one `Undo` group per biome stroke spans all four channel snapshots. → verify: Ctrl+Z reverts the whole biome stroke atomically.

## Risk Assessment (L×I)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Tile stream-out mid biome stroke corrupts a multi-channel multi-tile edit | 3 | 5 | **15** | Pin touched tiles against `TerrainStreamingManager`/`TerrainResidencyRing` for stroke duration; resolver takes residency set |
| Four-channel fan-out per stamp blows the dispatch budget | 3 | 4 | 12 | Per-frame dispatch cap + queue; spacing-stamping bounds stamp count; channel toggles reduce live payloads |
| Biome undo captures channels inconsistently (partial revert) | 3 | 4 | 12 | One `Undo` group spans all four snapshots; atomic-revert test |

## Test plan

- `run_tests`: full suite green (no SSOT change); brush-math + density-math + splat-weight contracts hold.
- New: biome stamp fan-out (N channels per stamp), per-channel mute mask, atomic biome undo.
- Manual: paint Forest across the 2-tile seam with props muted; one-Ctrl+Z revert.
