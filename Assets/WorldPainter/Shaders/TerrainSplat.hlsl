// TerrainSplat.hlsl
// 4-layer splat-weight sample and texture-array blend.
//
// SSOT: TerrainTileAsset (C#) — RGBA→layer channel mapping:
//   R = layer 0 (base ground)
//   G = layer 1 (grass overlay)
//   B = layer 2 (rock / cliff)
//   A = layer 3 (path / road)
// Weights stored as RGBA8 [0,255]; normalised to [0,1] by Unity's sampler.
// Phase 2 normalises the 4 weights so they always sum to 1.

#ifndef TERRAIN_SPLAT_INCLUDED
#define TERRAIN_SPLAT_INCLUDED

// ─── Uniforms (set by material — never MPB, RenderGraph drops it) ─────────────
// Splat-weight RGBA texture (one weight per channel, one texel per tile UV).
Texture2D<float4>   _SplatTex;
SamplerState        sampler_SplatTex;

// Layer albedo array: up to MAX_SPLAT_LAYERS=4 slices.
TEXTURE2D_ARRAY(_LayerAlbedoArray);
SAMPLER(sampler_LayerAlbedoArray);

// Layer UV tiling (world-units per repeat) — from TerrainShadingConfig.DEFAULT_LAYER_TILING.
float _LayerTiling;

// ─── Sample splat weights (normalised) ───────────────────────────────────────
// tileUV : [0,1] within the tile.
// Returns four normalised weights corresponding to layers 0..3.
// SSOT channel→layer: r=0 g=1 b=2 a=3  (TerrainTileAsset).
float4 SampleSplatWeights(float2 tileUV)
{
    float4 raw = _SplatTex.Sample(sampler_SplatTex, tileUV);

    // Normalise so weights sum to 1.  Guard against zero-sum (bare tile = full layer 0).
    float total = raw.r + raw.g + raw.b + raw.a;
    if (total < 1e-5)
        raw = float4(1, 0, 0, 0);
    else
        raw /= total;

    return raw;
}

// ─── Blend 4 array slices by splat weights ────────────────────────────────────
// tileUV  : [0,1] tile UV — multiplied by _LayerTiling for texture tiling.
// weights : normalised splat weights (from SampleSplatWeights).
// Returns blended RGBA colour.
float4 BlendLayerArray(float2 tileUV, float4 weights)
{
    float2 layerUV = tileUV * _LayerTiling;

    // Single array sample path — keeps sampler count low (mobile budget).
    float4 c0 = SAMPLE_TEXTURE2D_ARRAY(_LayerAlbedoArray, sampler_LayerAlbedoArray, layerUV, 0);
    float4 c1 = SAMPLE_TEXTURE2D_ARRAY(_LayerAlbedoArray, sampler_LayerAlbedoArray, layerUV, 1);
    float4 c2 = SAMPLE_TEXTURE2D_ARRAY(_LayerAlbedoArray, sampler_LayerAlbedoArray, layerUV, 2);
    float4 c3 = SAMPLE_TEXTURE2D_ARRAY(_LayerAlbedoArray, sampler_LayerAlbedoArray, layerUV, 3);

    return c0 * weights.r + c1 * weights.g + c2 * weights.b + c3 * weights.a;
}

// ─── Convenience: sample + blend in one call ──────────────────────────────────
float4 SplatBlend(float2 tileUV)
{
    float4 weights = SampleSplatWeights(tileUV);
    return BlendLayerArray(tileUV, weights);
}

#endif // TERRAIN_SPLAT_INCLUDED
