// BrushMask.hlsl — shared per-texel weight for all brush payload kernels.
// Include this file in any compute kernel that needs a brush-shaped weight mask.
//
// Per-texel weight = falloffLUT(normDist) × strength × sign
// (optional stamp texture is a P6 extension — not wired here)
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

// ── UV helpers ────────────────────────────────────────────────────────────────

/// Convert integer dispatch thread ID to texel UV centre.
float2 TexelToUV_BM(uint2 id, int rtRes)
{
    return (float2(id) + 0.5f) / float(rtRes);
}

// ── Legacy inline falloff (used if LUT path is disabled) ─────────────────────
// Cubic smoothstep on (1-t): stays near 1 in centre, drops to 0 at edge.
// This MUST match TerrainBrushMathTests.BrushFalloff (the CPU reference).

float BrushFalloffInline(float2 texelUV, float2 centerUV, float radiusUV)
{
    float dist = length(texelUV - centerUV);
    float t    = saturate(dist / max(radiusUV, 0.0001f));
    float inv  = 1.0f - t;
    return inv * inv * (3.0f - 2.0f * inv);
}

// ── LUT-driven falloff ────────────────────────────────────────────────────────

float BrushFalloffLUT(float2 texelUV, float2 centerUV, float radiusUV)
{
    float dist    = length(texelUV - centerUV);
    float normDist = saturate(dist / max(radiusUV, 0.0001f));
    // Sample LUT: U = normDist mapped to [0..255]/256, V = 0.
    // Use Load to avoid sampler state requirements across all platforms.
    int lutX = (int)clamp(normDist * 255.0f, 0.0f, 255.0f);
    return _FalloffLUT.Load(int3(lutX, 0, 0));
}

// ── Primary entry: compute per-texel brush weight ─────────────────────────────
//
// Returns weight in [0,1]. Returns 0 when texel is outside the brush radius.
// Callers multiply by _Strength and apply sign themselves so the SAME weight
// works for raise (+), lower (-), smooth (blend), and splat.

float BrushWeight(float2 texelUV, float2 centerUV, float radiusUV, bool useLUT)
{
    if (useLUT)
        return BrushFalloffLUT(texelUV, centerUV, radiusUV);
    else
        return BrushFalloffInline(texelUV, centerUV, radiusUV);
}

#endif // BRUSH_MASK_HLSL
