# Adversarial Review — GpuTerrain CDLOD module

Branch `plan/gpu-terrain-cdlod` (`a59e103..4ac7b1e`). Scope: `Assets/GpuTerrain/**` + `ScatterField.cs` seam. Read-only.

## Two explicit verdicts (requested)

### RenderGraph submit discipline — PASS (with one MINOR)
`GpuTerrainEngine` mirrors `GrassGpuEngine` correctly on all five load-bearing points:
- Submit driven from the **player loop** (`GpuTerrainRenderer.LateUpdate` in play; `EditorApplication`/`beginCameraRendering` per-camera in edit) — not an all-cameras submit. ✓
- Non-zero `worldBounds` on `RenderParams` (extent clamped `Mathf.Max(1f, …)` on Y). ✓
- `RenderParams` built via the **Material constructor** (`new RenderParams(this.patchMaterial!)`), preserving `renderingLayerMask`. ✓
- Buffers bound to the **material** (`patchMaterial.SetBuffer`), never an MPB; `matProps` never set. ✓
- Buffers **rebound each Submit** (node + visible buffers re-`SetBuffer` every frame — domain-reload guard). ✓
- `CopyCounterValue` into the args buffer at offset 4 matches the reference. ✓

The only deviation is the indirect-draw camera scoping, see MINOR-1 — not a draw-drop.

### SSOT height decode parity — PASS
All three sites are arithmetically identical, normalization included:
- `TerrainHeightFormat.DecodeHeight`: `min + (raw/65535) * (max-min)`.
- `TerrainHeightSampleCpu.SampleDecoded` → calls `DecodeHeight` (same).
- `TerrainVtf.hlsl DecodeHeight(raw)`: `_MinHeight + raw*(_MaxHeight-_MinHeight)` — here `raw` is the **already-normalized** [0,1] texture sample (`SampleLevel(...).r` on R16/RHalf returns normalized), so the `/65535` is correctly absorbed by the hardware. Verified the RHalf fallback also stores `raw/65535` normalized (`TerrainTileGpuResources.ConvertR16ToRHalf`). Formula parity is exact.

The decode math is clean. The DEFECT below is in the **UV** feeding that decode on the GPU, not the decode itself.

---

## BLOCKER

### B1 — GPU tile UV uses world coords, not tile-local: every non-(0,0) tile samples the wrong height/splat
`Assets/GpuTerrain/Shaders/TerrainPatch.shader:111-113` (forward VS), `:213` (shadow VS), and the normal path via `:131`.

```hlsl
float tileU = worldX / 256.0; // TILE_SIZE_M = 256
float tileV = worldZ / 256.0;
```

This is the **global** UV, not the tile-local UV. The CPU SSOT (`TerrainWorldGrid.WorldToTexelUV`) correctly subtracts the tile origin: `u = (worldX - origin.x) / TILE_SIZE_M`. For tile (0,0) the two agree, so all current single-tile tests and the demo scene pass. For any streamed tile at coord (tx,tz)≠(0,0) (the entire Phase 3 multi-tile feature), `worldX/256` is ≥1 and gets clamped (`wrapMode=Clamp`) to the tile edge → the whole tile renders the boundary row of the heightmap, i.e. flat/garbage terrain. **This is exactly the "preview≠saved / grass floats" class the SSOT focus area warns about, surfacing as soon as more than one tile is resident.**

Why tests miss it: every EditMode test exercises tile (0,0) or the CPU sampler (which is correct). The bug only appears with a live multi-tile camera in play mode.

Fix: pass the tile origin into the material (the engine already knows it — `TileOriginWorld(tile.tileCoord)` in `Build`) as a `float2 _TileOriginWS` uniform and compute `tileUV = (worldXZ - _TileOriginWS) / TILE_SIZE_M`. Do it in both passes and keep `TILE_SIZE_M` a uniform too (see MINOR-2). The shadow pass at `:213` has the identical defect and must be fixed in lockstep.

---

## MAJOR

### M1 — Async loader generation-token guard has a re-enqueue race (use-after-evict window)
`Assets/GpuTerrain/Runtime/TerrainTileLoader.cs:113-146`.

