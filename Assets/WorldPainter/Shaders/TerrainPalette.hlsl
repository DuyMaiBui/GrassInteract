// TerrainPalette.hlsl
// Unity-Terrain-style per-tile alphamap blend across an unlimited (cap 16) TerrainLayer
// palette stored map-wide. Replaces the 4-channel splat path in TerrainSplat.hlsl.
//
// SSOT (C#):
//   - WorldMapAsset.TerrainPalette[]  → palette diffuse textures (max MAX_TERRAIN_LAYERS).
//   - TerrainTileAsset.alphamaps[]    → per-tile RGBA32 textures, one per 4 palette entries.
//   - palette[i].tileSize / .tileOffset → per-layer UV scaling (Unity TerrainLayer fields).
//
// Layout (matches the C# cap of 16 layers):
//   alphamap k holds weights for palette indices [4k .. 4k+3] on RGBA channels.

#ifndef TERRAIN_PALETTE_INCLUDED
#define TERRAIN_PALETTE_INCLUDED

// Must match TerrainShadingConfig.MAX_TERRAIN_LAYERS / MAX_TERRAIN_ALPHAMAPS exactly.
#define TERRAIN_PALETTE_MAX_LAYERS    16
#define TERRAIN_PALETTE_MAX_ALPHAMAPS 4

// ── Palette uniforms (bound by TerrainPaletteBinder + GpuTerrainEngine) ─────
TEXTURE2D_ARRAY(_TerrainPaletteArray);
SAMPLER(sampler_TerrainPaletteArray);

int    _TerrainPaletteCount;
float4 _TerrainPaletteTilings[TERRAIN_PALETTE_MAX_LAYERS]; // .xy = tileSize world m, .zw = tileOffset world m

// ── Per-tile alphamaps (one RGBA32 per 4 palette entries) ──────────────────
Texture2D<float4> _TerrainAlphamap0;
Texture2D<float4> _TerrainAlphamap1;
Texture2D<float4> _TerrainAlphamap2;
Texture2D<float4> _TerrainAlphamap3;
SamplerState      sampler_TerrainAlphamap0;
SamplerState      sampler_TerrainAlphamap1;
SamplerState      sampler_TerrainAlphamap2;
SamplerState      sampler_TerrainAlphamap3;

int _TerrainAlphamapCount;

// ── Fallback (matches TerrainSplat.hlsl behaviour) ─────────────────────────
static const float4 TERRAIN_PALETTE_FALLBACK = float4(0.4, 0.55, 0.3, 1.0);

// Tile-LOCAL UV (0..1 across the tile) → tile-LOCAL alphamap sample.
// Helper: pick alphamap by index k.
float4 SampleAlphamap(int k, float2 tileUV)
{
    if (k == 0) return _TerrainAlphamap0.Sample(sampler_TerrainAlphamap0, tileUV);
    if (k == 1) return _TerrainAlphamap1.Sample(sampler_TerrainAlphamap1, tileUV);
    if (k == 2) return _TerrainAlphamap2.Sample(sampler_TerrainAlphamap2, tileUV);
    return _TerrainAlphamap3.Sample(sampler_TerrainAlphamap3, tileUV);
}

// Pick a single RGBA channel.
float SelectChannel(float4 v, int c)
{
    if (c == 0) return v.r;
    if (c == 1) return v.g;
    if (c == 2) return v.b;
    return v.a;
}

// World-XZ → palette UV for layer i. Uses palette[i].tileSize / tileOffset semantics
// (Unity TerrainLayer convention): UV = (worldXZ + tileOffset) / tileSize.
// The C# binder writes tileSize into .xy and tileOffset into .zw of _TerrainPaletteTilings[i].
// We need world-XZ — the patch shader passes it to BlendTerrainPalette below.
float2 PaletteUV(int i, float2 worldXZ)
{
    float4 t = _TerrainPaletteTilings[i];
    float2 tileSize  = max(t.xy, 1e-3); // guard divide-by-zero on un-authored entries.
    float2 tileOff   = t.zw;
    return (worldXZ + tileOff) / tileSize;
}

// Sum (palette[i].diffuse(layerUV) * alphamap[i/4].channel[i%4]) over the active palette.
// Divides by the total accumulated weight (≈ 1 when alphamaps are well-formed) so the
// result stays in [0,1]. Falls back to TERRAIN_PALETTE_FALLBACK when nothing is bound.
float4 BlendTerrainPalette(float2 tileUV, float2 worldXZ)
{
    int paletteCount  = min(_TerrainPaletteCount,  TERRAIN_PALETTE_MAX_LAYERS);
    int alphamapCount = min(_TerrainAlphamapCount, TERRAIN_PALETTE_MAX_ALPHAMAPS);
    if (paletteCount <= 0 || alphamapCount <= 0) return TERRAIN_PALETTE_FALLBACK;

    float4 totalRGB    = 0;
    float  totalWeight = 0;

    // Sample at most 4 alphamaps once each; reuse across the channel loop below.
    float4 a0 = (alphamapCount > 0) ? _TerrainAlphamap0.Sample(sampler_TerrainAlphamap0, tileUV) : 0;
    float4 a1 = (alphamapCount > 1) ? _TerrainAlphamap1.Sample(sampler_TerrainAlphamap1, tileUV) : 0;
    float4 a2 = (alphamapCount > 2) ? _TerrainAlphamap2.Sample(sampler_TerrainAlphamap2, tileUV) : 0;
    float4 a3 = (alphamapCount > 3) ? _TerrainAlphamap3.Sample(sampler_TerrainAlphamap3, tileUV) : 0;

    [loop]
    for (int i = 0; i < paletteCount; ++i)
    {
        int   alphaIdx = i / 4;
        int   channel  = i % 4;
        float4 alphas  = (alphaIdx == 0) ? a0 : (alphaIdx == 1) ? a1 : (alphaIdx == 2) ? a2 : a3;
        float  w       = SelectChannel(alphas, channel);
        if (w <= 0.0) continue;

        float2 layerUV = PaletteUV(i, worldXZ);
        float4 c       = SAMPLE_TEXTURE2D_ARRAY(
            _TerrainPaletteArray, sampler_TerrainPaletteArray, layerUV, i);
        totalRGB    += c * w;
        totalWeight += w;
    }

    if (totalWeight < 1e-5) return TERRAIN_PALETTE_FALLBACK;
    return float4(totalRGB.rgb / totalWeight, 1.0);
}

#endif // TERRAIN_PALETTE_INCLUDED
