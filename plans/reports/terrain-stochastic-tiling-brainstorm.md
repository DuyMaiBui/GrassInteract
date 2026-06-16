# Terrain Anti-Tiling — 2-Tap Rotated Stochastic (Brainstorm / Design)

**Date:** 2026-06-16
**Status:** Design approved — ready for `/t1k:plan`
**Scope:** Single-file shader change. No authoring/C# change.

## Problem
WorldPainter terrain albedo shows a visible world-grid repeat at mid/far distance.
Root cause: `TerrainPalette.hlsl` samples each layer at `PaletteUV(i) = (worldXZ+offset)/tileSize`
— a plain periodic world-space repeat with nothing breaking the periodicity
(`Assets/WorldPainter/Shaders/TerrainPalette.hlsl:100-102`).

Cost surface is small: normals are heightmap-derived, smoothness/metallic are constants,
so **only albedo is textured** — the entire fix is the per-layer albedo sample.

## Approaches evaluated
| # | Technique | Taps/layer | Quality | Mobile fit | Verdict |
|---|---|---|---|---|---|
| 1 | Pure per-cell rotation, no blend (IQ T1 minus blend) | 1 | seams at cell borders | cheapest | rejected (visible seams) |
| 2 | **2-tap rotated stochastic (IQ T1 capped at 2)** | **2** | **seamless, slight contrast wash** | **good** | **CHOSEN** |
| 3 | Heitz–Neyret histogram-preserving | 3 + Tinv LUT | best, no wash | heavier | deferred (overkill for mobile) |

References reviewed: IQ "Texture Repetition" (https://iquilezles.org/articles/texturerepetition/),
Heitz–Neyret HPG 2018 (https://eheitzresearch.wordpress.com/722-2/).

## Chosen architecture — 2-Tap Rotated Stochastic (IQ Technique 3, smooth-index)
> **Correction (implementation review):** the first cut used IQ Technique **1**
> (discrete hash-cell nearest-neighbour blend). Code review proved that is **not
> C0 in 2D** at 2 taps — `max(fc.x,fc.y)` couples the axes, so a neighbour flip on
> one axis while the other carries weight leaves a seam along every cell edge
> (needs 4 taps to be seamless). Replaced with IQ Technique **3**, which is
> genuinely seamless at 2 taps.

Per active layer, replace the single `SAMPLE_TEXTURE2D_ARRAY` with:
1. `baseUV = PaletteUV(i, worldXZ)` (unchanged).
2. `dUVdx = ddx(baseUV); dUVdy = ddy(baseUV)` — computed once, from the **continuous** base UV,
   **before** the zero-weight `continue` (uniform control flow; the GRAD sample after is safe on explicit grads).
3. `k = StochasticIndexField(baseUV * FREQ)` — a procedural C1 value-noise scalar; `l = k*VARIANTS; ia=floor(l); ib=ia+1; f=frac(l)`.
4. Sample two variants `ca=variant(ia)`, `cb=variant(ib)`; each variant applies a hash-derived **rotation + offset**,
   sampled via `SAMPLE_TEXTURE2D_ARRAY_GRAD` with the rotated `dUVdx/dy` (correct mips, no blur seam).
5. `w = smoothstep(0.2,0.8, f - CONTRAST*lumaDelta); lerp(ca,cb,w)`. C0 because at each integer-`l` handoff
   the incoming variant index equals the outgoing one and the smoothstep plateaus pin `w` to 0/1 there.
   **Invariant:** `CONTRAST < 0.2` (smoothstep half-band) or the handoff plateau breaks → seams return.

### Decisions
- **2 taps not 4:** blend along dominant boundary axis only — ~95% of seam-hiding at half the far-corner cost.
- **`SAMPLE_GRAD` mandatory:** per-cell rotation/offset jump would spike auto-mip derivatives → blurred seams.
  Gradients of the *continuous* base UV avoid this (one ddx/dy pair, reused for both taps).
- **Shader keyword** `#pragma multi_compile _ _TERRAIN_STOCHASTIC` → compiles out entirely when off (**zero cost**),
  switchable per-material / quality tier without re-authoring.
- **Global (all layers):** no per-layer branch (avoids mobile divergence), no `WorldMapAsset` authoring change.
- **No LUT, no new textures:** procedural hash. Accepted tradeoff = mild contrast wash in blend bands
  (vs. the Heitz–Neyret 3-tap LUT version).
- **No distance-fade in v1** (YAGNI) — revisit only if profiling shows tap cost hurts.

## Cost
Typical pixel = 1–3 active layers → 2–6 albedo taps worst case, bounded by the existing palette loop.
Dominant added cost on tiler GPUs = `SAMPLE_GRAD`; keyword toggle is the safety valve.

## Risks
- Contrast softening in blend bands (no histogram LUT) — fine for natural ground, visible on high-contrast textures.
- `SAMPLE_GRAD` ALU on mobile tilers — mitigated by the keyword + bounded tap budget.

## Validation
Shader logic not unit-testable here (`execute_code` broken in this env — project memory).
→ A/B screenshots (keyword off vs on, identical camera) + `rendering_stats` to confirm tap/frame delta stays in budget.

## Follow-ups (deferred, not in v1)
- Per-layer opt-in flag (`_TerrainPaletteStochastic[i]`) to exempt structured textures (paths/roads).
- Distance-fade stochastic→plain tiling beyond a radius for far-field savings.
- Upgrade path to Heitz–Neyret 3-tap+LUT if contrast wash proves objectionable.
