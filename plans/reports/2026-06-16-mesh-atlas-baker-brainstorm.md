# Brainstorm — Mesh Combine + Texture Atlas Baker

Date: 2026-06-16 · Mode: brainstorm (design approved, no implementation)

## Problem
Reusable Editor tool: take a selection of meshes + their materials, pack all source
textures into one atlas, remap UVs into the atlas sub-rects, combine into one mesh with
one material. Goal: draw-call reduction / material consolidation, lossless.

## Requirements (confirmed)
- Form: **EditorWindow wizard**, general reusable asset-pipeline tool (saved to library).
- UV path: **0–1 unique UVs** → rect-pack atlas (no re-unwrap, no render-to-texture).
- Maps: **Albedo, Normal, Metallic/Mask, Emission** — all four, one shared layout.
- Packer: **Custom MaxRects** (padding + POT + unit tests).
- Scalars: **Fold per-material `_BaseColor`/`_Metallic`/`_Smoothness` factors into pixels.**
- Output: mesh + 4 atlases + 1 material + **ready-to-drop prefab**.

## Pipeline
1. Collect — gather meshes + per-renderer material set + 4 source maps each.
2. Pack — MaxRects rect-pack from albedo → `Dictionary<Material,Rect>` (0–1 sub-rects).
3. Bake — blit each material's 4 maps into the 4 atlases using the SAME layout;
   fold scalar factors into pixels; correct sRGB(albedo/emission) vs linear(normal/mask).
4. Remap — `uv_new = rect.pos + uv_old * rect.size` per mesh (UV0).
5. Combine — `Mesh.CombineMeshes` → 1 mesh, 1 submesh.
6. Emit — write CombinedMesh.asset, 4 atlas PNGs, 1 URP-Lit material, 1 prefab.

Invariant: atlas layout computed once (from albedo), reused identically across all 4
channels so maps stay registered.

## Architecture (Editor-only, library package)
| Component | Responsibility | Testable |
|---|---|---|
| `AtlasBakerWindow` | EditorWindow UI: selection, atlas size, padding, channel toggles, output, Bake + preview | integration |
| `AtlasPacker` | MaxRects → sub-rects, padding, POT | **unit (pure C#)** |
| `MapBaker` | RenderTexture + Graphics.Blit per channel, scalar fold, readback, color-space | integration |
| `UVRemapper` | sub-rect UV math | **unit (pure C#)** |
| `MeshCombiner` | CombineMeshes, single material | integration |
| `AtlasAssetWriter` | save textures/mesh/material/prefab, wire URP Lit shader | integration |

Package: `Packages/<lib>/Editor/MeshAtlas/` + `MeshAtlas.Editor.asmdef`.
Menu: `Tools/Library/Mesh Atlas/Combine & Bake`. Generic names (no project tokens).

## Correctness guards
1. **Scalar factors folded into pixels** — else differently-tinted materials merge wrong.
2. **Padding + edge-bleed dilation** (default 4px + dilate) — kills mip/bilinear bleed.
3. **Out-of-0–1 UV guard** — detect, warn + list offending meshes, skip them (no silent corruption).

## Out of scope (v1 / future modules)
Runtime dynamic merge · tiled-UV render-to-texture path · LOD generation · mesh decimation.

## Risks
- Atlas resolution budget: many high-res sources → pick atlas size or downscale per-rect;
  expose max atlas size + per-source max in UI.
- Normal-map packing must stay linear and not be re-normalized incorrectly across rects.
- Lightmap/UV2 not handled in v1 (UV0 only) — note in UI.

## Success criteria
- Bake of N props → 1 draw call, visually identical (tint/metal/smooth preserved).
- `AtlasPacker` + `UVRemapper` EditMode tests green.
- Prefab drops in as a working replacement.

## Next step
Optional `/t1k:plan` to phase: (P1) packer+remapper+tests, (P2) map baker+scalar fold,
(P3) combine+asset/prefab writer, (P4) wizard UI + UV guard.
