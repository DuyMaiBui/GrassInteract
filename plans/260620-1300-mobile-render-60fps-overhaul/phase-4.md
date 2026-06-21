# Phase 4 — Grass scatter bake → `BakedGrassData` blob (L)

**Priority:** P2. **Effort:** L. **HIGH-RISK (R2 — buffer-layout).** Enables Phase 3's hitch-free full reloads.

## Objective

Move grass scatter computation from runtime to bake time. Add a `BakedGrassData` byte-blob sub-asset (mirroring the proven `AuthoredInstancesData` V3 format) that stores the already-baked `BladeInstance[]` + `ChunkAabb[]` + `ChunkRange[]` + grid metadata. At runtime, `GrassGpuEngine.Build` becomes a 3× `SetData` upload instead of a full scatter + counting-sort + AABB build. Kills the **−150–600ms startup hitch** and makes any future buffer rebuild a cheap reload rather than a main-thread re-scatter spike.

## Design (operationalizes brainstorm §"Grass scatter bake")

### Why this is low-conceptual-risk but high-mechanical-risk

The exact arrays already exist as CPU-side fields on `ChunkedBladeBuffer` (`bladeInstances`, `chunkAabbs`, `chunkRanges`) plus the `GridX/GridZ/ChunkSize/TotalChunks/TotalBlades/ScaleMax2` metadata. Phase 4 serializes those arrays and uploads them back — no new math. The risk (R2) is purely **byte-layout fidelity**: the blob must round-trip the 20-B / 24-B / 8-B blittable structs exactly, or grass renders garbage. Mirror the `AuthoredInstancesData` V3 pattern (version header byte + flat byte[] + explicit Write/Read helpers) which is already battle-tested with round-trip tests (`AuthoredInstancesDataBlobTests`).

### Refactor `ChunkedBladeBuffer.Bake`

Split the existing `Bake(...)` into two pure halves so the editor can bake arrays without a GPU, and the runtime can upload without re-baking:

- `BakeArrays(GrassScatterResult scatter, ScatterLayer layer, ...)` → fills `bladeInstances/chunkAabbs/chunkRanges` + metadata + `scaleMax2`. **No `SetData`, no GraphicsBuffer.** Editor-callable, deterministic.
- `UploadFromArrays()` → the 3× `new GraphicsBuffer(...) + SetData(...)` block (currently the tail of `Bake`). Runtime-callable.
- `Bake(...)` keeps its current behavior = `BakeArrays(...)` + `UploadFromArrays()` (preserve the live editor scatter path so fast iteration is unchanged).
- Add `LoadFromArrays(BladeInstance[], ChunkAabb[], ChunkRange[], grid metadata, scaleMax2)` → sets the CPU arrays + metadata from a deserialized blob, then `UploadFromArrays()`.

`ValidatePartition` stays as the CI gate and runs against `BakeArrays` output.

### `BakedGrassData` blob format (mirror `AuthoredInstancesData` V3)

- `[VERSION_BYTE=1]` + header (`GridX, GridZ, ChunkSize, TotalChunks, TotalBlades` as int32, `ScaleMax2` as float, `oriented` flag) + `BladeInstance[]` (20 B each: float3 posWS, uint packedYawScale, uint hash) + `ChunkAabb[]` (24 B each) + `ChunkRange[]` (8 B each). Explicit `WriteFloat/WriteUInt/WriteInt` + `Read*` helpers identical in spirit to `AuthoredInstancesData`. Reject any blob whose version byte ≠ 1 with an exception (no silent migration).

### Runtime branch in `GrassGpuEngine.Build`

```
if (layer.BakedGrass != null)  → bladeBuffer.LoadFromBaked(layer.BakedGrass)   // 3× SetData only
else                            → current live-scatter path (BakeArrays+Upload)  // editor fast-iteration
```

Add a `BakedGrassData? BakedGrass` reference on `ScatterLayer` (serialized). When present, the runtime never runs `GrassScatter.Build`/counting-sort.

### Editor bake tool

`WorldGrassBaker` (new) bakes each grass layer's scatter via `ChunkedBladeBuffer.BakeArrays`, validates with `ValidatePartition`, packs a `BakedGrassData`, and writes it as a sub-asset via `AssetDatabase.AddObjectToAsset` (mirroring how `WorldMapBaker.BakeOneTile` creates/saves tile sub-assets). Hook it as a menu item under `Tools/Library/WorldPainter/` and/or into the existing world bake flow alongside `WorldMapBaker.BakeAll`.

