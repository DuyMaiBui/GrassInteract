# Phase F5 — Bake/write for the edited layout

Effort: **M** · Blocked by: F4 · Blocks: nothing (terminal of the UV-edit path).

## Goal

Bake the F4 `PlacedImage` layout (arbitrary rotation + tint) into the atlas and write a NEW asset. `Graphics.DrawTexture` (used by `MapBaker`) cannot rotate, so F5 adds a GL/material quad blit (`RotatedAtlasBlit`) that places each placed image as a rotated quad whose corners are the `AffineUVRemapper`-mapped source-UV corners. Tint reuses `ScalarFold.FoldAlbedo`; edges reuse `EdgeDilation`; write reuses `AtlasAssetWriter`.

## File ownership

Create:
- `Editor/Baking/RotatedAtlasBlit.cs` — GL-based rotated blit into the active RenderTexture: for one `PlacedImage`, compute the 4 atlas-space quad corners from `AffineUVRemapper.Apply` of the image's UV corners (0,0)(1,0)(1,1)(0,1), map to screen-pixel rect honoring the SAME Y-flip as `MapBaker.ToScreenRect` (screen origin top-left, atlas bottom-left), and `GL.Begin(GL.QUADS)` with the source texture bound (a blit material with `_MainTex`). PURE-where-possible: the corner math (`QuadCorners(AffineUVRemapper, int atlasSize) → Vector2[4]`) is split into a static testable method; only the GL draw touches the GPU. ≤180 lines.
- `Editor/Baking/PlacedImageBaker.cs` — orchestrates an edited-layout bake mirroring `MapBaker` but for `PlacedImage`s: clear RT to channel default → for each placed image `RotatedAtlasBlit.Draw` → ReadPixels → fold tint over the placed region (`ScalarFold.FoldAlbedo`) → `EdgeDilation.Dilate` → return `Texture2D`. Produces an `AtlasWriteResult` via `AtlasAssetWriter.Write`. ≤200 lines.

Test (create):
- `Tests/Editor/RotatedAtlasBlitMathTests.cs` — `QuadCorners`: identity affine → axis-aligned rect == `MapBaker.ToScreenRect`-equivalent corners (Y-flip parity); 90° rotation → corners rotated as expected; the 4 atlas-UV corners equal `AffineUVRemapper.Apply` of the 4 source-UV corners (this is the parity gate that prevents misalignment). No GPU in the test — math only.

Edit:
- `Editor/UI/Tabs/UvEditTab.cs` (F4) — wire the "Bake" button to `PlacedImageBaker`. This is the ONLY cross-phase file edit in the plan; F5 runs strictly after F4.

## Reuse map

| Need | Reuse |
|------|-------|
| Per-image tint fold | `ScalarFold.FoldAlbedo` (verbatim) |
| Edge bleed safety | `EdgeDilation.Dilate` (verbatim) |
| Y-flip / pixel-rect convention | mirror `MapBaker.ToScreenRect` / `ToPixelRect` EXACTLY |
| Write mesh+PNG+material+prefab | `AtlasAssetWriter.Write` |
| Affine UV→atlas mapping | `AffineUVRemapper` (F4) — the shared contract |

## Design notes — the highest-risk seam in the whole plan

The rotated blit MUST place pixels exactly where `AffineUVRemapper` says the UVs go, or the mesh (whose UVs F4/F1 wrote) samples the wrong atlas region → visible misalignment. Defense:
- ONE affine matrix is the source of truth (F4's `AffineUVRemapper`). `RotatedAtlasBlit.QuadCorners` is derived from it, never re-derived from Euler/scale separately.
- `QuadCorners` Y-flip must match `MapBaker.ToScreenRect` (it already documents the top-left-screen vs bottom-left-atlas flip). Cite that comment.
- Unit-test corner parity BEFORE any GPU bake. Then a single manual visual bake confirms GPU == math.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Rotated blit corners ≠ affine UV mapping → texture misaligns on mesh | 4 | 5 | 20 | HIGH. `QuadCorners` derived from the SAME `AffineUVRemapper`; corner-parity unit test is a hard gate; Y-flip mirrors `MapBaker.ToScreenRect`; one paired visual bake before sign-off. |
| GL blit material/shader missing in editor (no `_MainTex` unlit blit) | 3 | 3 | 9 | Use a built-in unlit/`Hidden/Internal-GUITexture`-style material or `GL.sRGBWrite` + textured quad; validate the material loads; fall back to `Blit` material if absent (surfaced, not silent). |
| sRGB/linear mismatch vs `MapBaker` per channel | 3 | 3 | 9 | Mirror `MapBaker`'s `RenderTextureReadWrite` per `BakeChannelInfo.IsLinear`; reuse the same RT setup. |
| Overlapping placed images bake in wrong order | 2 | 3 | 6 | Draw in placement order (last-on-top); warn if assigned islands overlap. |

Score ≥ 15: rotated-blit parity (20) — gated by the corner-parity unit test + paired visual bake BEFORE finalize.

## Verify gate

- Unit: `RotatedAtlasBlitMathTests` GREEN, including the corner==affine-mapping parity assertion (fresh `MeshAtlas.Tests.dll` mtime + 0 console errors; `run_tests` best-effort).
- Manual (paired with the unit test — mandatory for the ≥15 risk): place a rotated + tinted image on an island in F4 → Bake → open the prefab → the texture aligns on the mesh with the correct rotation and tint, no seam/offset. Source assets unchanged.
