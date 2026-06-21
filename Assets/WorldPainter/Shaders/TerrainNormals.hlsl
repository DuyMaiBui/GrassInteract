// TerrainNormals.hlsl
// Terrain surface normal: two paths controlled by _WP_LIVE_SCULPT keyword.
//
// ── Baked path (DEFAULT, _WP_LIVE_SCULPT OFF) ──────────────────────────────────
//   Phase 5b: one texture fetch from a pre-baked RG8 hemisphere-XZ normal map.
//   Stores (nx, nz) remapped to [0,1]; ny is reconstructed as sqrt(1 - nx² - nz²).
//   NOT octahedral encoding — no oct-fold; relies on Y-up hemisphere constraint.
//   Replaces the 4-tap SampleHeightVTF central-difference with a single bilinear
//   sample of _BakedNormalTex.  Baked by WorldMapBaker.BakeOneTile into normalData[].
//
// ── Live-sculpt path (_WP_LIVE_SCULPT ON) ─────────────────────────────────────
//   Original 4-tap central-difference via SampleHeightVTF — used during:
//     • Editor painting (BeginSculptPreview forces the keyword so normals update live).
//     • Pre-Phase-5 tiles that haven't been re-baked yet (no normalData).
//   GpuTerrainEngine.Build + BeginSculptPreview control the keyword.
//
// SSOT decode: TerrainVtf.hlsl DecodeHeight / TerrainHeightFormat.DecodeHeight (C#).
// TerrainVtf.hlsl must be included before this file.

#ifndef TERRAIN_NORMALS_INCLUDED
#define TERRAIN_NORMALS_INCLUDED

// ── Normal epsilon (1/heightRes) — set from TerrainShadingConfig.DEFAULT_NORMAL_EPSILON ──
float _NormalEpsilon;

// ── Phase 5b: baked normal texture (RG8, hemisphere-XZ, NOT oct-encoded) ──────
// Stores nx and nz in [0,1]; ny reconstructed in shader. See WorldMapBaker.BakeNormalData.
// Bound by GpuTerrainEngine when tile.IsNormalValid; absent on pre-Phase-5 tiles.
// Declared as Texture2D<float4> so the RG channels arrive as [0,1] floats.
// _WP_LIVE_SCULPT OFF path samples this; ON path ignores it.
TEXTURE2D(_BakedNormalTex);
SAMPLER(sampler_BakedNormalTex);

// ─── SampleNormalWS: unified entry point called by TerrainPatch.shader frag ──
// tileUV : [0,1] tile-local UV.
// Returns a normalised world-space normal.
//
// When _WP_LIVE_SCULPT is NOT defined (runtime baked path):
//   One bilinear fetch of _BakedNormalTex; decode RG → (nx, nz); reconstruct ny.
//
// When _WP_LIVE_SCULPT IS defined (live sculpt / fallback path):
//   Original 4-tap central-difference via SampleHeightVTF + _NormalEpsilon / _TileSizeM.
float3 SampleNormalWS(float2 tileUV)
{
#ifdef _WP_LIVE_SCULPT

    // ── Live-sculpt path: 4 SampleHeightVTF taps (original DeriveNormalWS) ──────
    float eps = _NormalEpsilon;

    float hPX = SampleHeightVTF(tileUV + float2( eps,  0.0));
    float hNX = SampleHeightVTF(tileUV + float2(-eps,  0.0));
    float hPZ = SampleHeightVTF(tileUV + float2( 0.0,  eps));
    float hNZ = SampleHeightVTF(tileUV + float2( 0.0, -eps));

    float worldStep = eps * _TileSizeM;
    float invStep2  = 1.0 / (2.0 * worldStep);

    float dhdx = (hPX - hNX) * invStep2;
    float dhdz = (hPZ - hNZ) * invStep2;

    return normalize(float3(-dhdx, 1.0, -dhdz));

#else

    // ── Baked path (Phase 5b): single bilinear fetch ──────────────────────────
    // RG8 hemisphere-XZ encoding (NOT octahedral — no oct-fold):
    //   R = nx * 0.5 + 0.5,  G = nz * 0.5 + 0.5
    // Decode: nx = R*2-1, nz = G*2-1, ny = sqrt(max(0, 1 - nx^2 - nz^2)) (Y-up hemisphere).
    float4 encoded = SAMPLE_TEXTURE2D(_BakedNormalTex, sampler_BakedNormalTex, tileUV);
    float nx = encoded.r * 2.0 - 1.0;
    float nz = encoded.g * 2.0 - 1.0;
    // Clamp before sqrt to guard numerical underflow on steep slopes baked near ±1.
    float ny = sqrt(max(0.0, 1.0 - nx * nx - nz * nz));
    return normalize(float3(nx, ny, nz));

#endif // _WP_LIVE_SCULPT
}

// Phase 5b: keep DeriveNormalWS as an alias for _WP_LIVE_SCULPT callers (backward compat).
// Any call site using DeriveNormalWS(tileUV) in the existing TerrainPatch.shader frag
// should be updated to SampleNormalWS(tileUV) — see TerrainPatch.shader frag comment.
#define DeriveNormalWS(uv) SampleNormalWS(uv)

#endif // TERRAIN_NORMALS_INCLUDED
