# Plan: Mesh Combine + Texture Atlas Baker

Date: 2026-06-16 1531 · Mode: --auto (single-agent) · Source design: `plans/reports/2026-06-16-mesh-atlas-baker-brainstorm.md`

A reusable Unity Editor wizard that takes a selection of meshes + their materials, packs all source textures into one atlas, remaps UV0 into the atlas sub-rects, folds per-material scalar factors into the baked pixels, and combines into one mesh with one URP-Lit material plus a ready-to-drop prefab. Editor-only, library-quality (generic names, no project tokens).

## Scope summary (all decisions locked — no open questions)

- **Placement:** standalone `Assets/MeshAtlas/` package. `Assets/MeshAtlas/Editor/` (runtime-of-tool code + `MeshAtlas.Editor.asmdef`) and `Assets/MeshAtlas/Tests/Editor/` (`MeshAtlas.Tests.asmdef`). Generic names only.
- **Menu:** `Tools/Mesh Atlas/Combine & Bake`.
- **UV path:** 0–1 unique UVs → rect-pack atlas. No re-unwrap, no render-to-texture.
- **Maps:** Albedo, Normal, Metallic/Mask, Emission — one shared atlas layout (computed once from albedo, reused identically across all four channels).
- **Packer:** custom MaxRects, configurable padding, power-of-two output, pure C#, unit-tested.
- **Scalar fold:** multiply `_BaseColor` tint, `_Metallic`, `_Smoothness` per-material into baked pixels per atlas region.
- **Output:** combined mesh (1 submesh) + 4 atlas PNGs + 1 URP-Lit material + prefab.
- **Color space:** albedo + emission sRGB; normal + mask linear.

## Components → phase + file ownership

| Component | Type | Phase | Owns |
|---|---|---|---|
| `AtlasPacker` | pure C# | P1 | `Editor/Packing/AtlasPacker.cs` |
| `UVRemapper` | pure C# | P1 | `Editor/Packing/UVRemapper.cs` |
| `MapBaker` | integration | P2 | `Editor/Baking/MapBaker.cs` |
| `MeshCombiner` | integration | P3 | `Editor/Combine/MeshCombiner.cs` |
| `AtlasAssetWriter` | integration | P3 | `Editor/Output/AtlasAssetWriter.cs` |
| `AtlasBakerWindow` | EditorWindow | P4 | `Editor/UI/AtlasBakerWindow.cs` |

## Phases

- **Phase 1 — Packing core + asmdef scaffold** — `AtlasPacker`, `UVRemapper`, both asmdefs, EditMode tests. Pure C#, no Editor deps. | Effort: M
- **Phase 2 — Map baker** — `MapBaker`: 4-channel blit, scalar fold, per-channel color space, edge-bleed dilation. | Effort: L
- **Phase 3 — Combine + asset emission** — `MeshCombiner` + `AtlasAssetWriter`: combined mesh, 4 PNGs, URP-Lit material, prefab. | Effort: M
- **Phase 4 — Wizard UI + UV guard + e2e bake** — `AtlasBakerWindow`, out-of-0–1 UV guard, end-to-end sample bake. | Effort: M

## Feasibility

- **Reuse check:** NEW package. No existing atlas/packer/combine code in repo (verified — only `Assets/WorldPainter/{Editor,Tests/Editor}` exist, unrelated). asmdef pattern reused from `WorldPainter.Editor.asmdef` / `WorldPainter.Tests.asmdef`.
- **Complexity:** moderate. Pure-C# core (P1) is simple + unit-testable; baking (P2) is the complex node (GPU blit, color space, dilation); P3/P4 are mechanical Editor asset I/O + IMGUI.

## Dependencies