`CancelTile` increments the coord's generation and removes it from `inFlight`. But a *stale* `PendingCallback` already sitting in `pendingQueue` (captured the OLD generation) is correctly rejected — good. The hole is the **opposite** order:

1. Tile A enqueued at gen 0 (in `pendingQueue`, captured gen 0).
2. `CancelTile(A)` → gen becomes 1, `inFlight` cleared.
3. `Enqueue(A)` again (ring brought it back) → captures gen 1, `inFlight` re-added.
4. `DrainMainThreadQueue` runs: the **first** callback (gen 0) is rejected (0≠1) — good — but it also does `this.inFlight.Remove(pending.Coord)` at `:144` **only on the accepted path**. The rejected path `continue`s at `:142` without touching `inFlight`. That's actually fine. The real issue: the second (gen 1) callback then fires `OnTileLoaded`, which builds GPU resources. If between step 3 and the drain the tile was evicted again via `CancelTile` (gen→2), the gen-1 callback is correctly rejected. So the token logic itself holds.

The genuine defect: **`inFlight.Remove` in `CancelTile` (:115) lets a second `Enqueue` for an in-flight coord slip through before the first callback drains**, posting TWO thread-pool work items for the same coord. Both enqueue a `PendingCallback`; the first to drain with a matching gen wins and the residency `Add` double-load guard (`:107` in residency set / `:149` in manager) rejects the second — so no double GPU build, but the second `Upload`/`Build` in `OnTileLoaded` runs BEFORE the `Contains` check only if ordering differs. Re-read: `OnTileLoaded:149` checks `Contains` first and returns, so it's guarded. Net: **no leak in the happy path, but the `inFlight` set no longer reflects reality** (a cancelled-then-re-enqueued coord can have a phantom in-flight entry that blocks a legitimate re-load until the next drain). Concretely a player who walks out of and back into a tile's ring within one frame can see that tile fail to reload until the stale callback drains.

Fix: do not `inFlight.Remove` in `CancelTile`; instead let the generation mismatch be the sole authority and clear `inFlight` for the coord only inside `DrainMainThreadQueue` (both accepted AND rejected paths) and `OnTileLoaded`. Add an `inFlight.Remove(pending.Coord)` on the `continue` (stale) branch at `:142`.

### M2 — Streaming upload budget counts enqueues, not uploads; `MAX_UPLOADS_PER_FRAME` is not enforced
`Assets/GpuTerrain/Runtime/TerrainStreamingManager.cs:100-133`.

The comment at `:18` and `:120` says "enqueue loads … within upload budget", and `uploadBudget = MAX_UPLOADS_PER_FRAME`. But the GPU upload (`TerrainTileGpuResources.Upload` + `engine.Build`) happens in `OnTileLoaded`, which is invoked from `DrainMainThreadQueue` — and `DrainMainThreadQueue` drains the **entire** pending queue every tick with no cap (`while(true)` at `:128`). So if N tiles' async callbacks land in the same frame, all N do a synchronous main-thread `Texture2D.LoadRawTextureData + Apply + new Material` in one frame — the exact hitch the per-frame budget was meant to prevent. The `uploadBudget` only throttles how many *enqueues* are posted per tick, which is the cheap part.

Fix: cap the drain loop in `DrainMainThreadQueue` (pass a `maxThisFrame`), or move the budget check into `OnTileLoaded` and re-queue overflow.

---

## MINOR

### MINOR-1 — Indirect draw is camera-scoped via `RenderParams.camera`, but the terrain engine ignores `targetCamera` for the cull while honoring it for the draw
`GpuTerrainEngine.Submit:167,201`. The cull uses `targetCamera ?? Camera.main` for frustum planes, and `MakeRenderParams(targetCamera)` scopes the draw to that camera. In play mode `GpuTerrainRenderer.SubmitForCamera(null)` → draw renders in all cameras but cull frustum is `Camera.main`. That matches the grass reference, so it is consistent — but note `TerrainStreamingManager.Tick:140` submits with `cam` = `Camera.main` explicitly (`Submit(cam, cp)`), so in the streaming path the draw is scoped to `Camera.main` only and will NOT appear in a secondary/split-screen camera. Likely fine for this project; flagging because it diverges from the single-tile renderer's all-cameras play-mode behavior. Confirm intent.

