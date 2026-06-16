# Phase 2 — Map baker

Effort: L · Blocks: P3, P4 · Blocked by: P1 (consumes `Dictionary<…,Rect>` layout)

## Goal

Implement `MapBaker`: for a given atlas layout (the SAME `Dictionary` from `AtlasPacker`, computed once from albedo and reused identically across all four channels), blit each material's source maps into the four atlases via `Graphics.Blit`, fold per-material scalar factors into the pixels, read back to `Texture2D` with correct per-channel color space, and run an edge-bleed dilation pass. This is the highest-risk phase — the two ≥15 risks (normal-map color space, edge/mip bleed) gate sign-off here.

## File ownership (exact paths)

- `Assets/MeshAtlas/Editor/Baking/MapBaker.cs` (orchestrates 4-channel bake)
- `Assets/MeshAtlas/Editor/Baking/ScalarFold.cs` (computes folded tint/metallic/smoothness per material → blit params)
- `Assets/MeshAtlas/Editor/Baking/EdgeDilation.cs` (post-blit dilation pass)
- `Assets/MeshAtlas/Editor/Baking/BakeChannel.cs` (enum: Albedo, Normal, MaskMetallicSmoothness, Emission)

## Implementation notes

- **One layout, four channels:** layout is passed in from P1's packer result; `MapBaker` iterates the four `BakeChannel`s using identical sub-rects so maps stay registered.
- **Per-channel color space (mandatory):**
  - Albedo, Emission → `RenderTexture` with `RenderTextureReadWrite.sRGB`; readback `Texture2D` created with `linear: false`.
  - Normal, Mask → `RenderTextureReadWrite.Linear`; readback `Texture2D(..., linear: true)`. Never apply sRGB to these.
- **Scalar fold (`ScalarFold`):** multiply `_BaseColor` into albedo pixels (in the albedo's gamma space — fold before/consistent with sRGB write to avoid double-darkening), `_Metallic` into the mask's metallic channel, `_Smoothness` into the mask's smoothness channel. Read factors from each `Material` via `GetColor`/`GetFloat` with safe defaults when a property is absent (tint=white, metallic=0, smoothness=0.5 — defaults documented inline, not silent-wrong).
- **Blit:** for each material region, `Graphics.Blit(sourceMap, atlasRT, scaleOffset)` into the sub-rect (use a blit material/`Material` that applies the scalar fold, or `Graphics.Blit` with a `Vector4` scale-bias + a small fold shader). When a material lacks a channel's source map, write the folded flat value (e.g. flat normal `(0.5,0.5,1)`, black emission) into that region — never leave garbage.
- **Edge dilation (`EdgeDilation`):** after all regions are blitted, run a dilation that bleeds region edge colors outward into the padding gutter so bilinear/mip sampling never reads a neighbor region. Default radius ≈ padding (4px).
- Returns four `Texture2D` (one per enabled channel) + the layout, handed to P3.

## Success criteria

- Four atlases produced from a 2-material sample, all using identical sub-rects (maps registered).
- A solid-color material's baked albedo texel equals its `_BaseColor × source` (scalar fold proven).
- Baked normal atlas remains linear and a known source normal texel survives round-trip unchanged (no sRGB corruption).
- Padding gutter contains dilated edge color, not neighbor-region color.

## Verification step

NOT unit-testable in batch (GPU blit + RenderTexture readback need a live editor). Verify by an **in-editor sample bake**:
1. Build a 2-material test selection (one tinted, one with a normal map).
2. Run `MapBaker` on it (via a temporary menu item or the P4 window once available).
3. Inspect output `Texture2D`s: tint folded, normal linear, gutter dilated. Use `read_console` for errors.
4. Where feasible, an EditMode test can assert on `ScalarFold` math (pure C#) and on `EdgeDilation` over a hand-built `Color[]` buffer (pure C#) — split the math out so those two are unit-covered even though the blit isn't.

> MCP timeout during bake ≠ disconnect — Unity is busy; wait, do not restart the editor.

## Rollback

Delete `Assets/MeshAtlas/Editor/Baking/`. P1 unaffected; P3/P4 not yet built. No cascade.