- P1 blocks P2, P3, P4 (asmdefs + `Dictionary<Material,Rect>` contract + UV math consumed downstream).
- P2 blocks P3 (atlas textures feed the material/PNG writer) and P4 (Bake button calls baker).
- P3 blocks P4 (window orchestrates combine + write).
- Critical path: P1 → P2 → P3 → P4 (fully sequential; no parallel-safe phases since each consumes the prior's public types).

## Guards (mandatory — confirmed in design)

1. **Scalar fold into pixels** — P2 multiplies `_BaseColor`/`_Metallic`/`_Smoothness` per region before readback, else differently-tinted materials merge wrong.
2. **Padding + edge-bleed dilation** — P1 reserves padding (default 4px); P2 dilates atlas edges to kill mip/bilinear bleed.
3. **Out-of-0–1 UV detection** — P4 (with helper in P1) detects UV0 outside [0,1], warns + lists offending meshes, SKIPS them. No silent corruption.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|---|---|---|---|
| Normal map re-normalized / wrong color space across rects → broken lighting | 4 | 5 | 20 | Bake normal + mask atlas as **linear** RenderTexture (`RenderTextureReadWrite.Linear`), import baked normal PNG with `TextureImporterType.NormalMap`, never apply sRGB. Dedicated EditMode-adjacent sample bake comparing a known normal texel. HIGH — mitigation mandatory before P2 sign-off. |
| Edge/mip bleed between atlas regions at runtime | 4 | 4 | 16 | Padding gutter (P1, default 4px) + edge-bleed dilation pass (P2). Verify on a 2-material sample bake at distance/mip. HIGH — mitigation mandatory. |
| Scalar-fold color-space error (tint applied in linear vs sRGB) double-darkens albedo | 3 | 4 | 12 | Fold `_BaseColor` in the albedo's gamma space; cover with a sample bake comparing a solid-color material's pre/post baked texel. |
| `Graphics.Blit` / RenderTexture readback unverifiable by EditMode unit test (needs live editor) | 4 | 2 | 8 | Split testable math (packer/remapper → EditMode) from GPU work (P2/P3 → in-editor sample bake). State explicitly in verification. |
| Many high-res sources blow the atlas budget | 3 | 3 | 9 | Expose max atlas size + per-source max in UI (P4); MaxRects fails gracefully → warn + abort, never silent overflow. |
| `Mesh.CombineMeshes` 16-bit index overflow on large selections | 2 | 4 | 8 | Set `mesh.indexFormat = UInt32` in `MeshCombiner` before combine. |
| asmdef-only edit no-op compile masks errors | 3 | 2 | 6 | After asmdef edits, touch a `.cs` to force recompile (`refresh_unity(force, all)` is unreliable on asmdef-only changes). |

**High-risk items (score ≥ 15):** normal-map color space (20), edge/mip bleed (16). Both have mandatory mitigations gating P2 sign-off.

## Timeline

| Phase | Effort | Notes |
|---|---|---|
| P1 Packing core + asmdefs | M | No blocker — start here. Pure C#, EditMode-testable. |
| P2 Map baker | L | Blocked by P1. Highest risk (normal/color/dilation). |
| P3 Combine + asset emission | M | Blocked by P1 + P2. Mechanical Editor I/O. |
| P4 Wizard UI + UV guard + e2e | M | Blocked by P1–P3. Integration + manual verify. |
| **Total** | **M+L+M+M (~L overall)** | Critical path: P1 → P2 → P3 → P4 (fully sequential). |

## Unity verification realities

- **Pure-C# parts (P1: packer, remapper, UV-guard math):** EditMode tests via Test Runner (`run_tests`). Fully automatable, no live render.
- **GPU blit / asset write (P2, P3):** NOT unit-testable in batch — verify by a **sample bake in-editor** (2-material props) and inspect output atlas/material/prefab visually + via `read_console`.
- **End-to-end (P4):** run the wizard on a sample selection, confirm 1 draw call, visual parity (tint/metal/smooth preserved), prefab drops in clean.
- **asmdef gotcha:** `refresh_unity` may no-op on asmdef-only edits. After editing an `.asmdef`, touch any `.cs` in that assembly to force a recompile, then `read_console`.
- **MCP timeout ≠ disconnect:** a timed-out MCP call during bake/recompile means Unity is busy — wait, do not kill/restart the editor.

## Out of scope (v1) — future work

- Runtime dynamic merge (this is Editor-bake-only).
- Tiled-UV render-to-texture path (only 0–1 unique UVs supported).
- LOD generation.
- Mesh decimation.
- UV2 / lightmap handling (UV0 only).

## Phase detail files

- `phase-1-packing-core.md`
- `phase-2-map-baker.md`
- `phase-3-combine-emit.md`
- `phase-4-wizard-ui.md`

---

Cook handoff: `/t1k:cook plans/260616-1531-mesh-atlas-baker/plan.md`
