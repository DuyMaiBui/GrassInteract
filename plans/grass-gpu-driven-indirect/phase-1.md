# Phase 1 - IGrassEngine seam + GrassCpuEngine extraction

Effort: M. Depends on: nothing. Blocks: ALL other phases (the seam is the integration point).
Goal: introduce a single engine abstraction the facade delegates to, and move the existing CPU path behind it VERBATIM. Zero behavior change - this de-risks every later phase by giving a clean swap point and a verified low tier.

## Scope - file ownership

NEW:
- Assets/GrassInteract/Runtime/IGrassEngine.cs - the seam.
- Assets/GrassInteract/Runtime/GrassCpuEngine.cs - wraps GrassBendSimulator + GrassRenderer.

MODIFIED:
- Assets/GrassInteract/Runtime/GrassInteractField.cs - delegate Rebuild / Step / Submit to an IGrassEngine instead of owning simulator + renderer directly.

UNCHANGED (consumed as-is, do NOT edit):
- GrassBendSimulator.cs, GrassRenderer.cs, GrassScatter.cs, GrassScatterResult, InstanceBatchPool.cs, GrassLODConfig.cs, GrassLayer.cs, GrassInteractor.cs, GrassFieldSpace.cs.

## Interface design (IGrassEngine)

The seam must cover everything the facade currently calls across both modes. Minimal surface (mirror the existing call sites in GrassInteractField):

- void Build(GrassLayer layer, GrassLODConfig config, Vector3 origin) - (re)build placement + per-engine state. CPU engine: builds scatter + simulator + renderer exactly as Rebuild() does today.
- void Step(float dt) - advance per-frame deform state. CPU engine: simulator.Step(dt). GPU engine (later): no-op or buffer upload.
- void Submit(Camera targetCamera, Vector3 lodReferencePos) - issue the draw. CPU engine: renderer.Render(...) with the simulator output slabs + scatter.WorldBounds. targetCamera null = all cameras (play); a camera = that one (edit per-camera).
- Bounds WorldBounds { get; } - for gizmos + bounds queries.
- void Dispose() - release engine resources (CPU: return pooled slabs to InstanceBatchPool; GPU later: release GraphicsBuffers).

The facade keeps ownership of: grassLayer, prewarmSlabs, InstanceBatchPool, the edit/play driver wiring (LateUpdate / EditorApplication.update / beginCameraRendering), and multiple-field warning. The engine owns scatter + per-mode render state.

## Implementation notes

- Keep #nullable enable. camelCase private fields, this. prefix, [SerializeField] private - mirror existing files exactly.
- GrassCpuEngine.Build does what GrassInteractField.Rebuild does today minus the field-level wiring: ReleaseScatter -> GrassScatter.Build -> new GrassRenderer -> new GrassBendSimulator. It receives the InstanceBatchPool from the facade (so pooling/lifetime stays facade-owned) OR owns its own pool (decide: keep pool facade-owned, pass it in - that matches today where the field owns the pool and prewarm).
- GrassInteractField.LateUpdate/EditorStepTick call engine.Step; SubmitGrass calls engine.Submit; OnDestroy/ReleaseScatter call engine.Dispose. The LOD-reference-position logic (Camera.main vs targetCamera) stays in the facade and is passed into Submit, OR moves into the engine - keep it in the facade to preserve the exact current selection logic.
- The WarnIfMultipleEnabledFields, gizmos, and edit/play driver code stay in the facade unchanged.

## Verification gate (live-editor evidence - NOT should-work)

1. set_active_instance GrassInteract FIRST.
2. Open the demo scene with the existing grass field. read_console - zero compile errors after the refactor domain reload.
3. EDIT MODE: capture a Scene-view screenshot. Compare against a pre-refactor screenshot of the same camera framing - blades render with identical color + placement (not black, not missing).
4. PLAY MODE: enter Play. execute_code reading UnityEditor.UnityStats.triangles for one frame; record the value. It MUST equal the pre-refactor tri count for the same camera + field (the LOD selection + slab submission are unchanged).
5. Move a GrassInteractor in Play - blades lean away exactly as before (the simulator is the same instance).
6. Exit Play, confirm no leaked errors / no GC spike in the profiler grass frame (the per-frame path still allocates nothing).

Pass = steps 2-6 all match pre-refactor behavior. Any divergence = the extraction changed behavior; fix before Phase 2.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Extraction subtly changes render/sim order or args (regression) | 2 | 4 | 8 | Pure move: same simulator/renderer instances, same args, same call sites. Screenshot + tri-count parity gate above. Diff the moved code against the original line-by-line. |
| Pool / lifetime ownership split wrong -> double-return or leak of slabs | 2 | 3 | 6 | Keep InstanceBatchPool facade-owned; pass into engine.Build; engine.Dispose returns scatter slabs via GrassScatter.ReturnSlabs (same as ReleaseScatter today). |
| Edit-mode driver wiring lost in the move -> Scene view stops rendering | 2 | 4 | 8 | Leave ALL driver wiring (LateUpdate, EditorApplication.update, beginCameraRendering subscribe/unsubscribe) in the facade untouched; only the simulator/renderer calls move. |

## Rollback

Restore the pre-refactor GrassInteractField.cs; delete IGrassEngine.cs + GrassCpuEngine.cs. They are unreferenced once the facade is reverted.
