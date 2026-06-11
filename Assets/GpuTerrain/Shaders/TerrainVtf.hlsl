// TerrainVtf.hlsl
// Shared VTF height-sample + CDLOD morph helpers.
// Included by TerrainPatch.shader.
//
// SSOT: TerrainHeightFormat.DecodeHeight (C#):
//   worldY = _MinHeight + (raw / 65535.0) * (_MaxHeight - _MinHeight)
// This file replicates that formula VERBATIM for GPU vertex-stage use.
//
// Toggle:
//   #define TERRAIN_VTF_FALLBACK   → use pre-baked vertex Y (TEXCOORD1) instead of VTF.

#ifndef TERRAIN_VTF_INCLUDED
#define TERRAIN_VTF_INCLUDED

// ─── Uniforms (set per-material by GpuTerrainEngine) ─────────────────────────
float   _MinHeight;         // TerrainTileAsset.minHeight
float   _MaxHeight;         // TerrainTileAsset.maxHeight
Texture2D<float>    _HeightTex;     // R16 / RHalf height texture
SamplerState sampler_HeightTex;     // declared separately (Unity sampler convention)

// ─── Height decode (SSOT — mirrors TerrainHeightFormat.DecodeHeight EXACTLY) ─
// raw is the normalised [0,1] value read from the R16/RHalf texture sample.
float DecodeHeight(float raw)
{
    return _MinHeight + raw * (_MaxHeight - _MinHeight);
}

// ─── VTF height sample from world-UV ─────────────────────────────────────────
// uv: [0,1] within the tile (tile-local, not world UV).
float SampleHeightVTF(float2 uv)
{
#ifdef TERRAIN_VTF_FALLBACK
    // Fallback path: vertex Y already baked as TEXCOORD1.x; caller passes it here.
    // This overload is unused in fallback mode — caller short-circuits.
    return 0.0;
#else
    float raw = _HeightTex.SampleLevel(sampler_HeightTex, uv, 0).r;
    return DecodeHeight(raw);
#endif
}

// ─── CDLOD morph helper ───────────────────────────────────────────────────────
// vertexXZ : vertex position in node-local [0,1] space (from mesh UV).
// nodeScale: world-space node side length (metres).
// lod      : node LOD band (0 = finest).
// morphBlend: [0,1]; 0 = finest grid, 1 = coarser grid (no morph).
//
// Returns morphed node-local XZ snapped toward the 2× coarser grid.
// Crack-free proof: at morphBlend=1 (band edge), position is identical to the
// coarser parent — no T-junction.
float2 MorphVertex(float2 vertexXZ, float morphBlend)
{
    // vertexXZ is node-local UV [0,1] with step = 1/PATCH_RES.
    // Scale to integer-step space: even indices (0,2,4..PATCH_RES) are on the coarser grid
    // and remain unchanged; odd indices snap to the preceding even index.
    // Crack-free at morphBlend=1: odd vertices merge with their coarser neighbour.
    const float patchRes = 16.0; // must match TerrainPatchMesh.PATCH_RES
    float2 vi       = vertexXZ * patchRes;
    float2 fracPart = frac(vi * 0.5) * 2.0; // 0 at even int, 1 at odd int
    return vertexXZ - (fracPart / patchRes) * morphBlend;
}

// ─── Morph blend factor from squared distance ─────────────────────────────────
// sqrDist   : squared distance from camera to vertex (world space).
// morphStart: squared distance at which blend starts (from CdlodNode).
// morphEnd  : squared distance at which blend = 1 (from CdlodNode).
float CalcMorphBlend(float sqrDist, float morphStart, float morphEnd)
{
    if (morphEnd <= morphStart) return 0.0;
    return saturate((sqrDist - morphStart) / (morphEnd - morphStart));
}

#endif // TERRAIN_VTF_INCLUDED
