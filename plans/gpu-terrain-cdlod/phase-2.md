# Phase 2 — Terrain Shading

**Effort:** M · **Blocks:** nothing (visual quality) · **Blocked by:** Phase 1 (patch shader), Phase 0 (splat data)

## Goal

Make the Phase 1 surface look like real terrain: 4-layer splat blend from a texture array,
heightmap-derived normals (no stored normal map needed), and clean URP lit integration. Parallel-safe
with Phase 4 after the milestone gate (disjoint files).

## Feasibility

- **Reuse check:** extends the Phase 1 `TerrainPatch.shader` and `TerrainVtf.hlsl` (NEW shading body).
  Splat byte layout comes from Phase 0's `TerrainTileAsset` (documented RGBA→layer SSOT). No new C#
  engine — shading is shader-side plus a small material-config holder.
- **Complexity:** moderate. Mobile sampler budget (4 layers + normal derivation) is the main constraint.

## File ownership (new files)

```
Assets/GpuTerrain/
  Runtime/
    TerrainLayerSet.cs              (ScriptableObject: up to 4 layer textures → builds Texture2DArray; tiling/color) ≤200
    TerrainShadingConfig.cs         (named consts: MAX_SPLAT_LAYERS=4, normal-derivation epsilon, tiling defaults)   ≤120
  Shaders/
    TerrainSplat.hlsl               (4-layer texture-array sample + weight-normalize; SSOT channel→layer mapping)    ≤150
    TerrainNormals.hlsl             (height-derivative normal from VTF neighbour samples; SSOT with Phase 0 mapping)  ≤120
    TerrainPatch.shader (EDIT)      (replace Phase 1 simple-lit body with splat + derived-normal lit)                 owned-by-1, extended-here
  Tests/Editor/
    TerrainLayerSetTests.cs         (texture-array build: 4 slices, correct format, layer cap enforced)
    TerrainSplatWeightTests.cs      (host-replicated weight-normalize math: weights sum→1, dominant layer wins)
```

> **File-ownership note:** `TerrainPatch.shader` is created in Phase 1 and EXTENDED here. To avoid a
> two-phase edit collision, Phase 1 leaves a clearly marked `// PHASE 2: shading body` region; Phase 2
> replaces only that region. Sequence Phase 2 after Phase 1 merges.

## Tasks

1. **`TerrainShadingConfig`** — named constants: `MAX_SPLAT_LAYERS = 4`, normal-derivation sample
   epsilon, default tiling. No magic numbers in shaders — push via material/config.
   - *Verify:* constants referenced by both `TerrainLayerSet` and the shader (no inline literals).
2. **`TerrainLayerSet`** — `ScriptableObject` building a `Texture2DArray` (≤4 slices) from up to 4
   layer albedo textures; per-layer tiling + tint. Enforce the 4-layer cap (warn + truncate, surfaced).
   - *Verify:* `TerrainLayerSetTests` — array has exactly min(N,4) slices; >4 layers truncates with a logged warning; format mobile-compatible.
3. **`TerrainSplat.hlsl`** — sample the 4 splat weights from Phase 0's splat texture (RGBA→layer per
   the `TerrainTileAsset` SSOT), normalize, blend the 4 array slices. Single texture-array sample path.
   - *Verify:* `TerrainSplatWeightTests` (host-replicated) — normalized weights sum to 1; a 100%-channel input picks exactly that layer.
4. **`TerrainNormals.hlsl`** — derive the surface normal from VTF height neighbour samples (central
   difference), using the SAME height decode as Phase 0/Phase 1 (no separately stored normal map →
   no drift). Account for tile size → world-space gradient.
   - *Verify:* on a known ramp tile, derived normal matches the analytic slope within epsilon (host-replicated math test).
5. **`TerrainPatch.shader` shading body** — replace the Phase 1 placeholder lit region: splat-blended
   albedo + derived normal → URP lit output. Keep buffer/texture binds as material properties (RenderGraph MPB lesson).
   - *Verify:* terrain shows distinct layers with smooth blends; lighting responds to the directional light; no per-frame allocations.

## Risk Assessment

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| 4-layer array + normal derivation exceeds mobile fragment sampler/ALU budget | 3 | 4 | 12 | Single array sample (not 4 separate textures); central-difference normal reuses already-fetched VTF; profile on device; cap is a named const. |
| Derived-normal seams at tile boundaries | 3 | 3 | 9 | Use Phase 0's 1-texel skirt/overlap so neighbour samples exist across the edge; SSOT decode. |
| Splat channel→layer mismatch with Phase 0 byte layout | 2 | 4 | 8 | Cite `TerrainTileAsset` RGBA mapping as SSOT in `TerrainSplat.hlsl`; host-replicated weight test. |
| Two-phase edit collision on TerrainPatch.shader | 2 | 2 | 4 | Phase 1 marks the shading region; Phase 2 sequenced after Phase 1 merge; single-owner region edit. |

**Score ≥ 15:** none. Highest is the mobile budget risk (12) — mitigated by single-sample design +
on-device profiling.

## Timeline

| Task | Effort | Notes |
|---|---|---|
| TerrainShadingConfig | S | constants SSOT |
| TerrainLayerSet + tests | M | texture-array build |
| TerrainSplat.hlsl + tests | M | weight blend |
| TerrainNormals.hlsl | S | central-difference |
| TerrainPatch.shader body | M | URP lit integration |
| **Total** | **M** | Critical path: LayerSet → Splat → shader body |

## Test strategy

EditMode NUnit, host-replicated shader math (the morph-test pattern from Phase 1):
- `TerrainLayerSetTests` — array slice count, cap enforcement, format.
- `TerrainSplatWeightTests` — normalized weights, dominant-layer selection.
- `TerrainNormalsTests` (host-replicated) — derived normal vs analytic slope on a ramp.
- Visual verification (Game view) — distinct layers, smooth blends, lit response — is a manual gate.
</content>
