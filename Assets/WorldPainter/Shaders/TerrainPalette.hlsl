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
// Phase 5d: layer-count multi_compile variants — compile-time layer cap per variant so the
// shader compiler can prove how many alphamap samples are needed and eliminate dead branches.
// GpuTerrainEngine.Build sets the keyword to match the tile's actual layer count.
// The fallback (no keyword set) defaults to the maximum 16-layer path (safe, never crashes).
#if defined(_TERRAIN_LAYERS_4)
#define TERRAIN_PALETTE_MAX_LAYERS    4
#define TERRAIN_PALETTE_MAX_ALPHAMAPS 1
#elif defined(_TERRAIN_LAYERS_8)
#define TERRAIN_PALETTE_MAX_LAYERS    8
#define TERRAIN_PALETTE_MAX_ALPHAMAPS 2
#else
// _TERRAIN_LAYERS_16 or no keyword: full 16-layer path.
#define TERRAIN_PALETTE_MAX_LAYERS    16
#define TERRAIN_PALETTE_MAX_ALPHAMAPS 4
#endif

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

// ── Stochastic anti-tiling (2-tap rotated, smooth-index) ───────────────────
// Breaks the visible world-grid repeat by cross-fading, per pixel, between two
// hash-transformed (rotation + offset) variants of the layer texture. The
// blend factor comes from a SMOOTH low-frequency procedural index field, so
// the result is C0-continuous everywhere — at each integer handoff the incoming
// variant equals the outgoing one (variant ia(l+ε) == ib(l-ε)). This is a true
// 2-tap method (no cell-edge seams, unlike a discrete cell-grid blend which
// needs 4 taps in 2D). Gated by _TERRAIN_STOCHASTIC (multi_compile) so it is
// entirely compiled out — zero cost — when disabled.
// Reference: Inigo Quilez "Texture Repetition" (Technique 3, two fetches).
#ifdef _TERRAIN_STOCHASTIC

static const float STOCHASTIC_TWO_PI     = 6.28318530718;
// Index-field frequency in UV units (lower = larger, gentler variation regions).
static const float STOCHASTIC_INDEX_FREQ = 0.25;
// Smooth index sweeps l = k*VARIANTS over [0, VARIANTS]; floor/floor+1 give the
// two variant indices, so the distinct-variant count is VARIANTS+1 at the endpoints.
static const float STOCHASTIC_VARIANTS   = 8.0;
// Decorrelating UV offset magnitude per variant (UV units).
static const float STOCHASTIC_OFFSET_MAG = 32.0;
// Contrast-restore strength: biases the cross-fade by luminance delta so the
// blend band stays narrow → less variance/contrast loss (iq's trick).
// LOAD-BEARING INVARIANT: MUST stay < the smoothstep half-band (0.2) below.
// w(handoff) = smoothstep(0.2,0.8, f - CONTRAST*d) with |d|≤1; if CONTRAST≥0.2
// the f→0/f→1 plateaus no longer pin w to 0/1 at the variant swap → seams return.
static const float STOCHASTIC_CONTRAST   = 0.1;

// 2D value hash → [0,1)^2 (Dave Hoskins hash22). No texture dependency.
float2 Hash22(float2 p)
{
    float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

// 1D hash → [0,1). Per-variant RNG.
float Hash11(float v)
{
    return frac(sin(v * 127.1) * 43758.5453123);
}

// Smooth low-frequency scalar in [0,1] — value noise (procedural, no texture).
// C1-continuous (cubic-smoothstep bilerp of 4 corner hashes).
float StochasticIndexField(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash22(i).x;
    float b = Hash22(i + float2(1.0, 0.0)).x;
    float c = Hash22(i + float2(0.0, 1.0)).x;
    float d = Hash22(i + float2(1.0, 1.0)).x;
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Sample palette layer i with variant `v`'s hashed rotation + offset applied.
// dUVdx/dUVdy are gradients of the CONTINUOUS base UV, rotated by the same
// matrix so mip selection stays correct under the per-variant rotation.
float4 SampleStochasticVariant(int i, float v, float2 uv, float2 dUVdx, float2 dUVdy)
{
    float r1  = Hash11(v);
    float r2  = Hash11(v + 17.0);
    float ang = r1 * STOCHASTIC_TWO_PI;            // random per-variant rotation
    float s, c;
    sincos(ang, s, c);
    float2x2 R = float2x2(c, -s, s, c);

    float2 off = float2(r1, r2) * STOCHASTIC_OFFSET_MAG; // decorrelating offset
    float2 ruv = mul(R, uv) + off;
    float2 rdx = mul(R, dUVdx);
    float2 rdy = mul(R, dUVdy);

    return SAMPLE_TEXTURE2D_ARRAY_GRAD(
        _TerrainPaletteArray, sampler_TerrainPaletteArray, ruv, i, rdx, rdy);
}

// 2-tap smooth-index stochastic sample of palette layer i. Cross-fades two
// adjacent variants chosen by a smooth index → seamless, no cell-edge artifacts.
float4 SampleLayerStochastic(int i, float2 uv, float2 dUVdx, float2 dUVdy)
{
    float k  = StochasticIndexField(uv * STOCHASTIC_INDEX_FREQ);
    float l  = k * STOCHASTIC_VARIANTS;
    float ia = floor(l);
    float ib = ia + 1.0;
    float f  = frac(l);

    float4 ca = SampleStochasticVariant(i, ia, uv, dUVdx, dUVdy);
    float4 cb = SampleStochasticVariant(i, ib, uv, dUVdx, dUVdy);

    // Contrast-preserving cross-fade (iq): narrow the transition band by the
    // luminance delta. smoothstep(0.2,0.8,...) keeps a pure-variant plateau at
    // each end so the ia/ib handoff at integer l is continuous.
    float d = dot(ca.rgb - cb.rgb, float3(0.3333, 0.3333, 0.3333));
    float w = smoothstep(0.2, 0.8, f - STOCHASTIC_CONTRAST * d);
    return lerp(ca, cb, w);
}

#endif // _TERRAIN_STOCHASTIC

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

        float2 layerUV = PaletteUV(i, worldXZ);
#ifdef _TERRAIN_STOCHASTIC
        // Gradients of the continuous base UV. MUST be computed here, in uniform
        // control flow BEFORE the zero-weight `continue` — quad derivatives are
        // undefined under divergent flow. The GRAD sample below then runs safely
        // in divergent flow because it consumes these explicit gradients.
        float2 dUVdx = ddx(layerUV);
        float2 dUVdy = ddy(layerUV);
#endif
        if (w <= 0.0) continue;

#ifdef _TERRAIN_STOCHASTIC
        float4 c = SampleLayerStochastic(i, layerUV, dUVdx, dUVdy);
#else
        float4 c = SAMPLE_TEXTURE2D_ARRAY(
            _TerrainPaletteArray, sampler_TerrainPaletteArray, layerUV, i);
#endif
        totalRGB    += c * w;
        totalWeight += w;
    }

    if (totalWeight < 1e-5) return TERRAIN_PALETTE_FALLBACK;
    return float4(totalRGB.rgb / totalWeight, 1.0);
}

#endif // TERRAIN_PALETTE_INCLUDED
