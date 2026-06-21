# Phase 3 — 1-D Segment Streaming Window (Sliding Window, Hysteresis, Budgeted Instantiate, Async Load, Seam-Snap)

**Effort:** L · **Blocks:** 6 · **Blocked by:** 1, 2

## Goal

Stream the corridor as a sliding window of segments keyed off distance-along-track. Replace the cut 2-D Chebyshev ring (`TerrainResidencyRing`, 5×5, O(radius²)) with a 1-D O(1) window: a handful of segments resident, bounded memory, no thrash at boundaries, budgeted per-frame instantiate, async Addressables load-ahead. Verify seams between adjacent baked segments are crack-free.

**Provisional default (confirm decision #4):** `SEGMENT_LENGTH_M = 100`, window = 5 (2 ahead / 1 current / 2 behind), ~500m resident. Keeping 1-2 behind supports a rear camera + backward roll.

## File Ownership (real paths)

Create:
- `Assets/WorldPainter/Runtime/Segment/SegmentStreamWindow.cs` — core 1-D window. Given a `distanceAlongTrack` (driven by the test flythrough harness in this phase; a real mover later), computes the resident set `[current-2 .. current+2]`, diffs against last frame, triggers load/evict. Hysteresis margin before eviction. VContainer-injected; `UniTask`-based async load. NO 2-D coords.
- `Assets/WorldPainter/Runtime/Segment/SegmentLoadBudget.cs` — `UPPER_SNAKE_CASE` consts: `MAX_SEGMENT_LOADS_PER_FRAME`, `MAX_INSTANTIATES_PER_FRAME`, `HYSTERESIS_M`, `LOAD_AHEAD_COUNT`. Mirrors the amortization pattern of `TerrainStreamingConfig.cs` (MAX_UPLOADS_PER_FRAME, HYSTERESIS_TILES) but 1-D + metric.
- `Assets/WorldPainter/Runtime/Segment/SegmentAddressableLoader.cs` — async `Addressables.LoadAssetAsync<SegmentAsset>` per segment index with load-ahead, handle bookkeeping, release on evict. `UniTask` wrappers, named methods.

Edit / repurpose:
- `Assets/WorldPainter/Runtime/Scatter/InstanceVisibilityColliderDriver.cs` — reuse its per-frame collider-cook amortization (`maxCollidersPerFrame`, FIFO) for segment collider cook spreading. Narrow to forward-only lookahead.
- `Assets/WorldPainter/Runtime/Terrain/TerrainColliderStreamer.cs` / `TerrainColliderRing.cs` / `TerrainColliderProvider.cs` — KEEP-TRIM: extract cook-amortization (FIFO, `MAX_COOKS_PER_FRAME=1`) + metric-distance logic; drop the 2-D ring. Narrow to 1-D forward lookahead.

Use (do not modify): `IObjectPoolManager` for `SegmentInstance` + prop instances (`Load`/`Spawn<SegmentInstance>(nameof(SegmentInstance), ...)`/`Recycle`/`Unload`), per `mono-pool-spawn-unity.md`.

## Concrete Steps

1. Author `SegmentStreamWindow` with the diff algorithm: resident set as a function of `currentIndex = floor(distance / SEGMENT_LENGTH_M)`; load `[curr - behind .. curr + ahead]`; evict outside `[range ± HYSTERESIS_M]`.
2. Author `SegmentLoadBudget` consts and apply per-frame caps to load + instantiate.
3. Author `SegmentAddressableLoader` with load-ahead (preload curr+ahead+1) and release-on-evict; track handles to avoid leaks.
4. Pool `SegmentInstance` + props via `IObjectPoolManager`: `Load(nameof(SegmentInstance), WINDOW_SIZE+1)` at start; `Spawn`/`Recycle` on window slide; `Unload` on teardown.
5. Seam-snap: at runtime, assert adjacent segments' shared edge verts (position + normal + UV) match. Seam-snap is BAKED (Phase 5), but this phase adds the runtime/edit-time verification test that fails if edges diverge.
6. Drive `distanceAlongTrack` from a throwaway flythrough camera/scrubber (test harness, NOT a deliverable) to exercise the window forward and backward.

## Verification

- **Compile:** `read_console` clean; `run_tests` EditMode green.
- **Unit test:** window diff produces correct load/evict sets across a swept distance (forward + backward), respects hysteresis (no load/evict thrash when straddling a boundary), respects per-frame budget caps. Model after `TerrainResidencyDiffTests` (now deleted) but 1-D.
- **Unit test:** seam metadata of adjacent baked segments has matching shared-edge verts (pos+normal+UV) within epsilon — fails on a crack.
- **On-device (GLES3.0):** flythrough across ≥10 segments shows no hitch spikes (budget working), no thrash at boundaries, no visible seams, bounded memory (Memory Profiler: resident segment count stays at window size).

## Success Criteria

- O(1) residency: exactly the window's segments resident regardless of total track length.
- No load/evict thrash at boundaries (hysteresis verified).
- Per-frame instantiate + load budget enforced (no spike on segment crossing).
- Async load-ahead hides latency (no blank-ahead during forward travel).
- Seams crack-free across a 10+ segment flythrough.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Segment seams crack (CDLOD skirt tool gone) | 4 | 4 | 16 | Bake-time seam-snap (Phase 5) + this phase's runtime edge-vert-equality test as the gate; keep 1-2 behind so rear camera also seam-checked |
| Streaming hitch on segment crossing (instantiate/cook spike) | 3 | 4 | 12 | Per-frame instantiate + collider-cook budget (FIFO amortization reused from InstanceVisibilityColliderDriver); load-ahead so work is pre-done |
| Addressables handle leak on rapid back-and-forth | 3 | 3 | 9 | Release handle on evict; bookkeeping test for handle count == resident count; UniTask cancellation on teardown |
| Window thrash when straddling a boundary | 3 | 3 | 9 | HYSTERESIS_M margin; unit test sweeps across a boundary asserting zero redundant load/evict |
| Pool sized too small → runtime alloc on slide | 2 | 3 | 6 | Pre-Load(nameof(SegmentInstance), WINDOW_SIZE+1); assert no Instantiate after warmup |

Score ≥15 mitigated before start: row 1 (seam-snap bake dependency on Phase 5 + runtime gate test).
