# Plan: BoingKit-free Interactive Grass + Terrain Paint Tool

**Generated:** 260601-1533 - **Project:** GrassInteract (Unity 6, URP 17.3, Mono - no DOTS/Burst)
**Design source (read first):** plans/reports/brainstorm-grass-interact-boingfree-20260601.md (all decisions user-locked via AskUserQuestion - do NOT re-ask)

Reworks the existing Assets/GrassInteract/ instanced-grass system: drop the BoingKit bend dependency, render in the Scene window + edit mode, add in-shader ambient wind + a trample RenderTexture interaction, density-map placement, and an editor brush. Target use case: grass bending as a car drives over Unity Terrain.

## Skills to activate FIRST (every phase)

- t1k-unity-base-code-conventions - camelCase private fields (no underscore), this. always, PascalCase public, UPPER_SNAKE_CASE constants, [SerializeField] private, #nullable enable in new files, no magic numbers.
- t1k-unity-base-mcp-skill - drive the Editor for the Scene-window verification gate each phase (read_console after every script change, poll editor_state.isCompiling).
- t1k-unity-base-game-patterns - service/config patterns (grass uses GPU instancing, NOT GameObject pools - InstanceBatchPool is matrix-slab pooling, keep it).
- URP shader references (unity-urp, unity-shader-graph for HLSL conventions) for Phases 0-3 shader work.
- unity-terrain - ground-snap raycast + paint-on-terrain target (Phases 4-5).

Per library-feature-discovery-protocol.md: before writing any new shader noise / RT-blit / raycast-snap util, grep Assets/GrassInteract/ + the Unity skills 3x for prior art; if found, reuse + note in skill; if not, implement and note.

## Phases

- Phase 0: Decouple BoingKit - remove 3 BoingKit.cginc includes + GrassInteract_ApplyLean; replace GrassInteractBend.hlsl -> GrassInteractDeform.hlsl (no-op stub); strip BoingReactorField from GrassInteractField, field param + UpdateShaderConstants from GrassRenderer, position/rotationSampleMultiplier from GrassLODConfig; remove BoingKit ref from runtime asmdef; add GrassFieldSpace helper. Effort: M
- Phase 1: Scene-window + edit-mode rendering - RenderPipelineManager.beginCameraRendering subscription, cull+LOD per Game AND SceneView camera, submit via Graphics.RenderMeshInstanced(RenderParams); ExecuteAlways; unsubscribe in OnDisable. Effort: M
- Phase 2: Ambient wind - flesh GrassInteractDeform.hlsl with hash/sin wind by pivot XZ, sway proportional to heightT, pivot-anchored; tunables _GrassWindDir/Strength/Freq/NoiseScale bound from config; applied in all 3 passes. Effort: S
- Phase 3: Trample RT interaction - GrassTrampleMap (R8 RT, ping-pong fade-recover, CommandBuffer additive splat, _GrassTrampleMap global) + GrassInteractor (register pos/radius/strength); shader folds blade toward ground by trample * heightT + hashed splay. Effort: L
- Phase 4: Density-map placement - GrassLayer SO (R8 readable density Texture2D, targetDensity, scaleRange, seed, groundSnapMask, GrassLODConfig renderConfig); rewrite ChunkGrid.Build to rejection-sample by density + raycast-down ground snap (fallback plane Y); GrassInteractField consumes GrassLayer. Effort: L
- Phase 5: Editor brush tool - GrassPainterWindow (EditorWindow) + SceneView.duringSceneGui: Paint/Erase, radius/strength/falloff, GUIPointToWorldRay -> Physics.Raycast -> density stamp (throttled Apply), Handles disc gizmo, density overlay + preview toggles, Save via SetPixels/Apply/SetDirty/SaveAssets. Effort: L
- Phase 6: Demo rewire + README + cleanup - GrassInteractDemoEffector -> GrassInteractor; GrassInteractDemoBuilder builds GrassLayer + GrassTrampleMap wiring (no Boing); remove BoingKit ref from Editor asmdef; update README.md; strip residual Boing serialized refs. Effort: M

## Feasibility

