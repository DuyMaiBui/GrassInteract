# Code Review: Phase F1 — Combine + Islands (Interactive UV Editor)

Adversarial review, evidence-first. Branch `feat/meshatlas-uv-editor`, uncommitted working tree.

## Verdict: BLOCK

F1's headline acceptance criterion — "combine of N meshes each with its own material+textures → the prefab renders with CORRECT textures" — is **not met for the multi-material case**, which is the entire reason F1 exists. The pipeline bakes EVERY island from a single source material. A 3-mesh/3-material combine produces an atlas containing only ONE material's textures. Island geometry and packing are correct; texture attribution is broken at the root.

---

## Critical (must fix — BLOCKER)

### C1 — Every island is baked from material[submesh 0]; all other source materials are silently dropped
**Files:** `Editor/UVEditorPipeline.cs:84-87, 134, 164-205`; `Editor/Combine/MeshCombiner.cs:34,38`; `Editor/Islands/UvIslandFinder.cs:33`

End-to-end trace (this is the flagged risk, confirmed true):

1. `UVEditorPipeline.BuildIdentityItems` (line 105-127) builds one `CombineItem` per (renderer, submesh) — distinct submeshes survive as separate items, but **all carry `SubRect = (0,0,1,1)`**.
2. `MeshCombiner.Combine` (line 34) sets **every** `CombineInstance.subMeshIndex = 0` and calls `CombineMeshes(mergeSubMeshes: true)` (line 38). **The combined mesh has exactly ONE submesh.** (Confirmed by the type doc on `MeshCombiner`: "merges everything into one mesh / one submesh".)
3. `UvIslandFinder.Find` (line 33) loops `subMesh = 0 .. subMeshCount-1`. Because `subMeshCount == 1`, **every `UvIsland` is constructed with `subMesh = 0`** (`BuildIsland` → `new UvIsland(triangleList, bounds, subMesh)`, line 166). So `island.SubMeshIndex == 0` for all islands.
4. `BuildBakeInputs` (line 192-193) resolves the material via `materialBySubmesh.TryGetValue(island.SubMeshIndex, out var mat)` = `TryGetValue(0, ...)` for **every** island → all islands receive the SAME material.
5. `BuildMaterialBySubmesh` (line 165-183) keys by the **source renderer's local submesh index** `s` with first-writer-wins (`if (!map.ContainsKey(s) ...)`). For 3 meshes each with 1 material (submesh 0), the map is `{ 0: firstRenderer.Materials[0] }`. meshB's and meshC's materials are **never inserted** and never surfaced as a warning.

**Net effect:** the atlas is baked entirely from `renderers[0].Materials[0]`'s Albedo/Normal/Mask/Emission. Each island's UVs ARE remapped to a distinct, non-overlapping packed rect (C-correct), but `MapBaker.DrawTexture` (`MapBaker.cs:49`) draws material A's source texture into every one of those rects. The prefab renders every island with material A's texture — silently wrong, no error, no warning. This violates "Errors Over Silent Fallbacks" on top of being functionally wrong.

This is worse than per-material attribution being merely lost: `BuildMaterialBySubmesh`'s submesh-index key is meaningless after the combine flattening, because the island's `SubMeshIndex` no longer references the source renderer it came from. Even a multi-submesh single mesh (e.g. one FBX, 3 materials) would partly work by accident only if every island happened to inherit distinct submesh indices — which it cannot, because the combine collapses them all to 0.

**Why the existing pipeline does NOT have this bug (the contrast the brief asked for):**
`AtlasBakePipeline` (`AtlasBakePipeline.cs:77-83`) builds `rectByMaterial` keyed by the actual `Material` object and `CombineItemBuilder.Build` (`CombineItemBuilder.cs:39-52`) attributes each source submesh to ITS material's rect **before** the merge, baking each material's UV0 into the correct sub-rect via per-item `SubRect`. The per-material → rect association is established pre-combine and carried into the bake. F1 inverted the order (combine first, then find islands on the flattened mesh) and thereby destroyed every island's link back to its source material. Answer to brief Q2: **yes, combine-first-then-find-islands destroys the per-island→source-material association.**

