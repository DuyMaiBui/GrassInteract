# Phase 2 — 1-D Baked-Segment Model + Segment Asset Schema + Standard-Mesh Renderer; Cut Runtime CDLOD Render Path

**Effort:** L · **Blocks:** 3, 5 · **Blocked by:** 1

## Goal

Define the runtime representation of the world as an ordered chain of fixed-length **baked segments**, each a standard URP MeshRenderer (no compute). Establish the `SegmentAsset` schema that the bake pipeline (Phase 5) emits and the stream window (Phase 3) consumes. Remove the runtime CDLOD render path so the shipping build renders the corridor with standard SRP-batched meshes.

**Provisional default (confirm decision #2):** segment terrain = **baked ribbon mesh** (one MeshRenderer per segment). Alt: per-segment built-in Terrain. Schema below is written for ribbon; a Terrain variant would swap the mesh field for a TerrainData ref.

## File Ownership (real paths)

Create:
- `Assets/WorldPainter/Runtime/Segment/SegmentAsset.cs` — `ScriptableObject` (Addressable). Fields (all `[SerializeField] private` + public getters): segment index, distance-along-track start/length, ribbon `Mesh` ref, terrain `Material` ref, baked `Mesh` for collider (or shared mesh), baked-props payload ref, grass-density map (`Texture2D` R8) + scatter config ref, baked-GI lightmap refs, shared-edge seam metadata (start/end edge vertex ring for seam-snap verification).
- `Assets/WorldPainter/Runtime/Segment/SegmentRenderConfig.cs` — `UPPER_SNAKE_CASE` consts: `SEGMENT_LENGTH_M` (default 100), per-tier segment draw distance, LOD bias. No inline literals.
- `Assets/WorldPainter/Runtime/Segment/SegmentInstance.cs` — runtime MonoBehaviour bound to a live segment: holds the instantiated MeshRenderer + MeshCollider + prop root + grass binding handle. `IObjectPoolManager`-spawned by Phase 3. Lifecycle hooks `OnSpawn`/`OnRecycle`.

Edit / move to editor-only or delete from runtime build (coordination with Phase 5 asmdef split):
- `Assets/WorldPainter/Runtime/Terrain/GpuTerrainEngine.cs` — DELETE from runtime render path (627 lines, RenderMeshIndirect 504/534). Either move to editor-only bake tooling if any sampling logic is reused, or delete. No runtime reference may remain.
- `Assets/WorldPainter/Runtime/Terrain/CdlodQuadtree.cs`, `CdlodNode.cs`, `TerrainPatchMesh.cs`, `TerrainTileLoader.cs`, `TerrainResidencyRing.cs`, `TerrainTileResidencySet.cs`, `TerrainTileGpuResources.cs`, `TerrainNodeBuffer.cs` — CUT from runtime build (open-world LOD machinery, no corridor payoff). Move to editor bake tooling only if a function is reused; otherwise delete.
- `Assets/WorldPainter/Runtime/Terrain/TerrainStreamingManager.cs` — CUT (2-D ring); the streaming CONCEPT (hysteresis, per-frame budget from `TerrainStreamingConfig.cs`) is reborn 1-D in Phase 3, not this 2-D code.
- `Assets/WorldPainter/Shaders/TerrainPatch.shader` — retain ONLY if it is the standard mesh material; if it depends on VTF/structured buffers (compute), author a standard ES3.0-safe terrain material instead.

Keep (used by segment renderer):
- `Assets/WorldPainter/Runtime/Terrain/HeightmapSurfaceSampler.cs`, `TerrainHeightSampleCpu.cs`, `TerrainHeightFormat.cs` — if reused for collider/ground sampling at bake or runtime ground-snap.

## Concrete Steps

1. Author `SegmentAsset` schema + `SegmentRenderConfig` consts. Decide seam metadata shape (edge vertex index ring) so Phase 3/5 can verify seam-snap.
2. Author `SegmentInstance` MonoBehaviour with pool lifecycle. MeshRenderer + MeshCollider assembled from `SegmentAsset` on spawn.
3. Delete/relocate the CDLOD runtime render path. Grep the runtime asmdef for any remaining `RenderMeshIndirect` (terrain) and `CdlodQuadtree`/`TerrainResidencyRing` references; runtime must have zero.
4. Remove now-orphaned EditMode tests that test deleted runtime code, OR move them under the editor bake-tooling tests if the code moved (coordinate with Phase 5). Affected tests: `CdlodQuadtreeTests`, `CdlodMorphMathTests`, `TerrainResidencyRingTests`, `TerrainResidencyDiffTests`, `TerrainTileLoaderTests`, `TerrainTileResidencySetTests`, `TerrainPatchMeshSkirtTests`, `GpuTerrainEngineUvBindingTests`, `TerrainNodeBufferTests`.
5. Wire one baked `SegmentAsset` (hand-authored for now; Phase 5 automates) and render it as a standard MeshRenderer in the test scene.

## Verification

- **Compile:** `read_console` clean; runtime asmdef compiles with NO reference to deleted CDLOD/GpuTerrain types.
- `run_tests` EditMode green after test cleanup; no test references a deleted runtime type.
- **Grep gate:** runtime path (`Assets/WorldPainter/Runtime/`) contains no `RenderMeshIndirect` for terrain and no `CdlodQuadtree`/`TerrainStreamingManager` instantiation.
- **On-device (GLES3.0):** one baked SegmentAsset renders as a standard mesh, visible, correct material, no pink.

## Success Criteria

- `SegmentAsset` + `SegmentInstance` + `SegmentRenderConfig` exist and one hand-baked segment renders standard-mesh on GLES3.0.
- Runtime CDLOD render path fully removed from the runtime build (grep-verified).
- EditMode suite green; orphaned CDLOD-runtime tests removed or relocated.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Deleting CDLOD code breaks compile via hidden runtime references | 4 | 4 | 16 | Grep all references before delete; compile after each removal batch (per ai-velocity-batch-compile-unity: blind-implement → verify once → fix all) |
| Schema churn forces Phase 3/5 rework | 3 | 4 | 12 | Lock SegmentAsset schema in this phase with Phase 3 + 5 owners reviewing before they start; version the asset |
| Standard mesh loses CDLOD's distance LOD on long ribbons | 2 | 3 | 6 | Bake 2-3 LODs per segment ribbon into an LODGroup at bake time (Phase 5); per-tier draw distance in SegmentRenderConfig |
| TerrainPatch.shader depends on compute/VTF | 3 | 3 | 9 | Author a standard ES3.0 terrain material; do not reuse a compute-dependent shader for runtime |

Score ≥15 mitigated before start: row 1 (grep + incremental compile gate).
