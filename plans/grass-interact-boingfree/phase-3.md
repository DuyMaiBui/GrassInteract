# Phase 3 - Trample RT interaction

Cross-ref: plan.md - brainstorm-grass-interact-boingfree-20260601.md (section C). Blocked by Phase 1. Parallel-safe with Phase 2 (shares GrassInteractDeform.hlsl + shader - sequence the file edit after Phase 2 if same implementer).
Activate first: t1k-unity-base-code-conventions, unity-urp, t1k-unity-base-mcp-skill, t1k-unity-base-game-patterns.

## Objective

Persistent, recovering trample trails that are interactor-count-independent in cost. A GrassTrampleMap MonoBehaviour owns an R8 RenderTexture over the field rect; each frame it (1) fades the map toward zero (recovery), (2) additively splats each registered interactor footprint via a CommandBuffer, (3) pushes _GrassTrampleMap + _GrassFieldRect globals. The shader samples trample once per blade at pivot XZ and folds the blade toward the ground by trample * heightT plus a per-blade hashed splay. Direction-agnostic (magnitude-only); RG directional flow is a documented future upgrade.

## Files owned

Created:
- Assets/GrassInteract/Runtime/GrassTrampleMap.cs (NEW) - [ExecuteAlways] MonoBehaviour. Owns two R8 RenderTextures (ping-pong) sized from a resolution config. LateUpdate: fade blit (value *= recovery via a tiny fade material/Blit), then a CommandBuffer that SetRenderTarget(current) and DrawMesh a quad per interactor at world-XZ->UV->NDC with additive blend, scaled by radius/strength; push _GrassTrampleMap global + GrassFieldSpace.BindGlobals(). Static registry: a List<GrassInteractor> with Register/Unregister.
- Assets/GrassInteract/Runtime/GrassInteractor.cs (NEW) - [ExecuteAlways] MonoBehaviour with worldRadius, strength, optional heightOffset. OnEnable registers with GrassTrampleMap (find active map or static list); OnDisable unregisters. Exposes WorldPosition (transform.position), Radius, Strength.

Modified:
- Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl - add trample sample + fold-toward-ground + hashed splay, layered on top of the Phase 2 wind inside GrassInteract_ApplyDeform.
- Assets/GrassInteract/Shaders/GrassInteractInstanced.shader - declare TEXTURE2D(_GrassTrampleMap)+SAMPLER in all 3 passes if not already global via the .hlsl.

## Implementation steps

1. GrassTrampleMap: serialized resolution (e.g. 256/512, [Min]), recoveryPerSecond (0..1 fade rate), splatStrengthScale. Create RT: new RenderTexture(res, res, 0, RenderTextureFormat.R8){ enableRandomWrite=false, wrapMode=Clamp, filterMode=Bilinear }; allocate two for ping-pong. Initialize cleared to 0.
2. Fade pass: Blit src->dst with a fade material (a tiny unlit shader or Graphics.Blit with a material that multiplies by saturate(1 - recoveryPerSecond*dt)). Swap ping-pong. Alternative simpler: a CommandBuffer ClearRenderTarget is wrong (kills trail); use the multiply-blit.
3. Splat pass: build a CommandBuffer; SetRenderTarget(activeRT); for each registered interactor compute UV = GrassFieldSpace.WorldToUv(interactor.WorldPosition); convert UV to NDC; DrawMesh a unit quad (built once) at that NDC with a splat material (additive blend, soft circular falloff from a radial function) sized by interactor.Radius mapped into UV space / strength. Execute via Graphics.ExecuteCommandBuffer. Keep the quad mesh + materials cached (no per-frame alloc).
4. Push globals: Shader.SetGlobalTexture("_GrassTrampleMap", activeRT); call the GrassFieldSpace built by GrassInteractField (or have GrassTrampleMap own its own GrassFieldSpace from the same transform/bounds - SSOT: prefer reading the field rect that GrassInteractField binds; document the single owner).
5. Shader/.hlsl: sample float trample = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler, GrassField_WorldToUv(pivotWS), 0).r; fold = blade bends toward ground: scale the (posWS - pivotWS) vertical component down by trample*heightT and push horizontally by a per-blade hashed splay direction * trample. Layer AFTER wind so trample dominates a flattened patch. Apply in all 3 passes for matching shadow/depth.
6. SPIKE FIRST (risk mitigation): before wiring the shader read, render the trample RT to a debug RawImage / use the Frame Debugger to confirm a single interactor produces a fading bright dot that moves with the interactor. Only then connect the shader sample.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| URP RT splat path wrong (CommandBuffer target/blend incorrect under URP 17.3) | 3 | 4 | 12 | SPIKE: visualize the RT first (RawImage). Use explicit additive BlendOp via the splat material; Blit ping-pong for fade. Verify in Frame Debugger before shader wiring. |
| Trample UV != density UV (drift) | 3 | 5 | 15 | Both read _GrassFieldRect from the single GrassFieldSpace owned/bound by GrassInteractField. GrassTrampleMap MUST NOT compute its own rect independently - it reads the same bound rect. This is the plan-level score-20 mitigation in action. |
| RT not cleared on create -> garbage trail at start | 2 | 3 | 6 | Clear both RTs to 0 on allocation; re-clear on resolution change. |
| Per-frame GC from CommandBuffer / material alloc | 3 | 3 | 9 | Cache CommandBuffer, quad mesh, splat+fade materials; only Clear()+rebuild the cmd buffer contents (or reuse with cb.Clear()). No new RenderTexture per frame. |
| ExecuteAlways edit-mode RT churn / leak on disable | 2 | 4 | 8 | Release RTs in OnDisable/OnDestroy (rt.Release() + DestroyImmediate). Re-create in OnEnable. |
| Magnitude-only fold looks flat/unnatural | 2 | 2 | 4 | Add per-blade hashed splay so flattened blades fan out; document RG directional map as future upgrade (design doc section C). |

## Effort

L

## Scene-window verification gate

1. read_console -> ZERO errors after compile.
2. SPIKE gate: a single GrassInteractor produces a moving, fading bright spot in the trample RT (debug RawImage or Frame Debugger).
3. Open GrassInteractDemo.unity in Play. The demo effector (now driving a GrassInteractor) leaves a FLATTENED TRAIL behind it that RECOVERS (stands back up) over a few seconds.
4. Trail follows the interactor in BOTH Game and Scene views.
5. Profiler: no per-frame GC from the trample update; cost independent of interactor count (1 vs 10 interactors -> same map-update cost).
6. The flattened patch aligns spatially with the interactor world position (UV mapping correct - cross-check vs field rect gizmo).

Done only when: moving effector leaves a recovering flattened trail, aligned to world position, no per-frame GC, visible in Scene view.
