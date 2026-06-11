# Phase 0 — Heightmap Data Model

**Effort:** M · **Blocks:** Phases 1, 3, 4, 5 (everything reads this) · **Blocked by:** nothing (foundation)

## Goal

Define the custom terrain tile asset format (R16 height + 4-channel splat), the world tile-grid
layout that maps world XZ → tile index → texel, and the GPU upload path that lands a tile's height
+ splat into GPU textures ready for the Phase 1 renderer to sample. Decoupled from Unity
`TerrainData`. Provide a CPU-readable height accessor (consumed by Phase 4 sampler and Phase 5 sculpt).

## Feasibility

- **Reuse check:** GPU-upload + counting-sort partition + `ValidatePartition` discipline reused from
  `ChunkedBladeBuffer.cs`. Tile asset is NEW (no existing height-tile type). World-grid math is NEW
  but mirrors the field-min-corner / cell-coordinate convention in `ChunkedBladeBuffer.Bake`.
- **Complexity:** moderate. The data model is the SSOT every later phase depends on — get the R16
  encoding, world↔texel mapping, and edge-overlap (skirt) convention right here.

## File ownership (new files)

```
Assets/GpuTerrain/
  GpuTerrain.asmdef                              (module asmdef, mirror GrassInteract)
  Runtime/
    TerrainTileAsset.cs              (ScriptableObject: R16 height bytes + splat bytes + tile metadata)   ≤200
    TerrainWorldGrid.cs             (world XZ ↔ tileCoord ↔ texel UV math; tile size, overlap convention) ≤200
    TerrainHeightFormat.cs          (R16 encode/decode constants; height range; normalized↔world Y)        ≤120
    TerrainTileGpuResources.cs      (per-tile GPU height/splat texture upload + dispose; IDisposable)       ≤200
    TerrainHeightSampleCpu.cs       (bilinear CPU height read from R16 — used by Phase 4 sampler + Phase 5) ≤150
  Editor/
    TerrainTileImporter.cs          (import R16/EXR → TerrainTileAsset; or ScriptedImporter)                ≤200
  Tests/Editor/
    GpuTerrain.EditorTests.asmdef   (mirror GrassInteract.EditorTests.asmdef references)
    TerrainWorldGridTests.cs        (round-trip world↔texel; boundary/clamp)
    TerrainHeightFormatTests.cs     (R16 encode→decode→encode byte-stability; range mapping)
    TerrainHeightSampleCpuTests.cs  (bilinear interpolation correctness on a known ramp heightmap)
```

## Tasks

1. **Module asmdef + namespace.** Create `GpuTerrain.asmdef` (rootNamespace `GpuTerrain`,
   references mirror `GrassInteract.asmdef` — empty references, autoReferenced true).
   - *Verify:* asmdef compiles; a trivial `GpuTerrain` namespace class is visible. (`refresh_unity(force, all)` — asmdef-only edits no-op a plain refresh.)
2. **`TerrainHeightFormat`** — R16 constants: `HEIGHT_BITS = 16`, normalized [0,1] ↔ world Y via
   serialized `minHeight`/`maxHeight` (named, not magic). Encode/decode helpers.
   - *Verify:* `TerrainHeightFormatTests` — encode(decode(x)) byte-stable for 0, 0.5, 1.0, and a fuzz sweep.
3. **`TerrainWorldGrid`** — tile size (metres, named const default e.g. `TILE_SIZE_M`), tiles-per-world,
   `WorldToTileCoord`, `WorldToTexelUV`, `TileOriginWorld`. Define the **1-texel skirt/overlap convention**
   (adjacent tiles share an edge row so Phase 1 morph + Phase 2 normals are seamless at tile boundaries).
   - *Verify:* `TerrainWorldGridTests` — world↔texel round-trips; a point on a tile boundary maps consistently from both tiles.
