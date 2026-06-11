# Phase 4 — Collider + Scatter Bridge ⚠️ LOAD-BEARING

**Effort:** M · **Blocks:** nothing downstream · **Blocked by:** Phase 0 (height data); collider streaming integrates Phase 3

> ⚠️ **LOAD-BEARING:** the moment terrain becomes custom-heightmap, `TerrainSurfaceSampler` (Unity
> `TerrainData`) no longer grounds scatter — **existing grass and rocks will FLOAT** until
> `HeightmapSurfaceSampler` ships. This phase is a hard dependency for the existing grass demo to
> keep working on the new terrain. The sampler half can and should ship early (it only needs Phase 0).

## Goal

Two deliverables: (1) `HeightmapSurfaceSampler : ISurfaceSampler` so the existing grass/rock scatter
grounds on the custom terrain via the SAME seam it already uses; (2) heightfield proxy colliders for
near tiles (gameplay-ready physics), streamed with the Phase 3 residency ring.

## Feasibility

- **Reuse check:** the sampler implements the EXISTING `ISurfaceSampler` interface
  (`Assets/GrassInteract/Runtime/ISurfaceSampler.cs`) — `GrassScatter.Build` already accepts any
  implementation, so NO grass-side change is needed beyond passing the new sampler. `TrySample`
  delegates to Phase 0's `TerrainHeightSampleCpu` + splat read. Colliders use Unity's
  `TerrainCollider` (heightfield) — cheaper than `MeshCollider` cook; streaming/eviction mirrors
  `InstanceVisibilityColliderDriver` + `InstanceColliderPool` (near-tile only, amortised).
- **Complexity:** moderate. The sampler is small and high-value; the collider streaming is the bulk.

## File ownership (new files)

```
Assets/GpuTerrain/
  Runtime/
    HeightmapSurfaceSampler.cs      (ISurfaceSampler impl over Phase 0 height+splat; lives in GpuTerrain) ≤200
    TerrainColliderProvider.cs      (builds a TerrainCollider/heightfield proxy from a tile's R16 data)   ≤200
    TerrainColliderStreamer.cs      (MonoBehaviour: near-tile collider lifecycle, amortised cook; ring-driven) ≤200
    TerrainColliderConfig.cs        (named consts: COLLIDER_RING_RADIUS, MAX_COOKS_PER_FRAME)               ≤100
  Tests/Editor/
    HeightmapSurfaceSamplerTests.cs (TrySample height matches CPU ramp; off-grid → false; slope/normal correct)
    TerrainColliderProviderTests.cs (heightfield data matches tile R16; resolution mapping correct)
    TerrainColliderStreamerDiffTests.cs (near-ring membership; cook amortisation; evict releases collider)
```

> **`ISurfaceSampler` lives in `GrassInteract`** while `HeightmapSurfaceSampler` lives in `GpuTerrain`.
> Per the library-decoupling rule the seam is the interface: `GpuTerrain.asmdef` must reference
> `GrassInteract` ONLY to implement `ISurfaceSampler`. Confirm this is the single cross-reference and
> that GrassInteract does NOT reference GpuTerrain (one-way: grass depends on the interface, not the
> terrain renderer). If a cleaner split is wanted later, the interface could move to a shared seam
> asmdef — out of scope here; document the one-way reference.

## Tasks

1. **`HeightmapSurfaceSampler`** — implement `ISurfaceSampler.TrySample(worldX, worldZ, out SurfaceHit)`:
   resolve tile via Phase 0 `TerrainWorldGrid`, bilinear height via `TerrainHeightSampleCpu`, derive
   normal (central difference, SSOT with Phase 2), populate `SlopeDeg` + `SplatWeights` from Phase 0
   splat. Return `false` off-grid (matches the `TerrainSurfaceSampler` out-of-bounds contract).
   - *Verify:* `HeightmapSurfaceSamplerTests` — `TrySample` height matches `TerrainHeightSampleCpu` on a ramp; off-grid → false; slope/normal correct.
2. **Grass grounding integration** — wire the existing grass demo to construct `HeightmapSurfaceSampler`
   instead of `TerrainSurfaceSampler` when a GpuTerrain is the ground (a small demo/bootstrapping edit;
   no GrassScatter algorithm change).
   - *Verify:* the existing grass/rock demo grounds on the custom terrain — no floating/sinking (the brainstorm's Phase 4 success metric, on-device/editor visual).
3. **`TerrainColliderProvider`** — build a Unity `TerrainCollider` heightfield (or equivalent
   heightfield proxy) from a tile's R16 height + world transform. Resolution-map R16 → heightfield.
   - *Verify:* `TerrainColliderProviderTests` — heightfield samples match the tile R16 within epsilon; a raycast down hits the expected world Y.
4. **`TerrainColliderConfig` + `TerrainColliderStreamer`** — near-ring of collider tiles (radius ≤
   render ring), amortised heightfield cook (`MAX_COOKS_PER_FRAME`), evict beyond the near ring.
   Driven by the Phase 3 residency manager (subscribe to resident set) OR single-tile against Phase 0
   if Phase 3 is not yet present.
   - *Verify:* `TerrainColliderStreamerDiffTests` — near membership; cook amortisation; evict releases the collider (no leak). Play-mode: a character/raycast lands on terrain near the camera; far tiles have no collider.

## Risk Assessment

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| Grass/rocks float — sampler height ≠ rendered surface (CPU/GPU decode drift) | 4 | 5 | 20 | `TrySample` calls the SAME `TerrainHeightSampleCpu` SSOT decode the Phase 1 VTF mirrors; ramp-parity unit test; demo visual gate. |
| Heightfield collider cook spikes the frame on tile cross | 3 | 4 | 12 | `TerrainCollider` heightfield (cheaper than MeshCollider cook); near-ring only; `MAX_COOKS_PER_FRAME` amortisation. |
| Cross-asmdef coupling violates library-decoupling (two-way reference) | 2 | 4 | 8 | One-way only: GpuTerrain → GrassInteract for the interface; assert GrassInteract has no GpuTerrain reference; documented. |
| Off-grid / streaming hole → TrySample returns stale height | 2 | 4 | 8 | Return `false` when the tile is not resident/loaded (scatter skips the candidate, matching the terrain-hole contract). |
| Collider resolution mismatch vs render (character clips terrain) | 2 | 3 | 6 | Resolution-map R16 → heightfield documented; provider test asserts raycast Y matches rendered height. |

**Score ≥ 15:** floating-scatter (20) — the load-bearing risk. Mitigation (shared SSOT decode +
parity test + demo visual gate) MUST pass before this phase is called done.

## Timeline

| Task | Effort | Notes |
|---|---|---|
| HeightmapSurfaceSampler + tests | M | ship EARLY (only needs Phase 0) — unblocks grass |
| Grass grounding integration | S | demo wiring, no algorithm change |
| TerrainColliderProvider + tests | M | heightfield build |
| TerrainColliderStreamer + config + tests | M | near-ring lifecycle |
| **Total** | **M** | Critical path: Sampler (early) ∥ Collider provider → streamer |

## Test strategy

EditMode NUnit mirroring `InstanceVisibilityColliderDriverTests`:
- `HeightmapSurfaceSamplerTests` — height parity with CPU decode, off-grid false, slope/normal.
- `TerrainColliderProviderTests` — heightfield ↔ R16 parity, raycast Y.
- `TerrainColliderStreamerDiffTests` — near membership, cook amortisation, evict release.
- **Demo visual gate** — existing grass/rock grounds correctly on custom terrain (no float/sink).
</content>