- Reuse check:
  - REUSE: GrassChunk, InstanceBatchPool (matrix-slab pooling, unchanged), the LOD-threshold + frustum-cull logic in GrassRenderer (re-homed onto per-camera callback), GrassBladeMeshBuilder (unchanged), the 3-pass shader structure.
  - NEW: GrassFieldSpace, GrassInteractDeform.hlsl, GrassTrampleMap, GrassInteractor, GrassLayer, GrassPainterWindow.
  - REWRITE: ChunkGrid.Build (rejection-sampling + ground snap), GrassRenderer.Render (per-camera, RenderParams), GrassInteractField (no Boing, ExecuteAlways, consumes GrassLayer), GrassLODConfig (drop Boing multipliers, add wind tunables), demo builder + effector.
- Complexity: moderate overall; Phase 3 (RT ping-pong + CommandBuffer splat in URP) and Phase 4 (deterministic rejection-sampling + raycast snap at build) are the complex spots.

## Dependencies

    Phase 0 (decouple) --> Phase 1 (scene render) --> Phase 2 (wind)
                                                 \--> Phase 3 (trample)   [parallel-safe with Phase 2]
    Phase 0 --> Phase 4 (density placement)  [needs GrassFieldSpace; independent of 1-3]
    Phase 4 --> Phase 5 (brush)              [brush paints the GrassLayer density map]
    Phases 1,2,3,5 --> Phase 6 (demo rewire + README + asmdef cleanup)

- Critical path: 0 -> 1 -> 3 -> 6 (longest by effort: M+M+L+M). Parallel long path 0 -> 4 -> 5 -> 6. Single implementer: strictly sequential 0..6.
- Blocks: Phase 0 blocks everything (shared GrassFieldSpace + Boing removal). Phase 4 blocks Phase 5. Phase 6 blocks nothing.
- Blocked by: none external - BoingKit stays installed, grass just stops referencing it.
- Parallel-safe pairs (multi-implementer): {Phase 2, Phase 3} after Phase 1. Note GrassInteractField.cs is touched by 1 AND 4 - sequence those on the same file. See File Ownership.

## Risk Assessment (MANDATORY)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Trample-map UV and density-map UV drift (two maps over field rect disagree) | 4 | 5 | 20 | HIGH. Single source of truth: GrassFieldSpace (C# + matching HLSL macros) built in Phase 0, emits _GrassFieldRect; every sampler keys off it. No phase computes UV inline. Verify Phase 3+4 by aligning a painted patch to a trample dent. |
| beginCameraRendering not unsubscribed -> leak / double-draw / editor crash on reload | 3 | 4 | 12 | Subscribe in OnEnable, unsubscribe in OnDisable; guard double-subscribe (-= before +=). ExecuteAlways runs in edit mode - verify no duplicate draws. |
| Density Texture2D compressed / non-readable -> black placement at load | 3 | 4 | 12 | GrassLayer validates isReadable + uncompressed R8 at Build; brush creates the asset with correct import settings. Hard error, no silent fallback. |
| Edit-mode rendering churns GC / hammers domain reload (ExecuteAlways) | 3 | 3 | 9 | Per-frame loop allocates nothing (snapshot LOD arrays, reuse RenderParams). Rebuild only on enable / explicit context-menu in edit mode, never per-frame. |
| Ground-snap raycast finds no collider at build (Terrain collider missing) | 3 | 3 | 9 | Fallback to field-plane Y with one Debug.LogWarning (surfaced). groundSnapMask documented; demo terrain has a TerrainCollider. |
| URP RT splat path wrong (SetRenderTarget+DrawMesh vs Blit ping-pong) | 3 | 4 | 12 | Phase 3 spikes the smallest splat first (one interactor, visualize RT in a RawImage) before wiring shader read. Blit ping-pong for fade, CommandBuffer DrawMesh for additive splat. |
| pragma target 4.5 -> 3.5 drop breaks an unrelated shader feature | 2 | 2 | 4 | Keep target 4.5 unless a concrete mobile need forces 3.5; no StructuredBuffer remains so either works. Defer the drop. |
| RenderMeshInstanced RenderParams API misuse (array overload vs span) | 2 | 3 | 6 | Use the matrix-array overload mirroring the old DrawMeshInstanced path; keep count cap 1023. |
| Brush stamp throttling missing -> editor stalls on every drag pixel | 2 | 3 | 6 | Paint into a CPU pixel buffer; throttle Texture2D.Apply() to mouse-up or N-ms cadence. |

