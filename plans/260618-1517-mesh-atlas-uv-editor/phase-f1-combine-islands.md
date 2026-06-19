# Phase F1 — Combine + auto-separate UV islands (vertical slice, FIRST)

Effort: **L** · Blocked by: nothing · Blocks: F2 (Combine tab hosts this).

## Goal

Drag-drop N meshes → combine → detect connected UV islands → re-pack them into 0..1 with padding so none overlap → rewrite combined UVs → bake source textures → write a NEW prefab (mesh + atlas PNG(s) + URP/Lit material). This is a working vertical slice with NO window chrome yet (driven by a tiny throwaway `EditorWindow` button or a direct pipeline call from a test/menu).

## Why islands ≠ existing per-material rects

The existing `AtlasBakePipeline` packs one rect PER MATERIAL and remaps each submesh's whole UV0 into that rect (`CombineItemBuilder` + `UVRemapper`). F1 packs one rect PER CONNECTED UV ISLAND so geometry sharing a material but living in disjoint UV regions does not overlap after combine. This is a NEW packing axis, so it needs new code on top of the reused packer.

## File ownership

Create:
- `Editor/Islands/UvIsland.cs` — model: `IReadOnlyList<int> TriangleIndices`, `Rect UvBounds` (min/max in UV0 space), submesh index. ≤80 lines.
- `Editor/Islands/UvIslandFinder.cs` — `static List<UvIsland> Find(Mesh mesh)`: union-find over triangles sharing a UV0 vertex index (adjacency = two triangles reference the same vertex index in the mesh's index buffer; treat per-submesh then merge). Pure (UnityEngine math + Mesh read only). ≤150 lines.
- `Editor/Islands/IslandPacker.cs` — `static IslandPackResult Pack(IReadOnlyList<UvIsland> islands, int padding)`: convert each island's UV bounds to a pixel size (proportional to bounds aspect × a base resolution), call `AtlasPacker.Pack`, return the normalized sub-rect per island + atlas size. Reuses `AtlasPacker` verbatim. ≤120 lines.
- `Editor/UVEditorPipeline.cs` — orchestrator for F1: takes meshes (+ their renderers/materials), runs `UvIslandFinder` → `IslandPacker` → rewrites combined-mesh UV0 per island (`UVRemapper.Remap` of each island's local-normalized UV into its packed sub-rect) → builds `BakeInput`s (one per island, source texture = the island's material albedo/normal/etc) → `MapBaker.Bake` → `AtlasAssetWriter.Write`. Returns a `PipelineResult`-shaped object. ≤200 lines.

Test (create):
- `Tests/Editor/UvIslandFinderTests.cs` — two disjoint quads in one mesh/one submesh → 2 islands; one welded quad → 1 island; multi-submesh disjoint → island count == disjoint group count.
- `Tests/Editor/IslandPackerTests.cs` — N island bounds → N sub-rects, all inside 0..1, pairwise non-overlapping (gutter respected), aspect ratio of each sub-rect ≈ aspect of its island bounds.

Edit: none (additive). `AtlasBakePipeline`, `MeshCombiner`, `AtlasPacker`, `MapBaker`, `AtlasAssetWriter` are CALLED, not modified.

## Reuse map

| Need | Reuse |
|------|-------|
| Merge meshes to 1 mesh/1 submesh | `MeshCombiner.Combine` (already UInt32-index safe) |
| Pack rects into PoT atlas + gutters | `AtlasPacker.Pack` (verbatim) |
| Remap a UV range into a sub-rect | `UVRemapper.Remap` (verbatim — F1 has no rotation) |
| Bake source maps into atlas channels | `MapBaker.Bake` + `BakeInput` |
| Write mesh+PNG+material+prefab | `AtlasAssetWriter.Write` |
| Per-material tint/metallic fold | `ScalarFactors.FromMaterial` + `MapBaker` (already folds) |

## Island-find algorithm (the load-bearing new logic)

1. For each submesh, read its triangle index list.
2. Union-find: index each triangle 0..T-1; two triangles are unioned iff they share a vertex INDEX (UV0 is per-vertex, so shared index ⇒ shared UV ⇒ same island). Use a `vertexIndex → first-seen-triangle-root` map to union in O(T·3·α).
3. Group triangles by root → one `UvIsland` per root; compute `UvBounds` = AABB of the island's UV0 coords.
4. Merge step across submeshes is NOT needed for connectivity (different submeshes never share index buffers in `MeshCombiner`'s output) — keep islands per-submesh. Document this assumption inline.

Edge cases (state in code comments, do not silently skip): degenerate triangle (zero-area UV) → still assigned to its island; island with zero UV bounds (all-same UV) → give it a min 1px size in `IslandPacker` (never 0 → packer rejects non-positive).

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Island packing overlaps or distorts aspect | 2 | 4 | 8 | Reuse `AtlasPacker` (gutters every rect); unit-test no-overlap + aspect on a fixture. |
| Union-find merges/splits islands wrongly (shared-vertex assumption false for some meshes) | 3 | 3 | 9 | Unit-test disjoint/welded/multi-submesh fixtures; document the shared-index = same-island invariant inline. |
| Zero-area UV island → packer rejects non-positive size | 2 | 2 | 4 | Clamp island pixel size to ≥1 in `IslandPacker`. |
| New `.cs` not imported → phantom CS0246 | 3 | 2 | 6 | `refresh_unity(force, all)` after writing new files (project memory). |

No score ≥ 15.

## Verify gate

- Unit: `UvIslandFinderTests` + `IslandPackerTests` GREEN (compile = fresh `MeshAtlas.Tests.dll` mtime + 0 console errors; `run_tests` best-effort, total:0 is NOT a pass).
- Manual: combine 3 distinct meshes (each its own material+texture) → open the written prefab in a scene → islands are non-overlapping in the atlas PNG (inspect PNG) AND the prefab renders with correct textures (screenshot). Source assets unchanged on disk.