## File ownership

- **Edit:** `Assets/WorldPainter/Runtime/Scatter/ChunkedBladeBuffer.cs` — split `Bake` → `BakeArrays` + `UploadFromArrays`; add `LoadFromArrays` / `LoadFromBaked`. Keep `Bake` as the composed editor path. Preserve `ValidatePartition`.
- **Create:** `Assets/WorldPainter/Runtime/Scatter/BakedGrassData.cs` — new `#nullable enable` `ScriptableObject` sub-asset; V1 byte-blob pack/unpack mirroring `AuthoredInstancesData`. `this.` prefix, byte-layout constants, version header.
- **Edit:** `Assets/WorldPainter/Runtime/Scatter/GrassGpuEngine.cs` — branch `Build` on `layer.BakedGrass != null`; call `LoadFromBaked` (3× SetData) vs the live path.
- **Edit:** `Assets/WorldPainter/Runtime/Scatter/*ScatterLayer*.cs` (the `ScatterLayer` definition) — add serialized `BakedGrassData? BakedGrass` field + accessor.
- **Create:** `Assets/WorldPainter/Editor/WorldPainter/WorldGrassBaker.cs` — editor baker; `BakeArrays` → `ValidatePartition` → pack `BakedGrassData` → `AssetDatabase.AddObjectToAsset`. Menu item + hook into world bake.
- **Create:** `Assets/WorldPainter/Tests/Editor/BakedGrassDataBlobTests.cs` — round-trip test: scatter → `BakeArrays` → pack blob → unpack → `LoadFromArrays` → assert byte-identical `BladeInstance[]`/`ChunkAabb[]`/`ChunkRange[]` + metadata + `ValidatePartition` passes on both. (Mirror `AuthoredInstancesDataBlobTests`.)

## Step-by-step tasks

1. Refactor `ChunkedBladeBuffer.Bake` into `BakeArrays` (pure) + `UploadFromArrays` (GPU). Verify `Bake` composed behavior unchanged.
2. Add `LoadFromArrays` / `LoadFromBaked`.
3. Author `BakedGrassData` with V1 blob pack/unpack (byte-exact, version header).
4. Add `BakedGrass` field to `ScatterLayer`.
5. Branch `GrassGpuEngine.Build` on baked-vs-live.
6. Author `WorldGrassBaker` editor tool (BakeArrays → ValidatePartition → pack → AddObjectToAsset → menu + bake hook).
7. Write `BakedGrassDataBlobTests` round-trip + partition validation.
8. Bake the demo grass field; ship the baked blob.

## On-device verification gate (PASS criteria)

- [ ] On device, the baked path renders grass **byte-identically** to the live-scatter path (same blade positions / density / look) — A/B compare a build with `BakedGrass` set vs null.
- [ ] **Grass field load shows zero multi-frame hitch** on device (PerformanceConsole 1%-low / "GC coll/frame" readout stays clean during load) — the −150–600ms startup spike is gone.
- [ ] `ValidatePartition` passes on the baked blob (CI gate) — partition contiguous, covers `[0, TotalBlades)`, AABB union covers world bounds.
- [ ] `BakedGrassDataBlobTests` round-trip is byte-identical (run via Test Runner; manual run if MCP run_tests is wedged).
- [ ] Steady-state FPS unchanged vs live path (this phase is a startup-hitch + reload-cost win, **not** a steady-fps win — set that expectation).

## Risk note (R2 — HIGH, score 15)

Buffer-layout mismatch (struct stride drift, chunk-range corruption) renders garbage grass. **Mitigations (mandatory before starting):**
1. Byte-exact mirror of the proven `AuthoredInstancesData` V3 helpers; version header rejects any mismatch.
2. `ValidatePartition` is a hard CI gate on the baked output.
3. `BakedGrassDataBlobTests` round-trip asserts byte-identity scatter→blob→load.
4. Runtime branch keeps the live path as a fallback (`BakedGrass == null` → unchanged behavior), so a bad bake cannot brick the editor iteration loop.
5. Stride constants (`BLADE_STRIDE=20`, `AABB_STRIDE=24`, `RANGE_STRIDE=8`) are the SSOT shared between blob layout and GraphicsBuffer — assert them in the test.
