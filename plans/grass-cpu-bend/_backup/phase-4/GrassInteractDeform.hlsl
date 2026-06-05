#ifndef GRASSINTERACT_DEFORM_INCLUDED
#define GRASSINTERACT_DEFORM_INCLUDED

// ----------------------------------------------------------------------------------------------------
// GrassInteract per-vertex blade deformation.
//
//   * Ambient wind   : cheap hash/sin sway keyed off the blade pivot XZ, scaled by heightT.
//   * Trample lean   : samples the _GrassTrampleMap VECTOR field at the pivot UV — rg = push direction
//                      (baked away-from-interactor by GrassTrampleMap), b = magnitude — and leans the blade
//                      along it, matting the core straight DOWN via _GrassFlatten. This replaced an older
//                      gradient-lean-away model whose scalar mask left the trampled core upright (zero
//                      gradient at the peak) and overshot the footprint (normalized push, size-independent);
//                      reading a bounded vector directly fixes both.
//
// Root-cause of original zero-bend bug (Phase 0 verdict):
//   Prior code used sampler name 'sampler_linear_clamp' (lowercase). This name is NOT declared
//   anywhere in Unity's URP ShaderLibrary. DX11 silently returns 0 for an undeclared SamplerState.
//   The correct Unity built-in global sampler is 'sampler_LinearClamp' (PascalCase), declared in
//   GlobalSamplers.hlsl. Including that file provides the declaration; no SAMPLER() redeclaration
//   needed (redeclaring would create a second unbound slot -- same silent-zero failure).
// ----------------------------------------------------------------------------------------------------

// Provides sampler_LinearClamp, sampler_PointClamp, sampler_LinearRepeat, etc.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

// (minX, minZ, sizeX, sizeZ) -- bound globally via GrassFieldSpace.BindGlobals().
float4 _GrassFieldRect;

// Ambient wind globals -- bound from GrassLODConfig by GrassInteractField.
float4 _GrassWindDir;        // xy = normalized XZ direction
float  _GrassWindStrength;   // tip sway amplitude (metres)
float  _GrassWindFreq;       // oscillation speed
float  _GrassWindNoiseScale; // spatial phase variation across the field

// Trample map -- top-down ARGBHalf written by GrassTrampleMap: rg = push direction (world XZ),
// b = magnitude; black (0,0,0,0) when no map is bound (zero direction + magnitude = upright).
TEXTURE2D(_GrassTrampleMap);

// Lean globals -- bound via GrassInteractField.BindDeformGlobals(). Keep OUTSIDE UnityPerMaterial so
// they can be set via Shader.SetGlobal*.
float  _GrassBendStrength;        // lateral lean amplitude (metres at full trample magnitude)
float  _GrassFlatten;             // height loss at full trample magnitude (0..1) -- mats the core down

float2 GrassField_WorldToUv(float3 posWS)
{
    return float2(
        (posWS.x - _GrassFieldRect.x) / max(_GrassFieldRect.z, 1e-5),
        (posWS.z - _GrassFieldRect.y) / max(_GrassFieldRect.w, 1e-5));
}

// Per-vertex blade deformation. pivotWS = blade base (never moves); heightT = 0..1 root..tip.
void GrassInteract_ApplyDeform(inout float3 posWS, inout float3 nrmWS, float3 pivotWS, float heightT)
{
    // --- Ambient wind ---
    float spatialPhase = dot(pivotWS.xz, float2(0.37, 0.21)) * _GrassWindNoiseScale * 6.2831853;
    float sway = sin(_Time.y * _GrassWindFreq + spatialPhase);
    float2 windOffset = _GrassWindDir.xy * (sway * _GrassWindStrength * heightT);
    posWS.x += windOffset.x;
    posWS.z += windOffset.y;

    // --- Trample lean (vector field) ---
    float2 uv = GrassField_WorldToUv(pivotWS);
    float4 trample = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_LinearClamp, uv, 0);

    float2 leanDir = trample.rg;          // push direction (world XZ), baked by GrassTrampleMap
    float  mag     = trample.b;           // bounded [0,1], falls to 0 by the footprint edge -> no overshoot
    float  len     = length(leanDir);
    leanDir = (len > 1e-5) ? (leanDir / len) : float2(0, 0);

    float bend = mag * heightT;
    posWS.x += leanDir.x * bend * _GrassBendStrength;   // bounded magnitude -> lean stays within the footprint
    posWS.z += leanDir.y * bend * _GrassBendStrength;
    posWS.y -= (posWS.y - pivotWS.y) * bend * _GrassFlatten;   // core (mag max) mats DOWN -- no upright spike
}

#endif // GRASSINTERACT_DEFORM_INCLUDED