**Fix direction (recommended):** find islands per SOURCE submesh BEFORE combining, carrying the source `Material` (not a submesh int) on each `UvIsland`. Concretely:
- Run `UvIslandFinder.Find` on each source `(mesh, submesh)` slice individually, tagging each produced island with its source `Material` reference (and the source `SubRect`/transform needed to place it).
- Pack all islands globally (`IslandPacker` is fine as-is).
- Bake each island from ITS tagged material's textures into its packed rect.
- Remap each island's UVs in the combined mesh into its packed rect — but this now requires tracking which combined-mesh vertices belong to which source island (the combine remaps/reindexes vertices, so the current "find on combined mesh" vertex identity is convenient; moving island-find pre-combine means you must thread island→combined-vertex mapping through `MeshCombiner`, OR have `MeshCombiner` emit a per-item submesh and find islands per-submesh on the combined mesh).
- Simplest correct variant: make `MeshCombiner` emit one submesh PER CombineItem (drop `mergeSubMeshes: true`, keep per-instance `subMeshIndex = i`) for the F1 path, and build `materialByCombinedSubmesh` keyed by the combine-item index → its source material. Then `UvIslandFinder`'s existing per-submesh loop yields islands whose `SubMeshIndex` correctly indexes into that map. This keeps "find on combined mesh" (no vertex re-threading) while restoring correct attribution. Note `AtlasAssetWriter`/prefab still bind a single material, which is correct since all channels share one atlas — only the BAKE source must be per-island.

Either way the multi-material bake MUST be covered by a test before this is unblocked (see I3).

---

## Important (fix before merge)

### I1 — Silent drop of non-zero-submesh materials violates errors-over-fallbacks
**File:** `Editor/UVEditorPipeline.cs:174-180`
`BuildMaterialBySubmesh` first-writer-wins per submesh index, and `BuildBakeInputs` (line 192) does nothing when `TryGetValue` misses (`mat` stays null → all-default textures). A renderer whose materials never make it into the map produces islands baked from defaults with **no warning**. `result.Warnings` exists and is logged by the dev menu (`UVEditorDevMenu.cs:44-47`) but is never populated here. Contrast `CombineItemBuilder.cs:42-44` which explicitly warns on a dropped submesh. Whatever the C1 fix, unmatched islands must emit a warning, not bake silently.

### I2 — `materialBySubmesh` is dead/misleading machinery
**File:** `Editor/UVEditorPipeline.cs:86, 164-183`
Given C1, this dictionary can only ever hold key 0 in the common single-material-per-mesh case and is keyed on a dimension (`island.SubMeshIndex`) that is constant after combine. It is not just buggy — it reads as if it works, which is exactly the "plausible-but-wrong slice" hazard. Remove or replace as part of the C1 fix; do not leave it as-is.

### I3 — Zero test coverage for the multi-material bake correctness (the one thing F1 must get right)
**Files:** `Tests/Editor/UvIslandFinderTests.cs`, `Tests/Editor/IslandPackerTests.cs`
The two test files are reasonable for what they cover (island count, UV bounds, no-overlap, aspect, degenerate, count-match — these genuinely assert the invariants, not shallow). But **nothing** exercises `UVEditorPipeline` end-to-end, and **nothing** asserts that island i is baked from material i's texture. The single highest-risk behavior in the phase is untested, which is why C1 shipped. Add a pipeline-level test (build 2 in-code meshes with 2 distinguishable solid-color materials, run `UVEditorPipeline.Run`, read back the atlas PNG, assert each island's packed rect contains its OWN material's color). The spec's "Verify gate" (phase-f1 line 60-61) calls for exactly this manual check; automate the core of it.

---

## Minor / Suggestions

- **M1 — `UvIslandFinder.GroupByRoot` returns `object` and always `null`** (`UvIslandFinder.cs:111-133`), with the caller writing `_ = islandTris;` (line 44) to discard it. Dead return type; make it `void` and drop the `_ =` line. Code smell, not a bug.
- **M2 — `UVEditorPipeline.DEFAULT_SOURCE_SIZE` (line 29) is unused.** Remove. (`AtlasBakePipeline` uses its own copy legitimately.)
- **M3 — `IslandPacker.IslandToPixelSize` mixes constants** — `BASE_RESOLUTION = 512` (line 38) while `MapBaker`/atlas default and `AtlasBakePipeline.DEFAULT_SOURCE_SIZE = 256`. Islands are sized to 512 on their max dimension regardless of the source texture's actual resolution, so a 64×64 source island is upscaled to 512 in the atlas (wasted atlas space + blurry). Consider deriving base resolution from the source texture size (as `AtlasBakePipeline.SourceSize` does) once C1 gives each island a material/texture. Quality, not correctness.
- **M4 — `UvIslandFinder.BuildIsland` guards `vi < uv.Length` (line 148) but not vertices added with no UV.** If `mesh.uv` is shorter than vertex count (mesh without UV0), those verts are skipped and bounds may collapse to the origin-fallback (line 160-162). Acceptable given `RendererCollector` filters out-of-range UVs, but a mesh with NO uv0 at all is not filtered and would yield a degenerate island silently. Edge case, low likelihood for atlas inputs.
- **M5 — `MapBaker.ToScreenRect` Y-flip is annotated "verify in-editor" (`MapBaker.cs:11-14, 147`).** Not new to F1 (reused backend), but since F1's manual verify gate requires a visual texture check, confirm orientation during the same in-editor pass.

