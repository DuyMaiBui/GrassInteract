# Phase 6 - GPU interactor upload

Effort: S. Depends on: Phase 5. Blocks: nothing (parallel-safe with Phase 8). Completes the lean-away deform that the Phase 5 VS reads.
Goal: snapshot GrassInteractor.Active each frame into a tiny StructuredBuffer<Interactor> (<=16) and bind it for the Phase 5 vertex shader lean-away loop. No GPU readback; upload-only, minimal bandwidth.

## Scope - file ownership

NEW:
- Assets/GrassInteract/Runtime/GrassInteractorBuffer.cs - owns a small GraphicsBuffer(Target.Structured) sized MAX_INTERACTORS (16), an Interactor[] staging array, an Upload(IReadOnlyList<GrassInteractor>) that fills + SetData, and Dispose. Defines struct Interactor { float3 posWS; float radius; float strength; } (blittable, ~20 B, pad to 32 B if alignment needs it).

MODIFIED:
- Assets/GrassInteract/Runtime/GrassGpuEngine.cs - own a GrassInteractorBuffer; in Step (or just before Submit) call Upload(GrassInteractor.Active); bind the buffer + the live count as a shader global/material property for GrassInteractIndirect.shader.
- Assets/GrassInteract/Shaders/GrassInteractIndirect.shader - the lean-away loop reads StructuredBuffer<Interactor> + uint interactorCount (the Phase 5 VS already references this buffer; this phase makes it real).

UNCHANGED: GrassInteractor.cs (registry + Active read-only - do NOT edit; reuse Active exactly as the CPU path does).

## Upload design

- MAX_INTERACTORS = 16 (UPPER_SNAKE_CASE const). Buffer allocated once at engine Build, reused every frame.
- Each frame: iterate GrassInteractor.Active; skip fake-null stale entries (same edit-mode-reload guard the CPU GrassBendSimulator.Step uses); copy posWS + radius + strength into the staging array up to 16. If Active.Count > 16, log ONCE (warn) and drop the overflow - do NOT NRE or grow unbounded.
- SetData the staging array (full 16 width or count); bind interactorCount uniform so the VS loop bound is exact.
- Edit mode + play mode both upload (the engine Step runs in both, mirroring the CPU driver) so a moved interactor leans in the Scene view too.

## Verification gate (live-editor evidence)

1. set_active_instance GrassInteract FIRST. High tier forced (Phase 7 override / temp bool).
2. PLAY MODE: place one GrassInteractor over the field, move it. The footprint of blades within its radius leans AWAY GPU-side; blades outside the radius stay upright; they return when it leaves (instantaneous per the Phase 5 stateless note).
3. Match the CPU tier: run the SAME interactor motion on the CPU tier (force-CPU) and on the GPU tier; the lean direction + footprint match (magnitude may differ slightly due to instantaneous-vs-recovered return - direction + which blades lean must match).
4. CAP TEST: enable 17 interactors. Console logs the overflow warning ONCE; no NRE; 16 are honored.
5. PROFILER: per-frame upload is a single small SetData (<= 16 * struct size); no GC; main-thread grass cost stays ~0.

Pass = correct footprint lean + CPU-direction parity + cap handled + negligible upload cost.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Interactor struct not blittable / alignment mismatch CPU vs HLSL -> garbage lean | 2 | 3 | 6 | float3 + 2 float only; verify HLSL struct stride == C# stride (pad to 16-byte multiple if needed); 1-interactor round-trip sanity in a debug readback. |
| >16 interactors -> overflow / NRE | 2 | 3 | 6 | Hard cap at MAX_INTERACTORS; warn-once + drop; never grow the buffer mid-frame. |
| Stale fake-null entries after domain reload -> bad data | 2 | 2 | 4 | Reuse the CPU path null-skip guard (GrassBendSimulator.Step pattern). |
| Per-frame SetData stall on mobile | 1 | 2 | 2 | Buffer is tiny (16 records); upload-only, no readback. Negligible bandwidth (R6). |

## Rollback

Delete GrassInteractorBuffer.cs; remove the upload + bind from GrassGpuEngine; the VS lean loop reverts to wind-only (interactorCount=0). GrassInteractor.cs untouched.
