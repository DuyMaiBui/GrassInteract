// BrushMask.hlsl — shared per-texel weight for all brush payload kernels.
// Include this file in any compute kernel that needs a brush-shaped weight mask.
//
// Per-texel weight = falloffLUT(normDist) × optional imported mask × strength × sign
//
// Usage:
//   #include "BrushMask.hlsl"
//   float weight = BrushWeight(texelUV);
//   ... apply weight to your payload ...
//
// Requires the following uniforms to be set by the caller:
//   float2 _BrushCenterUV  — brush center in tile UV [0,1]
//   float  _BrushRadiusUV  — brush radius in UV space (radius_m / TILE_SIZE_M)
//   float  _Strength        — stroke strength [0,1]
//   int    _RTRes           — RT resolution (texels per edge)
//   Texture2D<float> _FalloffLUT — 256×1 RFloat LUT sampled by normalized distance

#ifndef BRUSH_MASK_HLSL
#define BRUSH_MASK_HLSL

// ── LUT parameter (set by BrushLutUploader before dispatch) ──────────────────

/// Falloff LUT: 256×1 RFloat, X = normalised distance from centre [0..1], Y = weight [0..1].
/// When the LUT is absent or the kernel is in legacy mode, use the inline fallback below.
Texture2D<float> _FalloffLUT;

/// Brush footprint shape: 0 = Circle (Euclidean), 1 = Square (Chebyshev). Set every dispatch.
int _BrushShape;

/// Optional imported brush mask: a grayscale texture sampled across the brush footprint.
/// When _UseBrushMask=1 the per-texel weight is multiplied by the mask's red channel, so an
/// imported texture shapes the brush. Bound (a dummy white texture when absent) by BrushMaskBinder.
Texture2D<float4> _BrushMask;
int _UseBrushMask;

// ── UV helpers ────────────────────────────────────────────────────────────────

/// Convert integer dispatch thread ID to texel UV centre.
float2 TexelToUV_BM(uint2 id, int rtRes)
{
    return (float2(id) + 0.5f) / float(rtRes);
}

// Per-texel normalized-distance metric. Circle = Euclidean length; Square = Chebyshev
// (max of |du.x|,|du.y|), which yields square iso-contours through the SAME falloff curve.
float BrushDist_BM(float2 du)
{
    if (_BrushShape == 1)
        return max(abs(du.x), abs(du.y)); // Chebyshev → square
    return length(du);                    // Euclidean → circle
}

// ── Legacy inline falloff (used if LUT path is disabled) ─────────────────────
// Cubic smoothstep on (1-t): stays near 1 in centre, drops to 0 at edge.
// This MUST match TerrainBrushMathTests.BrushFalloff (the CPU reference).

float BrushFalloffInline(float2 texelUV, float2 centerUV, float radiusUV)
{
    float dist = BrushDist_BM(texelUV - centerUV);
    float t    = saturate(dist / max(radiusUV, 0.0001f));
    float inv  = 1.0f - t;
    return inv * inv * (3.0f - 2.0f * inv);
}

// ── LUT-driven falloff ────────────────────────────────────────────────────────

float BrushFalloffLUT(float2 texelUV, float2 centerUV, float radiusUV)
{
    float dist    = BrushDist_BM(texelUV - centerUV);
    float normDist = saturate(dist / max(radiusUV, 0.0001f));
    // Sample LUT: U = normDist mapped to [0..255]/256, V = 0.
    // Use Load to avoid sampler state requirements across all platforms.
    int lutX = (int)clamp(normDist * 255.0f, 0.0f, 255.0f);
    return _FalloffLUT.Load(int3(lutX, 0, 0));
}

// ── Optional imported brush-mask texture ─────────────────────────────────────
// Maps the texel's offset within the brush AABB (centre ± radius) to mask UV [0,1] and returns
// the mask's red channel (0 outside the footprint). Uses Load (no sampler state), so the mask
// must be an uncompressed, point-loadable texture — enforced on import by the brush dock.

float BrushMaskSample(float2 texelUV, float2 centerUV, float radiusUV)
{
    float2 maskUV = (texelUV - centerUV) / max(2.0f * radiusUV, 0.0001f) + 0.5f;
    if (maskUV.x < 0.0f || maskUV.x > 1.0f || maskUV.y < 0.0f || maskUV.y > 1.0f)
        return 0.0f;

    uint w, h;
    _BrushMask.GetDimensions(w, h);
    int mx = (int)clamp(maskUV.x * (float)(w - 1), 0.0f, (float)(w - 1));
    int my = (int)clamp(maskUV.y * (float)(h - 1), 0.0f, (float)(h - 1));
    return _BrushMask.Load(int3(mx, my, 0)).r;
}

// ── Primary entry: compute per-texel brush weight ─────────────────────────────
//
// Returns weight in [0,1]. Returns 0 when texel is outside the brush radius.
// Callers multiply by _Strength and apply sign themselves so the SAME weight
// works for raise (+), lower (-), smooth (blend), and splat.

float BrushWeight(float2 texelUV, float2 centerUV, float radiusUV, bool useLUT)
{
    float weight = useLUT
        ? BrushFalloffLUT(texelUV, centerUV, radiusUV)
        : BrushFalloffInline(texelUV, centerUV, radiusUV);

    if (_UseBrushMask != 0)
        weight *= BrushMaskSample(texelUV, centerUV, radiusUV);

    return weight;
}

#endif // BRUSH_MASK_HLSL