## Convention check (code-conventions-unity.md)
- `this.` prefix: COMPLIANT in instance methods (`MapBaker`); the new F1 files are all `static` so member access is N/A — no violation.
- Private fields camelCase no-underscore: COMPLIANT.
- Constants UPPER_SNAKE_CASE: COMPLIANT (`DEFAULT_PADDING`, `BASE_RESOLUTION`, etc.).
- File size ≤200 lines: COMPLIANT (`UVEditorPipeline.cs` = 227 lines including license/doc/braces but ~200 code; `UvIslandFinder.cs` 169, others well under).
- No magic numbers: COMPLIANT (padding/dilation/resolution are named constants).

## Edge cases
- Degenerate (zero-area) island UV bounds: HANDLED — `RewriteIslandUVs` guards div-by-zero (`UVEditorPipeline.cs:144-145`, `invW/invH = 0` when width/height ≤ 0), `IslandPacker` clamps to ≥1px (`IslandPacker.cs:94-103`), and there is a test (`IslandPackerTests.Pack_DegenerateZeroSizeIsland`). Good. Answer to secondary Q on degenerate bounds: no div-by-zero.
- UV renormalization `(uv - bounds.min)/bounds.size`: CORRECT and applied per island vertex (`UVEditorPipeline.cs:154-156`), then `UVRemapper.Remap` into the packed rect. Logic is sound.
- UInt32 index meshes: HANDLED — `MeshCombiner` emits `IndexFormat.UInt32` (`MeshCombiner.cs:37`); `UvIslandFinder` reads via `mesh.GetTriangles(submesh)` which returns `int[]` regardless of index format, and union-find indices are triangle ordinals not vertex values, so no overflow concern. No off-by-one found in triangle iteration (`base3 = t*3`, `k in 0..2` — correct).
- Union-find shared-vertex-index invariant: CORRECT for the combined-mesh case (welded vertices share an index ⇒ same island). Path compression and union are standard and correct.

## Regression check (additive-only)
CONFIRMED additive. `git diff --stat origin/main...HEAD` and `git log origin/main..HEAD` are EMPTY — the branch has no commits ahead of origin/main; all F1 files are untracked (`??`) in the working tree. No existing tracked source file was modified by F1. (The deleted textures/FBX and modified `WorldPainterDemo.unity` / `WorldMap.asset` in `git status` are unrelated working-tree changes — test-scene asset moves into `Assets/WorldPainter/Demo/`, not part of F1's code and not committed. Flag to the user: those deletions/moves are uncommitted and unreviewed here.)

## Summary
| # | Sev | File:line | Issue |
|---|-----|-----------|-------|
| C1 | BLOCKER | UVEditorPipeline.cs:84-87,164-205 + MeshCombiner.cs:34,38 | All islands baked from material[submesh 0]; other materials silently dropped — multi-material atlas is wrong |
| I1 | Important | UVEditorPipeline.cs:174-192 | Unmatched-material islands bake defaults with no warning (errors-over-fallbacks) |
| I2 | Important | UVEditorPipeline.cs:86,164-183 | `materialBySubmesh` is misleading dead machinery post-combine |
| I3 | Important | Tests/Editor/* | No test asserts island i ← material i; the core risk is uncovered |
| M1 | Minor | UvIslandFinder.cs:111-133 | `GroupByRoot` returns always-null `object` |
| M2 | Minor | UVEditorPipeline.cs:29 | unused `DEFAULT_SOURCE_SIZE` |
| M3 | Minor | IslandPacker.cs:38 | fixed 512 base resolution ignores source texture size (quality) |
| M4 | Minor | UvIslandFinder.cs:148 | mesh with no UV0 → silent degenerate island |
| M5 | Minor | MapBaker.cs:147 | Y-flip "verify in-editor" — confirm during manual gate |

### Score: 4/10
Island detection, packing, UV rewrite, and edge-case handling are solid and well-tested. But the phase's single defining feature — correct per-source-material texture baking for a multi-material combine — is broken at the root and silently produces a wrong-but-plausible result, with no test guarding it. That is a BLOCK, not an APPROVE-WITH-FOLLOWUPS.
