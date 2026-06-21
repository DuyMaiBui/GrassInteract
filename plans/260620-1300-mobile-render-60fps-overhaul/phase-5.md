# Phase 5 — Native-res quality bundle (L)

**Priority:** P2. **Effort:** L (5 sub-items). **Independent / parallel-safe.**

## Objective

Raise the native-resolution quality ceiling and shave fragment/bandwidth cost so the Phase-1 governor idles at 1.0× more often (less softening). Each sub-item is ~0.5–1.5 fps on the fragment-bound frame — collectively meaningful, individually small. **Honest framing: this bundle is the insurance that lets the governor stay near native, NOT a standalone path to 60.**

## Sub-items

### 5a — LOD2 far-field density falloff

Distance-ramped hash-skip in `GrassCull.compute` `BladeCull`: for blades bucketed into LOD2 (far field), apply an additional distance-ramped skip using the per-blade `hash` (reuse the stable hash from Phase 3) so far overdraw thins out. Keeps near field full. Reuses Phase 3's hash-skip primitive (DRY) — a distance-scaled threshold rather than a flat one. **+2–4 fps far overdraw.**
- **Edit:** `Assets/WorldPainter/Shaders/GrassCull.compute` (the LOD2 append branch).

### 5b — Terrain normal bake (4 height taps → 1 baked normal fetch)

`DeriveNormalWS` in `TerrainNormals.hlsl` currently does **4 `SampleHeightVTF` taps** per fragment (central difference). Bake an RG/octahedral normal texture at the **existing height-bake step** (`WorldMapBaker.BakeOneTile` already clones `heightData`; add a baked normal alongside) and replace the 4 taps with **1 normal fetch**. Keep `DeriveNormalWS` behind a `_WP_LIVE_SCULPT` multi_compile keyword so live terrain sculpting (editor) still derives normals on the fly. **+0.5–1.5 fps terrain-heavy.**
- **Edit:** `Assets/WorldPainter/Shaders/TerrainNormals.hlsl` (add baked-fetch path + keyword gate), `Assets/WorldPainter/Shaders/TerrainPatch.shader` (declare keyword + baked normal sampler; line ~194 `DeriveNormalWS(IN.tileUV)` call site).
- **Edit:** `Assets/WorldPainter/Editor/WorldPainter/WorldMapBaker.cs` (bake the normal map at the height-bake step), `Assets/WorldPainter/Runtime/Terrain/TerrainTileAsset.cs` (store baked normal), `Assets/WorldPainter/Runtime/Terrain/GpuTerrainEngine.cs` (bind baked normal sampler).

### 5c — ASTC + enable MIPS on the 512² RGBA32 alphamaps

The per-tile alphamaps are 512² RGBA32 with **`mipChain:false`** (confirmed in `GpuTerrainEngine` / `TerrainPaletteBinder`). The brainstorm flags **enabling mips matters as much as ASTC** — `mipChain:false` means full-res sampling at all distances = TBDR texture-cache thrash on distant terrain. Enable mips on the alphamaps + ASTC-compress where they are real imported textures.
- **Edit:** `Assets/WorldPainter/Runtime/Terrain/GpuTerrainEngine.cs` (the alphamap `Texture2D` creation → `mipChain: true`).
- **SCOPE WARNING (R3):** the **palette albedo array** is **RGBA32-built-in-code** in `TerrainPaletteBinder.Build` (`const TextureFormat format = TextureFormat.RGBA32; const bool mips = false;` + `Texture2DArray(..., mipChain: false)`). ASTC there is a **binder rewrite** (change the array format to an ASTC `Texture2DArray` + transcode the blit path + `Apply(updateMipmaps: true)`), **NOT an importer flag**. Scope this as **M, not S**. Land the alphamap-mips change first (it is the bigger, cheaper win); treat the palette-array ASTC as a separate, carefully-tested binder change. **+0.5–1.5 fps combined.**

### 5d — Terrain layer-count shader variants