### MINOR-2 — `TILE_SIZE_M` hardcoded as `256.0` in 3 shader files instead of a uniform
`TerrainPatch.shader:111-112,213`, `TerrainNormals.hlsl:23`. `TerrainNormals.hlsl:23` even comments "Phase 3 will promote to a uniform." Since this branch ships Phase 3 (multi-tile streaming), that promotion is now due — and B1's fix needs the tile origin uniform anyway, so add `_TileSizeM` at the same time. A magic-number literal that must stay in lockstep with `TerrainWorldGrid.TILE_SIZE_M` is an SSOT hazard.

### MINOR-3 — `maxCullSqrDistance` hardwired to 0 (distance cull disabled) in the terrain cull
`GpuTerrainEngine.RecordCullCommands:279` sets `maxCullSqrDistance = 0f` → the compute's distance reject is a no-op (by design, `0 = no cull`). That means the GPU node cull only does frustum + empty-sentinel; the LOD/distance selection is entirely CPU-side in `CdlodQuadtree.Select`. Correct for the CDLOD design, but the `maxCullSqrDistance` plumbing is dead code today. Either wire it to the far LOD range or drop it to avoid implying a guarantee that isn't active.

### MINOR-4 — `TerrainSculptRtWriteback` async readback can write into an evicted/disposed tile
`TerrainSculptRtWriteback.cs:66-95`. The `AsyncGPUReadback` callbacks null-check `pendingTile`/`pendingGpu` but those are only nulled in `Dispose()`. If the user re-targets a different tile (calls `RequestAsync` again) between the height readback request and its callback, the callback writes the OLD readback into the NEW `pendingTile` (fields were overwritten). The single-slot `pending*` design has no generation token (unlike the streamer). Low severity (editor-only, single-user, stroke-end triggered) but a real correctness hole if strokes on two tiles overlap a frame. Capture the tile/gpu/RT references as locals at `RequestAsync`/`Tick` time and close over them, rather than reading `this.pending*` inside the callbacks.

## NIT

- `TerrainStreamingManager.cs:101` `uploadBudget` is read but (per M2) only gates enqueues — rename to `enqueueBudget` until M2 is fixed, or the name lies.
- `TerrainNodeBuffer.Upload` releases + reallocates both GraphicsBuffers every frame (`ReleaseBuffers()` at `:65` then `new GraphicsBuffer`). The grass reference bakes once and reuses. For ≤512 nodes/tile this is minor, but per-frame GraphicsBuffer churn across many resident tiles adds up — consider grow-only reuse keyed on capacity.
- `CdlodNode.STRIDE = 32` is asserted-by-comment against the HLSL `RenderNode`; there's no runtime stride assert like the grass path reportedly has. The struct field order/size is correct (float3+float+uint+float+float+uint = 32), but a `Marshal.SizeOf`/static assert would make a future field add fail loudly instead of corrupting reads.
- `ScatterField.cs:155-157` `OnDisable()` is now empty (engines disposed only in `OnDestroy`). Engines + GPU buffers survive a disable/enable cycle of the component; intentional? If a domain reload disables-without-destroy, buffers persist. Confirm.

## Decoupling — PASS
`GpuTerrain → GrassInteract` is the only cross-reference (`GpuTerrainScatterGround.cs`, `HeightmapSurfaceSampler.cs` reference GrassInteract). No reverse code reference: `ScatterField.cs` only mentions `GpuTerrain` in doc comments; `ExternalSampler` is typed `ISurfaceSampler` (GrassInteract-owned interface), carrying no GpuTerrain token. Seam is genuinely generic. ✓

---

## Recommendation: NO-SHIP until B1 fixed

**B1 is a blocker** — the multi-tile feature that is the headline of this branch renders wrong terrain for every tile except (0,0), and it is invisible to the 198 passing EditMode tests because they all exercise tile (0,0) or the (correct) CPU sampler. Fix B1 (+ MINOR-2 since the fix needs the origin/size uniforms), then re-verify in a live ≥2-tile play-mode scene. M1/M2 are streaming-robustness issues that should be fixed before relying on streaming under load but do not corrupt rendered output. The RenderGraph discipline and decode-formula SSOT are both solid — the defect is confined to the UV that feeds the (correct) decode.
