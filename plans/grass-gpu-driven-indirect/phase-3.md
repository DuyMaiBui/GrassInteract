# Phase 3 - Compute A: chunk cull kernel (+ isolated harness FIRST)

Effort: M. Depends on: Phase 2. Blocks: Phase 4 (B reads A visible-chunk buffer + DispatchIndirect args).
Goal: a compute kernel that culls CHUNKS against frustum + max distance, appends visible chunk IDs to an AppendStructuredBuffer, and writes the DispatchIndirect args that size Compute B. R2 (counter reset) and R3 (indirect args sizing) are the classic GPU-driven bug sources - isolate them in a tiny harness BEFORE wiring the full pipeline.

## Scope - file ownership

NEW:
- Assets/GrassInteract/Shaders/GrassCull.compute - add the ChunkCull kernel (Compute B blade kernel lands in Phase 4 in the same file).
- Assets/GrassInteract/Editor/GrassCullHarness.cs (editor-only) - the isolation harness: feeds known chunk AABBs, runs ChunkCull, reads back the append count + indirect args, asserts. This is a TEST scaffold, kept editor-only; it is the Phase 3 gate.
- (partial) Assets/GrassInteract/Runtime/GrassGpuEngine.cs - START the engine here ONLY enough to own the cull buffers + dispatch A. Full engine completes in Phase 5. Keep it inert (facade does not select it yet).

UNCHANGED: ChunkedBladeBuffer (provides ChunkAabb + ChunkRange buffers + counts).

## ChunkCull kernel design

Inputs (per dispatch):
- StructuredBuffer<ChunkAabb> chunkAabbs; uint chunkCount;
- float4 frustumPlanes[6]; float3 camPosWS; float maxCullSqrDistance;
Outputs:
- AppendStructuredBuffer<uint> visibleChunks;  // visible chunk IDs
- RWStructuredBuffer<uint> dispatchArgsB;       // [threadGroupsX, 1, 1] for Compute B

Per thread = one chunk:
1. Read ChunkAabb; skip empty sentinel (min>max).
2. Distance reject: nearest-point-of-AABB to camPos sqr distance > maxCullSqrDistance -> reject.
3. Frustum reject: AABB-vs-6-planes (positive-vertex test) -> reject if fully outside any plane.
4. Survivor -> visibleChunks.Append(chunkId).

A SEPARATE tiny kernel (or the CPU after CopyCount) writes dispatchArgsB. Recommended: after ChunkCull, CopyCount(visibleChunks -> a count buffer), then a 1-thread kernel reads the count and writes dispatchArgsB[0] = ceil(count * bladesPerChunkGroupFactor). Decide the exact B grouping in Phase 4; Phase 3 proves the count+args plumbing with a placeholder formula.

## Counter-reset + indirect-args discipline (R2 + R3)

- visibleChunks buffer created with Target.Append | Target.Counter. EACH frame, BEFORE dispatching ChunkCull: visibleChunks.SetCounterValue(0). Never rely on implicit reset.
- CopyCount runs AFTER the ChunkCull dispatch completes (Unity orders GPU work in submission order on a single command buffer - keep them on the same buffer in order).
- dispatchArgsB buffer created with Target.IndirectArguments (+ Raw if needed). Zero-init before A; A writes the real value. Confirm the value is recomputed every frame, not stale.

## Verification gate (harness FIRST - live-editor evidence)

1. set_active_instance GrassInteract FIRST.
2. GrassCullHarness (edit mode): construct N=known chunk AABBs - some inside a synthetic frustum, some outside, some beyond maxDistance, some empty-sentinel. Expected visible = M (hand-counted).
3. Dispatch ChunkCull; CopyCount; GetData the count -> assert == M.
4. GetData visibleChunks[0..M) -> assert the IDs are exactly the expected survivors (set equality).
5. GetData dispatchArgsB -> assert threadGroupsX == ceil(M / groupSize) with the placeholder formula.
6. COUNTER RESET PROOF: run the dispatch TWICE in two consecutive harness ticks with SetCounterValue(0) between - assert the second count == M (not 2M). Then run WITHOUT the reset once to confirm the count doubles (proves the reset is load-bearing), then restore the reset.

Pass = steps 3-6 all hold. Only after the harness passes does the engine wire A into the real pipeline.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Append counter not reset -> doubling counts across frames | 4 | 3 | 12 | SetCounterValue(0) before each dispatch; step-6 proof shows doubling without it. This is the R2 mitigation, validated in isolation here. |
| DispatchIndirect args stale / mis-sized -> B runs 0 or garbage groups | 3 | 3 | 9 | Args buffer Target.IndirectArguments, zero-init, A rewrites every frame; step-5 asserts the value. R3 mitigation. |
| Frustum-plane convention wrong (sign/order) -> culls visible chunks | 3 | 3 | 9 | Extract planes with GeometryUtility.CalculateFrustumPlanes on the CPU, pass as float4 (normal.xyz, distance); positive-vertex test. Harness includes a chunk straddling a plane to catch sign errors. |
| GLES compute group-size limits | 2 | 2 | 4 | Use 64-thread groups (safe on GLES3.1). chunkCount is small (hundreds), one dispatch. |

## Rollback

Delete GrassCullHarness.cs + the ChunkCull kernel; the partial GrassGpuEngine stays inert (facade never selects it). No runtime path touched.
