# Phase 5 — WorldPainter Editor Bake Pipeline Repurpose (Editor-Only; Emits Segment Assets)

**Effort:** L · **Blocks:** 3 (supplies assets) · **Blocked by:** 2 (SegmentAsset schema)

## Goal

Repurpose the existing WorldPainter editor tooling (sculpt / paint / scatter / bake, `WorldMapAsset`, `ISurfaceSampler`) into an **editor-only** per-segment bake pipeline that emits the `SegmentAsset` payloads Phase 3 streams: ribbon mesh (seam-snapped) + baked collider + props + grass-density map + baked GI. Guarantee the editor assemblies and dead CDLOD/compute shaders do NOT compile into the runtime mobile binary.

**Provisional default (confirm decision #1):** centerline = **add `com.unity.splines` (editor-only)**; baker samples the spline to emit fixed-length segments. `com.unity.splines` is currently absent (verified: not in `Packages/manifest.json` / `packages-lock.json`). Alt: chain `WorldMapAsset` tiles (no new package).

## File Ownership (real paths)

Create (editor-only, under `Assets/WorldPainter/Editor/`):
- `Assets/WorldPainter/Editor/Segment/SegmentBaker.cs` — samples the centerline (spline or tile-chain), slices into `SEGMENT_LENGTH_M` segments, emits one `SegmentAsset` per segment via `AssetDatabase` + marks Addressable.
- `Assets/WorldPainter/Editor/Segment/SegmentRibbonMeshBaker.cs` — generates the ribbon mesh per segment from the centerline + cross-section width; produces 2-3 LODs for an LODGroup.
- `Assets/WorldPainter/Editor/Segment/SegmentSeamSnapper.cs` — snaps shared edge verts (position + normal + UV) of adjacent segments so Phase 3's seam test passes; writes seam metadata into `SegmentAsset`.
- `Assets/WorldPainter/Editor/Segment/SegmentColliderBaker.cs` — bakes the per-segment MeshCollider (cook offline).
- `Assets/WorldPainter/Editor/Segment/SegmentPropBaker.cs` — bakes per-segment props (standard MeshRenderers or instanced payload per Phase 4 decision).
- `Assets/WorldPainter/Editor/Segment/SegmentGrassDensityBaker.cs` — emits the R8 grass-density map per segment (reuse `WorldPainterDensityEncoder.cs`).
- `Assets/WorldPainter/Tests/Editor/SegmentSeamSnapTests.cs` — asserts adjacent baked segments have matching shared-edge verts (the bake-side counterpart to Phase 3's runtime gate).

Keep (reused by the baker — editor sampling + cook amortization):
- `Assets/WorldPainter/Runtime/Scatter/ISurfaceSampler.cs`, `RaycastSurfaceSampler.cs`, `TerrainSurfaceSampler.cs` — KEEP for grass placement + ground sampling during bake. (These are referenced by `GrassScatter.Build`; keep in runtime asmdef since grass uses them.)
- `Assets/WorldPainter/Runtime/Terrain/HeightmapSurfaceSampler.cs` — KEEP for height sampling.
- Editor pipeline: `WorldMapAsset.cs`, `WorldMapBaker.cs`, `WorldGrassBaker.cs`, `WorldPainterDensityEncoder.cs`, `WorldPainterAlphamapEncoder.cs`, `WorldMapAssetLifecycle.cs`, `WorldMapAssetFactory.cs` — repurpose as the segment-emitting pipeline.

Edit — asmdef + build hygiene (the runtime-bloat fix):
- `Assets/WorldPainter/WorldPainter.asmdef` — currently `includePlatforms: []` + `autoReferenced: true`, so all runtime terrain/compute code compiles into the mobile build. SPLIT: move CDLOD/compute-terrain runtime code (if any survived Phase 2 as "editor-reused") into the Editor asmdef or delete; ensure no compute-only terrain code remains in the runtime asmdef. Verify the runtime asmdef references nothing editor-only.
- `Assets/WorldPainter/Editor/WorldPainter.Editor.asmdef` — host the new `Segment/` bakers; add `com.unity.splines` reference if decision #1 = splines.
- `Assets/WorldPainter/Editor/Build/WorldPainterShaderStripper.cs` — extend to strip compute/indirect-only shader variants from the player build (so dead GPU-terrain shaders don't ship). Confirm it strips `GpuTerrain`-related variants.
- `Assets/WorldPainter/Editor/Build/MobileRenderConfigValidator.cs` — extend assertions: no SegmentAsset references a compute-only material on the Low tier; HDR-off assertion already present (used in Phase 6).

If decision #1 = splines:
- `Packages/manifest.json` — add `com.unity.splines` (editor-only usage; guarded so runtime build doesn't depend on it).

## Concrete Steps

1. Add centerline source per decision #1 (spline or tile-chain).
2. Author the segment bakers; emit one `SegmentAsset` per slice with all payloads.
3. Seam-snap adjacent segment edges at bake; write seam metadata; assert via `SegmentSeamSnapTests`.
4. Mark emitted `SegmentAsset`s Addressable with stable index-based keys (Phase 3 loads by key).
5. Split/clean the asmdefs so the runtime build excludes all bake tooling + dead CDLOD/compute code; extend the shader stripper.
6. Bake a 10+ segment test corridor for Phase 3/6 to stream.

## Verification

- **Compile:** `read_console` clean (editor + runtime); `run_tests` EditMode green incl. `SegmentSeamSnapTests`.
- **Build hygiene (critical):** make a player build (Android) and confirm the build report / `Library/PlayerScriptAssemblies` does NOT include `WorldPainter.Editor` or any CDLOD/compute-terrain runtime type. Grep the build's included-shaders for stripped GPU-terrain variants.
- **Asset emission:** baking the test corridor produces N `SegmentAsset`s, each Addressable, each with mesh+collider+props+density+GI.
- **On-device:** Phase 3 streams the baked corridor on GLES3.0 with crack-free seams (confirms bake quality).

## Success Criteria

- Bake pipeline emits valid per-segment `SegmentAsset`s from a centerline.
- Seam-snap verified at bake (`SegmentSeamSnapTests` green) and matches Phase 3's runtime gate.
- Runtime mobile build contains ZERO editor bake tooling and ZERO dead CDLOD/compute-terrain code (build-report verified).
- `ISurfaceSampler` family retained and used by the baker for placement.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Editor/CDLOD code leaks into runtime build (asmdef includePlatforms=[], autoReferenced=true today) | 4 | 4 | 16 | Asmdef split + player-build inspection of PlayerScriptAssemblies; shader stripper for compute variants; CI grep gate |
| Seam-snap at bake doesn't match Phase 3 runtime expectation | 3 | 4 | 12 | Shared seam-metadata contract authored in Phase 2 schema; SegmentSeamSnapTests mirrors Phase 3 gate exactly (same epsilon) |
| Splines added but accidentally referenced at runtime | 2 | 4 | 8 | Reference com.unity.splines only from Editor asmdef; runtime never imports Unity.Splines; build-report check |
| Baked GI / lightmap refs don't survive Addressable packaging | 3 | 3 | 9 | Validate lightmaps load with the SegmentAsset on device; bake GI into the segment's own scene/prefab packaged with the asset |

Score ≥15 mitigated before start: row 1 (asmdef split is the first step + build-report verification).
