# Phase 3 — Multi-Tile Streaming

**Effort:** L · **Blocks:** Phase 4 (collider streaming), Phase 5 (cross-tile sculpt) · **Blocked by:** Phase 0, Phase 1

## Goal

Scale from one tile to a large open world: a GPU residency ring (~5×5) of resident tiles around the
camera, async disk streaming of `TerrainTileAsset` height/splat into GPU textures, and far-tile
eviction with bounded memory. This is the heavyweight phase that makes the world large without
blowing the mobile memory budget.

## Feasibility

- **Reuse check:** per-tile rendering is the Phase 1 `GpuTerrainEngine` instanced once per resident
  tile; GPU upload/dispose is Phase 0's `TerrainTileGpuResources`. The residency ring + async loader +
  eviction are NEW. Eviction throttling mirrors the amortise-across-frames discipline in
  `InstanceVisibilityColliderDriver` (`maxCollidersPerFrame` → `maxTileUploadsPerFrame`).
- **Complexity:** complex — async lifecycle, memory bounding, and avoiding hitches on tile cross.

## File ownership (new files)

```
Assets/GpuTerrain/
  Runtime/
    TerrainResidencyRing.cs         (which tileCoords should be resident for a camera pos; ring radius named const) ≤200
    TerrainStreamingManager.cs      (MonoBehaviour [ExecuteAlways]: drives load/unload, owns per-tile engines)      ≤200
    TerrainTileLoader.cs            (async load TerrainTileAsset bytes off main thread → main-thread GPU upload)     ≤200
    TerrainTileResidencySet.cs      (resident-tile bookkeeping: coord→{asset, gpuResources, engine}; diff helpers)   ≤200
    TerrainStreamingConfig.cs       (named consts: RING_RADIUS, MAX_RESIDENT_TILES, MAX_UPLOADS_PER_FRAME, hysteresis) ≤120
  Tests/Editor/
    TerrainResidencyRingTests.cs    (ring membership: correct coords resident per camera pos; radius respected)
    TerrainResidencyDiffTests.cs    (load/evict diff: moving camera one tile loads N, evicts N; hysteresis on boundary)
    TerrainTileResidencySetTests.cs (bookkeeping add/remove/lookup; double-load guarded; eviction releases GPU)
```

## Tasks

1. **`TerrainStreamingConfig`** — named consts: `RING_RADIUS` (≈2 → 5×5), `MAX_RESIDENT_TILES`,
   `MAX_UPLOADS_PER_FRAME` (amortise), boundary `HYSTERESIS` (avoid thrash on edge straddle).
   - *Verify:* referenced everywhere; no inline ring/cap literals.
2. **`TerrainResidencyRing`** — pure function: camera world pos → set of `tileCoord` that should be
   resident (square/diamond ring of `RING_RADIUS` around the camera's tile).
   - *Verify:* `TerrainResidencyRingTests` — membership matches expected coords for several camera positions; radius respected; deterministic.
3. **`TerrainTileResidencySet`** — bookkeeping `Dictionary<tileCoord, ResidentTile>` (asset +
   `TerrainTileGpuResources` + `GpuTerrainEngine`); `Diff(desired)` → (toLoad, toEvict). Mirror the
   double-buffered desired-set discipline from `InstanceVisibilityColliderDriver` (read stable set,
   commit atomically).
   - *Verify:* `TerrainTileResidencySetTests` — add/remove/lookup; double-load guarded; evict disposes GPU resources (no leak).
4. **`TerrainTileLoader`** — async read `TerrainTileAsset` bytes off the main thread (file IO / async
   `Resources`/Addressables-agnostic for now), then hand off to the main thread for the GPU upload
   (Phase 0 `TerrainTileGpuResources` — GraphicsBuffer/Texture creation is main-thread only).
   - *Verify:* load a fixture tile async, assert bytes arrive and upload happens on the main thread without exception; cancellation on evict-before-load-complete is clean.
5. **`TerrainStreamingManager`** — `[ExecuteAlways]` MonoBehaviour: each player-loop tick compute
   desired ring → diff → enqueue loads (≤ `MAX_UPLOADS_PER_FRAME`) → evict far tiles → submit each
   resident tile's Phase 1 engine. Submit discipline unchanged (player-loop, per-tile RenderMeshIndirect).
   - *Verify:* fly the camera across tile boundaries — tiles load ahead, evict behind, resident count stays ≤ cap, no per-cross frame hitch (`rendering_stats` / profiler).
6. **Eviction memory proof** — evicted tiles release their `TerrainTileGpuResources` + engine buffers.
   - *Verify:* a soak test (move camera in a loop) shows resident GPU buffer count bounded (no monotonic growth).

## Risk Assessment

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| Many tiles → GPU/CPU memory blowup (no eviction or leak on evict) | 4 | 5 | 20 | Hard `MAX_RESIDENT_TILES` cap + `TerrainTileGpuResources.Dispose` on evict; soak test asserts bounded buffer count; ring radius is a named const. |
| Frame hitch when a tile uploads on cross (synchronous GPU upload) | 4 | 4 | 16 | `MAX_UPLOADS_PER_FRAME` amortisation (driver pattern); async byte-load off main thread; preload ring ahead with hysteresis. |
| Async load races eviction → upload a tile already evicted / use-after-dispose | 3 | 4 | 12 | Generation token + double-buffered desired set (the `InstanceVisibilityColliderDriver` stale-callback guard); cancel in-flight loads on evict. |
| Boundary thrash (camera straddles edge → repeated load/evict) | 3 | 2 | 6 | `HYSTERESIS` margin on the ring; evict only beyond radius+hysteresis. |
| Per-tile indirect draws multiply draw count back up | 2 | 3 | 6 | Resident tile count bounded by the ring; each tile is still ~1 draw → total stays a small multiple (validated against Phase 1 single-tile baseline). |

**Score ≥ 15:** memory blowup (20) and upload hitch (16). Both gated by named caps + amortisation +
a soak test before this phase is called done.

## Timeline

| Task | Effort | Notes |
|---|---|---|
| TerrainStreamingConfig | S | caps SSOT |
| TerrainResidencyRing + tests | S | pure math |
| TerrainTileResidencySet + tests | M | diff + bookkeeping |
| TerrainTileLoader + tests | M | async + main-thread upload handoff |
| TerrainStreamingManager | L | lifecycle orchestration |
| Eviction soak proof | S | memory-bound gate |
| **Total** | **L** | Critical path: ResidencyRing → ResidencySet → Loader → Manager → soak |

## Test strategy

EditMode NUnit (pure-math + bookkeeping; async tested with a synchronous fixture loader):
- `TerrainResidencyRingTests` — membership/radius correctness.
- `TerrainResidencyDiffTests` — load/evict counts on camera move; hysteresis behaviour.
- `TerrainTileResidencySetTests` — bookkeeping + dispose-on-evict (no leak).
- **Play-mode soak** (manual / user-run) — camera loop, bounded resident count, no hitch — is the gate.
</content>
