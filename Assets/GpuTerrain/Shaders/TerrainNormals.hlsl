// TerrainNormals.hlsl
// Heightmap-derived surface normal via central-difference on VTF height samples.
//
// SSOT decode: TerrainVtf.hlsl DecodeHeight / TerrainHeightFormat.DecodeHeight (C#):
//   worldY = _MinHeight + raw * (_MaxHeight - _MinHeight)
// This file uses DecodeHeight from TerrainVtf.hlsl — include TerrainVtf BEFORE this file.
//
// Phase 0 guarantees a 1-texel border overlap on all tile edges so neighbour
// samples at ±epsilon never fall outside the tile texture footprint.
//
// epsilon (UV space): 1/heightRes, supplied as _NormalEpsilon (TerrainShadingConfig).

#ifndef TERRAIN_NORMALS_INCLUDED
#define TERRAIN_NORMALS_INCLUDED

// TerrainVtf.hlsl must be included before this file (provides DecodeHeight + _HeightTex).

// ── Normal epsilon (1/heightRes) — set from TerrainShadingConfig.DEFAULT_NORMAL_EPSILON ──
float _NormalEpsilon;

// ── Tile size in world units — driven by the per-material uniform _TileSizeM.
// Declared in TerrainVtf.hlsl (included before this file) so no redeclaration needed.

// ─── Derive world-space surface normal from height neighbours ─────────────────
// tileUV : [0,1] tile-local UV.
// Returns a normalised world-space normal pointing roughly upward.
//
// Central difference:
//   dhdx = (h(u + eps, v) - h(u - eps, v)) / (2 * eps * TILE_SIZE_M)
//   dhdz = (h(u, v + eps) - h(u, v - eps)) / (2 * eps * TILE_SIZE_M)
//   normal = normalize(float3(-dhdx, 1, -dhdz))
//
// The 1/TILE_SIZE_M factor converts UV-space delta to world-space gradient.
float3 DeriveNormalWS(float2 tileUV)
{
    float eps = _NormalEpsilon;

    // Sample the four cardinal neighbours (all within the Phase 0 skirt).
    float hPX = SampleHeightVTF(tileUV + float2( eps,  0.0));
    float hNX = SampleHeightVTF(tileUV + float2(-eps,  0.0));
    float hPZ = SampleHeightVTF(tileUV + float2( 0.0,  eps));
    float hNZ = SampleHeightVTF(tileUV + float2( 0.0, -eps));

    // World-space UV step size (epsilon * _TileSizeM in metres).
    float worldStep = eps * _TileSizeM;
    float invStep2  = 1.0 / (2.0 * worldStep);

    float dhdx = (hPX - hNX) * invStep2;
    float dhdz = (hPZ - hNZ) * invStep2;

    // Cross-product of tangent vectors (1,dhdx,0) × (0,dhdz,1) → normal (-dhdx, 1, -dhdz).
    return normalize(float3(-dhdx, 1.0, -dhdz));
}

#endif // TERRAIN_NORMALS_INCLUDED
