// GpuGrassSpace.hlsl
// Standalone replacement for WorldPainter's WorldRootTransform.hlsl.
//
// GPUGrass renders grass on a Unity built-in Terrain, which lives directly in
// WORLD space — there is no painting↔world root transform. So every space helper
// is an IDENTITY passthrough. The function NAMES match the WorldPainter originals
// (WP_PaintingToWorld*) so the ported indirect shader needs no body edits; they
// simply return their argument unchanged.
//
// Because there is no root transform, the `_WP_ROOT_TRANSFORM` keyword and its
// global matrices are intentionally absent — these helpers compile to zero ALU.

#ifndef GPUGRASS_SPACE_INCLUDED
#define GPUGRASS_SPACE_INCLUDED

// Painting-space position → world space. Identity (terrain grass is world-space).
float3 WP_PaintingToWorldPos(float3 posP)    { return posP; }

// World-space position → painting space. Identity.
float3 WP_WorldToPaintingPos(float3 posW)    { return posW; }

// Painting-space normal → world space. Identity.
float3 WP_PaintingToWorldNormal(float3 nP)   { return nP; }

// Painting-space tangent/direction → world space. Identity.
float3 WP_PaintingToWorldDir(float3 dirP)    { return dirP; }

#endif // GPUGRASS_SPACE_INCLUDED