Add `#pragma multi_compile _TERRAIN_LAYERS_4 _TERRAIN_LAYERS_8 _TERRAIN_LAYERS_16` to `TerrainPatch.shader` so a tile with few layers compiles a cheaper palette loop (fewer array samples). Bind the active keyword from `GpuTerrainEngine` based on `TerrainPaletteBinder.ActiveCount`. **Part of the +0.5–1.5 fps combined.**
- **Edit:** `Assets/WorldPainter/Shaders/TerrainPatch.shader` (multi_compile + branch the palette loop), `Assets/WorldPainter/Runtime/Terrain/GpuTerrainEngine.cs` (set the keyword).

### 5e — Frag interpolator strip (grass)

`GrassInteractIndirect.shader` `Varyings` carries `positionWS` (TEXCOORD2), `normalWS` (TEXCOORD3), `tangentWS` (TEXCOORD4) + a TBN reconstruction. Gate the 3 world-space interpolators + TBN behind a keyword/`#if` so the cheap lit path doesn't pay for them. **COMPILE FOOTGUN (R4):** the fragment does `float3 normalWS = normalize(i.normalWS);` at line ~516 — if you strip the `normalWS` interpolator member you MUST also gate that `normalize(i.normalWS)` read under the SAME keyword, or it references a stripped member and the shader fails to compile. Provide a constant-up fallback normal when stripped. **+0.5–1.5 fps.**
- **Edit:** `Assets/WorldPainter/Shaders/GrassInteractIndirect.shader` (gate interpolators 444–446 + the line-516 read + TBN block 519–520 under one keyword).

## File ownership (summary)

- `Assets/WorldPainter/Shaders/GrassCull.compute` (5a)
- `Assets/WorldPainter/Shaders/TerrainNormals.hlsl`, `Assets/WorldPainter/Shaders/TerrainPatch.shader` (5b, 5d)
- `Assets/WorldPainter/Editor/WorldPainter/WorldMapBaker.cs` (5b)
- `Assets/WorldPainter/Runtime/Terrain/TerrainTileAsset.cs` (5b)
- `Assets/WorldPainter/Runtime/Terrain/GpuTerrainEngine.cs` (5b, 5c, 5d)
- `Assets/WorldPainter/Runtime/Terrain/TerrainPaletteBinder.cs` (5c palette-array ASTC — M-scope binder rewrite)
- `Assets/WorldPainter/Shaders/GrassInteractIndirect.shader` (5e)
- **Tests:** extend `Assets/WorldPainter/Tests/Editor/TerrainNormalsTests.cs` (baked-vs-derived normal parity within tolerance) and `Assets/WorldPainter/Tests/Editor/ScatterLodCullTests.cs` (5a far-field skip math).

## Step-by-step tasks

1. 5e first (self-contained shader; gate interpolators + the line-516 read together — verify compile on device).
2. 5a (reuse Phase 3 hash-skip; distance-ramped LOD2 thinning).
3. 5c alphamap mips (the bigger, cheaper win) — flip `mipChain:true` on alphamaps; verify on device.
4. 5d layer-count variants.
5. 5b terrain normal bake (bake step + keyword-gated fetch; keep live-sculpt path).
6. 5c palette-array ASTC (M-scope binder rewrite) LAST — most invasive, carefully A/B tested.

## On-device verification gate (PASS criteria)

- [ ] Each sub-item lands without **visible quality regression** on device (no banding from ASTC palette, no normal seams from baked normals, no missing far grass beyond intent).
- [ ] `GrassInteractIndirect` and `TerrainPatch` **compile all variants on device** (5e keyword gating proven — the `normalize(i.normalWS)` footgun did not fire).
- [ ] Terrain-heavy vista framing FPS improves measurably (governor sits closer to 1.0× than before the bundle) — measured on the Adreno 730.
- [ ] Live terrain **sculpting still works in editor** (5b `_WP_LIVE_SCULPT` keyword path intact).
- [ ] Distant terrain no longer thrashes texture cache (5c mips) — observable as steadier vista frame time on device.
- [ ] `TerrainNormalsTests` baked-vs-derived parity within tolerance (manual Test Runner run if MCP wedged).

## Risk note

R3 (ASTC palette = binder rewrite, score 12) — re-scope to M before starting 5c; land alphamap mips first. R4 (interpolator-strip compile footgun, score 9) — gate the `normalize(i.normalWS)` read under the same keyword as the member; on-device variant compile is the gate. None of these touch grass opacity (guardrail respected).
