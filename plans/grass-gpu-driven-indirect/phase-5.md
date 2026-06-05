# Phase 5 - Indirect shader + vertex-shader GPU deform (3 LODs)

Effort: L (largest phase). Depends on: Phase 4. Blocks: Phase 6 (deform reads interactor buffer), Phase 8 (edit-mode parity needs a live indirect draw).
Goal: a RenderMeshIndirect variant of the grass shader whose vertex shader reconstructs each blade transform from BladeInstance via the per-LOD visible-index buffer, applies wind (sin-by-hash) + lean-away-about-base deform GPU-side, and draws 3 LOD meshes via the Phase 4 indirect args. After this phase the high tier renders fully GPU-driven.

## Scope - file ownership

NEW:
- Assets/GrassInteract/Shaders/GrassInteractIndirect.shader - indirect variant. Separate file from the CPU GrassInteractInstanced.shader (keeps the low tier shader byte-for-byte intact). Includes UniversalForward + ShadowCaster + DepthOnly passes mirroring the CPU shader, but vertex-shader-driven from StructuredBuffers instead of unity_ObjectToWorld.

MODIFIED:
- Assets/GrassInteract/Runtime/GrassGpuEngine.cs - complete the engine: Build (bake + buffers), Step (no per-frame CPU deform - GPU does it; Step uploads interactors in Phase 6), Submit (run the cull command buffer + 3 RenderMeshIndirect calls with the visible-index buffers + args + bounds bound as material/global buffers). Implements IGrassEngine.

UNCHANGED: ChunkedBladeBuffer, GrassCull.compute, GrassLODConfig (LOD meshes), GrassInteractInstanced.shader (CPU tier - DO NOT touch).

## Vertex-shader deform (the core)

Bound buffers (per draw / global): StructuredBuffer<BladeInstance> blades; StructuredBuffer<uint> visibleLodN (the LOD being drawn); plus deform globals (time, windDir, windStrength, windFrequency, bendStrength, flatten, recovery is N/A on GPU since there is no per-frame persisted lean - see note). Interactor buffer is bound in Phase 6.

Per vertex (SV_VertexID + instance index from the indirect draw -> visible-index buffer -> global blade index):
1. uint bladeIdx = visibleLodN[instanceID]; BladeInstance b = blades[bladeIdx].
2. Unpack yaw + scale from b.packedYawScale; reconstruct the base TRS (pivot at base y=0), same as GrassScatter.
3. WIND: wave = sin(time * windFrequency + hashToPhase(b.hash)) * windStrength; windTilt = windDir * wave. SAME formula as GrassBendSimulator (PHASE_FREQ baked into hash in Phase 2).
4. LEAN-AWAY: loop interactors (Phase 6 buffer), accumulate away-direction * falloff * strength * bendStrength, same math as GrassBendSimulator (radius footprint, 1 - d/radius falloff, away = normalize(basePos.xz - interactor.xz)). Clamp to MAX_LEAN_DEGREES (55 deg/metre, 80 max - SAME constants).
5. Compose lean rotation about the base; apply flatten (local-Y scale loss by trample fraction) EXACTLY as the CPU LeanRotation + flatten block.
6. Transform the LOD mesh vertex by the reconstructed+deformed matrix -> world -> clip.

NOTE on recovery: the CPU path persists per-blade lean state that recovers over time. The GPU path is STATELESS per frame (no per-blade RW history in the steady fast path) - it computes instantaneous lean from current interactor positions each frame. This matches the brainstorm (deform fully in VS, interactors as a per-frame buffer). The visible result: blades snap to the instantaneous lean and return when the interactor leaves (recovery becomes immediate rather than rate-limited). This is the locked design (report: deform fully on GPU, no matrix buffer write). If a softened return is wanted later it is a follow-up, NOT this plan.

## LOD meshes + passes

- 3 RenderMeshIndirect calls, one per LOD mesh (LOD0 cross-quad, LOD1 single quad, LOD2 billboard). Each uses its own visibleLodN buffer + argsN. LOD2 billboard: orient the quad to face the camera in the VS (or skip when lodCount/skip config says so).
- Mirror the CPU shader 3 passes (forward/shadow/depth) so shadows + depth line up. Cull Off (single-sided strips). Field-wide bounds passed to RenderMeshIndirect (RenderParams.worldBounds) - the per-blade GPU frustum cull already trimmed, the bounds is just the safety AABB.

## Verification gate (live-editor evidence)

1. set_active_instance GrassInteract FIRST. Force the high tier (debug override - Phase 7 wires it; for now a temporary bool).
2. PLAY MODE: the field renders - color gradient correct (not black), blades placed correctly, 3 LODs visible at their distances.
3. execute_code UnityEditor.UnityStats.triangles for one frame -> scales with the visible LOD distribution (near = more LOD0 tris; far = fewer). Cross-check the tri count is plausible vs the Phase 4 LOD counts x mesh tri counts.
4. WIND animates (blades sway, out of lockstep - the hash phase works).
5. Move a GrassInteractor -> the correct footprint of blades leans away GPU-side (Phase 6 must be in for this; if Phase 6 not yet done, verify wind + placement + LOD here and defer the interactor visual to Phase 6 gate).
6. PROFILER: main-thread grass cost ~0 (no per-blade CPU matrix rebuild) - the win condition.
7. 100k-250k blades: set the demo field TargetInstances high; confirm it renders and the frame stays GPU-bound, not main-thread-bound.

Pass = renders correct color + placement + 3 LODs + wind + ~0 main-thread cost at 100k+. Interactor lean validated here or at Phase 6.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| VS StructuredBuffer reads unsupported on the target GLES tier (maxComputeBufferInputsVertex) | 3 | 4 | 12 | Probe gates it (Phase 7 R1). In editor (desktop GL/Vulkan) it works; the real test is Phase 7 hardware. Engine demotes to CPU if the probe/self-test fails. |
| Yaw/scale unpack diverges from CPU pose -> blades look wrong vs CPU tier | 2 | 3 | 6 | Reconstruct using the inverse of the Phase 2 pack; A/B compare one frame against the CPU tier at the same camera; widen packing if visibly off. |
| Wind look differs from CPU (phase/hash mismatch) | 2 | 2 | 4 | Bake the hash from the SAME (p.x*0.37 + p.z*0.21)*windNoiseScale formula; reproduce sin(time*freq + phase)*strength identically. |
| LOD2 billboard faces wrong way / pops | 2 | 2 | 4 | Camera-facing in VS using camPos; threshold continuity with LOD1 from the same LodMaxDistances. |
| RenderMeshIndirect bounds wrong -> whole draw culled | 2 | 4 | 8 | Pass the field-wide WorldBounds (same AABB the CPU path uses); default zero-extent culls everything (known CPU-path lesson). |

## Rollback

Delete GrassInteractIndirect.shader; revert GrassGpuEngine to the Phase 4 inert state. Facade stays on CPU tier. CPU shader untouched throughout.