Any score >= 15 mandates mitigation before that phase starts. The UV-drift risk (20) is mitigated structurally in Phase 0 (build GrassFieldSpace first); Phases 3 and 4 MUST consume it and never recompute UVs.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 0 - Decouple BoingKit | M | Blocks all. Builds GrassFieldSpace (mitigates score-20 UV risk). |
| Phase 1 - Scene render | M | Blocked by 0. Touches GrassInteractField (sequence vs Phase 4). |
| Phase 2 - Ambient wind | S | Blocked by 1. Parallel-safe with 3. |
| Phase 3 - Trample RT | L | Blocked by 1. Highest single-phase risk (URP RT splat). |
| Phase 4 - Density placement | L | Blocked by 0. Touches GrassInteractField + ChunkGrid. |
| Phase 5 - Editor brush | L | Blocked by 4. |
| Phase 6 - Demo + README + cleanup | M | Blocked by 1,2,3,5. Removes Editor-asmdef Boing ref last. |
| Total | 3L + 3M + 1S | Critical path: 0 -> 1 -> 3 -> 6 (and parallel 0 -> 4 -> 5 -> 6). Single implementer: strictly 0..6. |

## File Ownership (no two phases edit the same file without sequencing)

| File | Phase(s) | Sequencing note |
|------|----------|-----------------|
| Shaders/GrassInteractInstanced.shader | 0, 2, 3 | 0 removes Boing includes; 2 adds wind call; 3 adds trample call. Sequential. |
| Shaders/GrassInteractBend.hlsl -> GrassInteractDeform.hlsl | 0 (rename+stub), 2 (wind), 3 (trample) | Sequential. |
| Runtime/GrassInteractField.cs | 0 (drop Boing), 1 (ExecuteAlways), 4 (consume GrassLayer), 6 (cleanup) | Most-contended - strictly sequential. |
| Runtime/GrassRenderer.cs | 0 (drop field param), 1 (per-camera RenderParams) | Sequential. |
| Runtime/GrassLODConfig.cs | 0 (drop Boing multipliers), 2 (add wind tunables) | Sequential. |
| Runtime/ChunkGrid.cs | 4 (rewrite Build) | Phase 4 only. |
| Runtime/GrassFieldSpace.cs (NEW) | 0 | Created Phase 0; read-only thereafter. |
| Runtime/GrassTrampleMap.cs, GrassInteractor.cs (NEW) | 3 | Phase 3 only. |
| Runtime/GrassLayer.cs (NEW) | 4 | Phase 4 only. |
| Editor/GrassPainterWindow.cs (NEW) | 5 | Phase 5 only. |
| Editor/GrassInteractDemoBuilder.cs | 6 | Phase 6 only. |
| Demo/GrassInteractDemoEffector.cs | 6 | Phase 6 only. |
| GrassInteract.asmdef | 0 (remove BoingKit ref) | Phase 0. |
| Editor/GrassInteract.Editor.asmdef | 6 (remove BoingKit ref) | Phase 6 - after demo builder drops Boing types. |

## Backwards compatibility

- Breaking (intentional, no migration needed - single internal demo): GrassLODConfig loses position/rotationSampleMultiplier; GrassInteractField loses the reactorField ref; ChunkGrid.Build signature changes (now takes GrassLayer). Only consumer is the demo, rebuilt in Phase 6.
- Additive: GrassFieldSpace, GrassTrampleMap, GrassInteractor, GrassLayer, wind tunables - all new, flagged.
- Asset note: existing GrassInteractDemoConfig.asset is regenerated by the Phase 6 builder; GrassInteractDemo.unity is rebuilt; old serialized Boing refs become dangling - Phase 6 clears them.

## Rollback

Each phase is a self-contained commit. Revert in reverse-dependency order 6->5->4->3->2->1->0. Phase 0 GrassInteractDeform.hlsl is a no-op stub, so reverting Phases 2/3 leaves a compiling static field. Reverting Phase 1 returns to single-camera DrawMeshInstanced (obsolete warning) but still compiles. No phase removes the BoingKit package - a full revert only restores the asmdef refs.

## Verification philosophy (every phase)

Each phase ends with a Scene-window gate: open GrassInteractDemo.unity (or a scratch scene), confirm via t1k-unity-base-mcp-skill read_console (zero compile errors) + visual check in the Scene view (edit mode where the phase claims it). No phase is done until the Scene-window gate passes and the full asmdef compiles clean.

## Cross-reference

Design rationale, rejected alternatives, naming charter: plans/reports/brainstorm-grass-interact-boingfree-20260601.md.