4. **`TerrainTileAsset`** — `ScriptableObject` holding `byte[]` R16 height (heightRes²×2 B), `byte[]`
   splat (splatRes²×4 B), `Vector2Int tileCoord`, `int heightRes`, `int splatRes`, height range.
   Keep blittable/serializable; no Texture references stored (textures are built on upload).
   - *Verify:* create one in a test, serialize/deserialize, assert byte arrays survive.
5. **`TerrainTileGpuResources`** — build a `Texture2D`/`GraphicsBuffer` for height (R16 → `RHalf`/`R16`
   format) + splat (`RGBA32` texture array slot), `IDisposable` release. Mirror `ChunkedBladeBuffer`
   dispose discipline (null-safe `Release()`).
   - *Verify:* upload a known 4×4 ramp tile; read back via `Texture.GetPixelData`/async readback in a Play-mode-free probe; assert center texel matches.
6. **`TerrainHeightSampleCpu`** — bilinear height read from the R16 bytes at world XZ (SSOT with the
   GPU VTF sample formula so CPU sampler and GPU render agree). This is the function Phase 4's
   `HeightmapSurfaceSampler.TrySample` calls.
   - *Verify:* `TerrainHeightSampleCpuTests` — sample a linear ramp tile at known XZ, assert interpolated height within epsilon of analytic value.
7. **`TerrainTileImporter`** (Editor) — import a R16 `.raw` or EXR into a `TerrainTileAsset`
   (`AssetDatabase` create). Keep minimal; full authoring is Phase 5.
   - *Verify:* import a fixture R16 file, assert resulting asset's height bytes length == heightRes²×2.

## Risk Assessment

| Risk | Likelihood | Impact | Score | Mitigation |
|---|---|---|---|---|
| R16/world-Y mapping inconsistent between CPU sample and GPU VTF → grass floats / terrain seams | 4 | 5 | 20 | Single SSOT decode formula in `TerrainHeightFormat`; both CPU (`TerrainHeightSampleCpu`) and Phase 1 GPU VS call the identical math; byte-stability unit test. |
| Tile boundary seams (no skirt/overlap convention) | 3 | 4 | 12 | Define the 1-texel shared-edge convention in `TerrainWorldGrid` NOW; Phase 1 morph + Phase 2 normals assume it. |
| R16 texture format unsupported on a target mobile GPU | 2 | 4 | 8 | Probe `SystemInfo.SupportsTextureFormat`; fall back to `RHalf`; record chosen format on the GPU-resources object. |
| Splat byte layout drift vs Phase 2 sampler expectation | 2 | 3 | 6 | Document RGBA channel→layer mapping in `TerrainTileAsset`; Phase 2 cites it as SSOT. |

**Score ≥ 15:** the R16↔world-Y SSOT risk (20). Mitigation (single decode formula + byte-stability
test) MUST land before Phase 1 consumes the height texture.

## Timeline

| Task | Effort | Notes |
|---|---|---|
| asmdef + namespace bootstrap | S | unblocks all later files |
| TerrainHeightFormat + tests | S | the SSOT decode — do first |
| TerrainWorldGrid + tests | S | skirt convention decision |
| TerrainTileAsset | S | — |
| TerrainTileGpuResources | M | format probe + upload + dispose |
| TerrainHeightSampleCpu + tests | S | Phase 4 dependency |
| TerrainTileImporter | S | minimal; Phase 5 extends |
| **Total** | **M** | Critical path: HeightFormat → WorldGrid → GpuResources |

## Test strategy

EditMode NUnit tests under `Tests/Editor/`, mirroring `GrassInteract.Tests` (pure-math, no Play-mode):
- `TerrainHeightFormatTests` — encode/decode byte-stability + range mapping.
- `TerrainWorldGridTests` — world↔texel round-trip, boundary consistency, out-of-grid clamp.
- `TerrainHeightSampleCpuTests` — bilinear correctness on analytic ramp + flat tiles.
- GPU upload verified via a small readback probe (the structural-only pattern from `GrassGpuEngine.SelfTest`; full pixel correctness deferred to on-device).
</content>
