# Phase 4 - Compute B: per-blade cull + LOD bucketing + indirect draw args

Effort: M. Depends on: Phase 3. Blocks: Phase 5 (consumes per-LOD visible-index buffers + RenderMeshIndirect args).
Goal: for every blade in the visible chunks, do a per-blade frustum + distance test, bucket survivors into one of 3 LODs by distance, append the blade index into that LOD visible-index AppendBuffer, then CopyCount each LOD into RenderMeshIndirect args.

## Scope - file ownership

NEW (additions):
- Assets/GrassInteract/Shaders/GrassCull.compute - add the BladeCull kernel (dispatched via DispatchIndirect using Phase 3 dispatchArgsB).
- Assets/GrassInteract/Runtime/GrassGpuEngine.cs - extend: own the 3 per-LOD visible-index AppendBuffers, the 3 RenderMeshIndirect args buffers, and the CopyCount wiring. Still inert (facade selects it in Phase 7).

UNCHANGED: ChunkedBladeBuffer (BladeInstance + ChunkRange), Phase 3 visibleChunks + dispatchArgsB.

## BladeCull kernel design

Dispatched indirectly with dispatchArgsB (one thread group per visible-chunk batch; thread = one blade slot).
Inputs:
- StructuredBuffer<uint> visibleChunks; StructuredBuffer<ChunkRange> chunkRanges; StructuredBuffer<BladeInstance> blades;
- float4 frustumPlanes[6]; float3 camPosWS; float2/float3 lodMaxSqrDistances (LOD0/LOD1 thresholds; beyond LOD1 -> LOD2);
- float lod2SkipSqrDistance (optional far skip when lodCount allows).
Per blade:
1. Map (groupId, threadId) -> a visible chunk + a blade index within its ChunkRange. Bounds-check against count (last group is partial).
2. Read BladeInstance.posWS. Per-blade frustum test (point-or-small-sphere vs 6 planes) -> reject if outside.
3. sqrDist = distance(posWS, camPos)^2. Bucket: <= lod0Max -> LOD0; <= lod1Max -> LOD1; else -> LOD2 (or skip if beyond lod2Skip and lodCount<3 or skip-enabled).
4. Append the GLOBAL blade index into visibleLod[bucket].

Reuse the SAME LOD thresholds GrassRenderer/GrassLODConfig.LodMaxDistances already define (squared on the CPU, passed in) so high-tier LOD distances match the documented config.

## Per-LOD indirect draw args (CopyCount)

- Three AppendStructuredBuffer<uint> visibleLod0/1/2 (Target.Append | Target.Counter). SetCounterValue(0) before BladeCull every frame.
- Three GraphicsBuffer args (Target.IndirectArguments), one per LOD, laid out for RenderMeshIndirect (GraphicsBuffer.IndirectDrawIndexedArgs: indexCountPerInstance, instanceCount, startIndex, baseVertex, startInstance).
- After BladeCull: for each LOD, CopyCount(visibleLodN -> argsN at the instanceCount field). The mesh index count etc. are set once at bake from the LOD mesh.

## Counter-reset + ordering discipline (R2)

Order on a single command buffer each frame: SetCounterValue(0) x3 (LOD buffers) + reset visibleChunks + zero dispatchArgsB -> ChunkCull (A) -> CopyCount visibleChunks + write dispatchArgsB -> BladeCull (B, DispatchIndirect) -> CopyCount x3 into the draw args. Never reorder CopyCount before its producing dispatch.

## Verification gate (live-editor evidence)

1. set_active_instance GrassInteract FIRST.
2. Extend the Phase 3 harness (or a Play-mode debug readback): with a known camera pose over the baked demo field, dispatch A then B, GetData the 3 LOD counts.
   - Sum of the 3 LOD counts == total blades passing frustum+distance for that pose (cross-check against a CPU brute-force count of in-frustum-in-range blades; allow exact match - same thresholds).
   - Near camera -> LOD0 count dominates; pull the camera back -> counts shift toward LOD1/LOD2 monotonically.
3. GetData the 3 args buffers -> the instanceCount field of each equals its LOD count (CopyCount landed in the right slot).
4. Two consecutive frames with the same pose -> identical counts (no counter leak).

Pass = sum-match + monotonic LOD shift + args==count + frame-stable.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| group/thread -> blade mapping off-by-one on partial last chunk -> dropped/duplicated blades | 3 | 3 | 9 | Explicit bounds check threadIndexInChunk < ChunkRange.count; harness asserts sum == CPU brute-force count (catches both drop and dup). |
| CopyCount into the wrong args field offset -> instanceCount=0, nothing draws | 2 | 4 | 8 | Use GraphicsBuffer.CopyCount with the documented dst offset for instanceCount (IndirectDrawIndexedArgs layout); step-3 GetData asserts the value landed. |
| LOD threshold mismatch vs CPU path -> different LOD distribution | 2 | 2 | 4 | Pass LodMaxDistances squared from GrassLODConfig (same source the CPU GrassRenderer uses). |
| Three Append buffers each need their own counter reset -> easy to miss one | 3 | 3 | 9 | Centralize the per-frame reset list in GrassGpuEngine; step-4 frame-stability check catches a missed reset (count would grow). |

## Rollback

Remove the BladeCull kernel + the per-LOD buffer/args wiring from GrassGpuEngine. Phase 3 harness + A kernel remain; engine still inert.
