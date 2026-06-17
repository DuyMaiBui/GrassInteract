// WorldRootTransform.hlsl
// SSOT for the WorldPainter root-transform feature. All WorldPainter content
// (terrain, grass, props) is authored in PAINTING space (= the WorldPainter root
// GameObject's LOCAL space). These helpers map painting space → world space using
// the root's full non-uniform TRS, pushed as global matrices by WorldRootBinder.
//
// Gated by _TERRAIN-style keyword `_WP_ROOT_TRANSFORM` (multi_compile): when the
// root is identity the keyword is OFF and every helper is a no-op, so the OFF
// variant is byte-identical (and zero added ALU) versus the pre-feature shader.
//
// Bound by Runtime/WorldRootBinder.cs via Shader.SetGlobalMatrix:
//   _WPLocalToWorld : painting → world point transform   (root.localToWorldMatrix)
//   _WPWorldToLocal : world → painting point transform    (root.worldToLocalMatrix)
//   _WPNormalMatrix : inverse-transpose of the 3x3 (correct normals under scale)

#ifndef WORLDPAINTER_ROOT_TRANSFORM_INCLUDED
#define WORLDPAINTER_ROOT_TRANSFORM_INCLUDED

#ifdef _WP_ROOT_TRANSFORM
float4x4 _WPLocalToWorld;
float4x4 _WPWorldToLocal;
float4x4 _WPNormalMatrix; // 3x3 inverse-transpose packed in 4x4; upper-left used.
#endif

// Painting-space position → world space. No-op when the feature is off.
float3 WP_PaintingToWorldPos(float3 posP)
{
#ifdef _WP_ROOT_TRANSFORM
    return mul(_WPLocalToWorld, float4(posP, 1.0)).xyz;
#else
    return posP;
#endif
}

// World-space position → painting space. Used by in-shader CDLOD morph so the
// morph metric matches the painting-space CDLOD selection (camera in painting space).
float3 WP_WorldToPaintingPos(float3 posW)
{
#ifdef _WP_ROOT_TRANSFORM
    return mul(_WPWorldToLocal, float4(posW, 1.0)).xyz;
#else
    return posW;
#endif
}

// Painting-space normal → world space via the inverse-transpose matrix
// (correct under non-uniform scale). Returns a normalised normal.
float3 WP_PaintingToWorldNormal(float3 nP)
{
#ifdef _WP_ROOT_TRANSFORM
    return normalize(mul((float3x3)_WPNormalMatrix, nP));
#else
    return nP;
#endif
}

// Painting-space tangent/direction → world space. A tangent transforms by the
// localToWorld 3x3 (NOT the inverse-transpose, which is for normals) so the TBN
// basis stays consistent with WP_PaintingToWorldNormal under rotation. Normalised.
float3 WP_PaintingToWorldDir(float3 dirP)
{
#ifdef _WP_ROOT_TRANSFORM
    return normalize(mul((float3x3)_WPLocalToWorld, dirP));
#else
    return dirP;
#endif
}

#endif // WORLDPAINTER_ROOT_TRANSFORM_INCLUDED
