# Implementation Plan — Terrain 2-Tap Rotated Stochastic Anti-Tiling

**Source design:** `plans/reports/terrain-stochastic-tiling-brainstorm.md` (approved 2026-06-16)
**Mode:** standard · single-file shader change · no C#/authoring change
**Cook handoff:** `/t1k:cook plans/terrain-stochastic-tiling-plan.md`

## Goal
Break the visible world-grid repeat in WorldPainter terrain albedo using per-cell hashed
**rotation + offset** with a 2-tap GRAD-sampled boundary blend, gated behind a shader keyword
(zero cost when off), global across all palette layers.

## Files in scope
| File | Change |
|---|---|
| `Assets/WorldPainter/Shaders/TerrainPalette.hlsl` | Add stochastic sampler fn; route the layer sample through it under `#ifdef _TERRAIN_STOCHASTIC` |
| `Assets/WorldPainter/Shaders/TerrainPatch.shader` | Add `#pragma multi_compile _ _TERRAIN_STOCHASTIC` to the forward pass; expose a `[Toggle(_TERRAIN_STOCHASTIC)]` material property |

**Out of scope (deferred):** per-layer opt-in flag, distance-fade, Heitz–Neyret 3-tap+LUT.
No `WorldMapAsset` / binder / C# change.

---

> **Implementation note (review correction):** Phase 1 originally specced IQ
> Technique 1 (hash-cell nearest-neighbour blend). Review proved that is not C0
> at 2 taps in 2D. Implemented instead as IQ Technique 3 (smooth-index 2-tap
> cross-fade) — genuinely seamless at 2 taps. Status: **DONE, review APPROVE-WITH-NITS.**

## Phase 1 — Stochastic sampler in TerrainPalette.hlsl
**Verify:** shader compiles (`read_console` clean); keyword OFF path is byte-identical behaviour to today.

Add a self-contained helper above `BlendTerrainPalette`:

```hlsl
// 2x2 hash -> [0,1)^2 ; cheap, no texture dependency
float2 Hash22(float2 p);

// Rotate uv about a pivot by angle a (cos/sin from hashed value)
float2 RotateUV(float2 uv, float2 pivot, float s, float c);

// 2-tap rotated stochastic sample of palette layer `i`.
// grads = ddx/ddy of the CONTINUOUS base UV (computed once by caller).
float4 SampleLayerStochastic(int i, float2 baseUV, float2 dUVdx, float2 dUVdy)
{
    // 1. cell coords from baseUV (scale chosen so a cell ~ a few texture repeats)
    // 2. nearest 2 cells along dominant fractional axis
    // 3. per cell: hash -> angle (+ optional offset); rotate baseUV about cell centre
    // 4. SAMPLE_TEXTURE2D_ARRAY_GRAD(... layerUV, i, rotated dUVdx, rotated dUVdy) x2
    // 5. blend by smoothstep(frac) boundary weight -> return
}
```

Routing in `BlendTerrainPalette` (lines ~91-105), inside the active-layer loop:

```hlsl
float2 layerUV = PaletteUV(i, worldXZ);
#ifdef _TERRAIN_STOCHASTIC
    float2 dUVdx = ddx(layerUV);
    float2 dUVdy = ddy(layerUV);
    float4 c = SampleLayerStochastic(i, layerUV, dUVdx, dUVdy);
#else
    float4 c = SAMPLE_TEXTURE2D_ARRAY(_TerrainPaletteArray, sampler_TerrainPaletteArray, layerUV, i);
#endif
```

**Notes / invariants:**
- Gradients come from the **continuous** `layerUV` (pre-rotation) — required so the per-cell
  rotation/offset jump does not spike auto-mip derivatives (→ blurred seams). One ddx/dy pair, reused.
- `ddx/ddy` are valid in the fragment loop (uniform control flow over `i`); the `[loop]` + `continue`
  on zero-weight is fine because derivatives are computed from `layerUV` which is defined every iteration
  — **confirm no `ddx` sits behind the `if (w<=0) continue;` early-out** (move the grad calc after the continue, before the sample).
- Cell scale is a named `static const` (e.g. `STOCHASTIC_CELL_REPEATS`), not a magic literal.

### Risk Assessment
| Risk | L (1-5) | I (1-5) | Score | Mitigation |
|------|--------|--------|-------|------------|
| `ddx` behind dynamic `continue` → undefined gradient / warning | 3 | 3 | 9 | Compute grads after the zero-weight early-out, before the sample |
| Visible cell seams (blend too narrow) | 2 | 3 | 6 | Tune smoothstep band; A/B screenshot in Phase 3 |
| Mip blur at cell borders (wrong grad) | 2 | 4 | 8 | Use continuous-UV grads (designed in); verify on a high-freq texture |

---

## Phase 2 — Keyword + material toggle in TerrainPatch.shader
**Verify:** material shows a "Stochastic Tiling" toggle; toggling flips the `_TERRAIN_STOCHASTIC` keyword; `read_console` clean.

1. Add to the `UniversalForward` pass HLSLPROGRAM (near the other `multi_compile` block, ~line 47-57):
   ```
   #pragma multi_compile _ _TERRAIN_STOCHASTIC
   ```
   ShadowCaster pass is **not** touched (albedo-only feature).
2. Add a Properties entry so it's toggleable per-material without code:
   ```
   [Toggle(_TERRAIN_STOCHASTIC)] _StochasticTiling ("Stochastic Tiling (anti-repeat)", Float) = 0
   ```

**Note:** keyword default OFF → existing materials/scenes render unchanged until explicitly enabled.

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Keyword not stripped → variant bloat | 2 | 2 | 4 | Single `multi_compile _` toggle = 2 variants on one pass; negligible |
| Toggle property name collides with binder-set uniform | 1 | 3 | 3 | `_StochasticTiling` is new; grep confirms no collision |

---

## Phase 3 — Validation (visual A/B + tap budget)
**Verify:** documented A/B screenshots + `rendering_stats` delta; sign-off.

`execute_code` is broken in this env (project memory) → **no runtime shader unit test.** Validate by:
1. Same camera, **keyword OFF** screenshot → baseline (grid repeat visible).
2. **Keyword ON** screenshot → repeat broken, no obvious cell seams, no mip blur at borders.
3. `rendering_stats` before/after → confirm added cost matches the 2-tap/active-layer budget and stays acceptable for the mobile target.
4. Check a grazing-angle / far view (where tiling is worst) and a high-contrast texture (where contrast-wash would show).

### Risk Assessment
| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Contrast wash objectionable on some texture | 2 | 3 | 6 | Documented tradeoff; escalation path = 3-tap+LUT follow-up |
| Mobile cost higher than expected | 2 | 4 | 8 | Keyword is the safety valve; cap via quality tier; distance-fade follow-up |

---

## Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Stochastic sampler | S (~1d) | Core HLSL; gradient-ordering is the one trap |
| Phase 2: Keyword + toggle | S (<0.5d) | Mechanical pragma + property |
| Phase 3: Validation | S (~0.5d) | Visual A/B + rendering_stats, needs editor |
| Total | S (~2d) | Critical path: P1 → P2 → P3 (strictly sequential, one file then editor) |

## Success criteria
- Keyword OFF = pixel-identical to current behaviour (no regression).
- Keyword ON = no visible world-grid repeat at mid/far distance, no hard cell seams, correct mips.
- Added GPU cost ≤ 2 albedo taps per active layer; shader compiles with zero console errors.
- No C#/authoring/`WorldMapAsset` change; ShadowCaster pass untouched.
