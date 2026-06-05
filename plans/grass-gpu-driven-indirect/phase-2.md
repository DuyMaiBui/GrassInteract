# Phase 2 - ChunkedBladeBuffer baker

Effort: M. Depends on: Phase 1. Blocks: Phase 3, 4, 5 (they consume the baked buffers).
Goal: turn the flat scatter output into GPU-ready chunked buffers: a compact BladeInstance array sorted by grid cell, one AABB per chunk, and a chunk->blade-range table. Uploaded ONCE to GraphicsBuffers; the per-frame GPU pipeline never re-touches this.

## Scope - file ownership

NEW:
- Assets/GrassInteract/Runtime/ChunkedBladeBuffer.cs - the baker + the GraphicsBuffer holder (owns BladeInstance buffer, ChunkAABB buffer, chunk-range buffer; bake from a GrassScatterResult; Dispose releases buffers).
- (struct) BladeInstance + (struct) ChunkAabb + (struct) ChunkRange - define in ChunkedBladeBuffer.cs (small related types may share a file per code-conventions). Must be blittable for GraphicsBuffer.SetData with allowUnsafeCode:false.

MODIFIED:
- Assets/GrassInteract/Runtime/GrassLODConfig.cs - ADD serialized fields chunkSize (default 16) and lodCount (default 3) + their public getters. Additive only.

UNCHANGED: GrassScatter (provides BaseSlabs + BasePositionSlabs + SlabCounts + TotalCount + WorldBounds), GrassFieldSpace.

## Data structures

    struct BladeInstance { float3 posWS; uint packedYawScale; uint hash; }   // 20 B, blittable
      - posWS: blade base world position (from GrassScatterResult.BasePositionSlabs).
      - packedYawScale: yaw (0..360 -> e.g. 16-bit) + uniform scale (16-bit half/fixed) packed into one uint.
        Decompose the base matrix the SAME way GrassBendSimulator does: m.rotation (yaw about Y) + m.lossyScale.x.
      - hash: per-blade wind phase hash (reuse the GrassBendSimulator phase formula: (p.x*0.37 + p.z*0.21) * windNoiseScale, baked to a uint hash) so the GPU VS reproduces the SAME sway.

    struct ChunkAabb { float3 min; float3 max; }   // 24 B - one per grid cell
    struct ChunkRange { uint start; uint count; }   // 8 B - contiguous slice into BladeInstance[]

## Baking algorithm

1. Read GrassScatterResult: iterate every blade (flatten the slabs to a flat list of (posWS, baseYaw, baseScale, phase)).
2. Grid = field XZ bounds / chunkSize -> gridX * gridZ cells. Assign each blade to cell index = floor((pos.xz - fieldMinXZ) / chunkSize). Use GrassFieldSpace minXZ for the origin so it matches placement exactly.
3. Counting sort blades by cell: produces BladeInstance[] sorted so each cell is a contiguous range. Record ChunkRange{start,count} per cell.
4. Per cell, compute ChunkAabb as the union of its blades positions expanded by (maxScale*MaxBladeHeight + BendHeadroom) in Y and (maxScale + BendHeadroom) laterally - reuse the SAME headroom logic GrassScatter.BuildFieldBounds uses so no blade is wrongly culled.
5. Empty cells: ChunkRange{start=0,count=0}; ChunkAabb collapsed (min>max sentinel) so Compute A trivially rejects them.
6. Upload: GraphicsBuffer(Target.Structured) for BladeInstance[], ChunkAabb[], ChunkRange[]. SetData once. Store gridX, gridZ, chunkSize, totalChunks, totalBlades.

## Edit-mode bakeability

- Bake is a pure function of GrassScatterResult - callable in edit mode (the high tier engine will bake on Build, same trigger as the CPU engine). No Play-mode dependency.
- Expose an editor assert/dump: log total BladeInstance count, total chunk count, and a min/max occupancy so the verification gate can read the table.

## Verification gate (live-editor evidence)

1. set_active_instance GrassInteract FIRST.
2. In edit mode, trigger a bake (a temporary editor menu item or a debug call) on the demo field.
3. execute_code / console assert:
   - BladeInstance count == GrassScatterResult.TotalCount (every blade placed, none dropped).
   - Sum of all ChunkRange.count == TotalCount AND the ranges are contiguous, non-overlapping, covering [0, TotalCount) (no gap/overlap - editor assert dumps any violation).
   - Union of all non-empty ChunkAabb contains the GrassScatterResult.WorldBounds XZ extent (no blade outside any chunk AABB).
4. Vary chunkSize (e.g. 8 vs 16 vs 32) -> chunk count changes as gridX*gridZ; blade count invariant.

Pass = counts + partition + AABB-cover asserts all hold for at least two chunkSize values.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Yaw/scale packing loses precision -> visible blade pose drift vs CPU path | 2 | 3 | 6 | 16-bit yaw (360/65536 deg step ~0.005deg) + 16-bit scale over the ScaleRange is sub-pixel. Verify in Phase 5 against a CPU reference frame; widen to 2 uints if a diff is visible. |
| Chunk origin mismatch vs GrassFieldSpace -> blades in wrong cell, cull artifacts | 2 | 3 | 6 | Use GrassFieldSpace minXZ as the grid origin (the SAME rect placement keys off). Assert each blade cell index is in-range. |
| Non-blittable struct -> GraphicsBuffer.SetData fails under allowUnsafeCode:false | 2 | 3 | 6 | Use only float3 (Vector3)/uint fields; no managed refs, no bool. Confirm with a compile + a 1-element round-trip SetData/GetData in the harness. |
| AABB headroom diverges from render bounds -> false culls | 2 | 3 | 6 | Reuse the exact headroom constants from GrassScatter.BuildFieldBounds (MaxBladeHeight*maxScale + BendHeadroom). |

## Rollback

Delete ChunkedBladeBuffer.cs; revert the two additive GrassLODConfig fields. Nothing else references the baker until Phase 3.
