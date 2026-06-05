# Phase 1 - Scene-window + edit-mode rendering

Cross-ref: plan.md - brainstorm-grass-interact-boingfree-20260601.md (section B). Blocked by Phase 0.
Activate first: t1k-unity-base-code-conventions, t1k-unity-base-mcp-skill, t1k-unity-base-game-patterns, unity-urp.

## Objective

Fix the Scene-window invisibility and the obsolete-API warning. Replace the single-camera Graphics.DrawMeshInstanced(..., camera: gameCamera) path with a RenderPipelineManager.beginCameraRendering subscription that culls + LOD-selects + submits per rendering camera (Game AND SceneView), using the modern Graphics.RenderMeshInstanced(RenderParams) API. Make the field render in edit mode via [ExecuteAlways]. Allocate nothing per frame.

## Files owned

Modified:
- Assets/GrassInteract/Runtime/GrassInteractField.cs - add [ExecuteAlways]; move subscribe/unsubscribe to OnEnable/OnDisable; drop LateUpdate-driven single-camera render; call grassRenderer.Render(camera, ...) from the per-camera callback.
- Assets/GrassInteract/Runtime/GrassRenderer.cs - change Render to accept the per-call camera; submit via Graphics.RenderMeshInstanced(in RenderParams, mesh, 0, matrices, count) instead of DrawMeshInstanced; build a reusable RenderParams (material, shadowCastingMode, receiveShadows:false, layer, no per-call camera).

## Implementation steps

1. GrassInteractField: add [ExecuteAlways] + [DisallowMultipleComponent]. In OnEnable: ensure built (Rebuild if chunks null), then RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering; += OnBeginCameraRendering (guarded double-subscribe). In OnDisable: -= OnBeginCameraRendering and release nothing (keep chunks). In OnDestroy: unsubscribe + release chunks + pool.Clear.
2. Add private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam): early-out if chunks/renderer/config null. Accept Game cameras AND SceneView cameras; reject Preview/Reflection cameras (cam.cameraType == CameraType.Preview || CameraType.Reflection -> skip) to avoid double-draw. Call grassRenderer.Render(cam, chunks, config).
3. Drop the cullingCamera-driven LateUpdate render path (cullingCamera serialized field can stay as an optional override hint OR be removed; recommend remove for KISS - culling now happens against whatever camera is rendering).
4. GrassRenderer.Render(Camera camera, GrassChunk[] chunks, GrassLODConfig config): keep the frustum-cull + SelectLod logic unchanged. Build the RenderParams once in the ctor (rp = new RenderParams(material){ shadowCastingMode = ..., receiveShadows = false, layer = 0 }) and reuse it. For each visible chunk + batch: Graphics.RenderMeshInstanced(in rp, mesh, 0, batch, count). RenderMeshInstanced submits to ALL active cameras for the current SRP frame by default - so to keep per-camera culling correct, gate the submission to the camera passed in via the beginCameraRendering callback (each callback fires per camera; cull against that camera; the matrices submitted are only those visible to it). Confirm RenderMeshInstanced respects the active render context camera; if it draws to all cameras, fall back to RenderParams.camera = camera to scope it (verify in the gate).
5. Field-rect gizmo: in OnDrawGizmosSelected also draw the GrassFieldSpace rect (wire rect at field Y) in addition to the existing chunk AABBs.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| beginCameraRendering double-subscribe on domain reload -> draws multiplied | 4 | 3 | 12 | Always -= before += in OnEnable; unsubscribe in OnDisable AND OnDestroy. Verify draw count in Frame Debugger (one set per camera). |
| RenderMeshInstanced draws to ALL cameras regardless of cull camera -> wrong-LOD double draw | 3 | 4 | 12 | Test empirically in the gate. If it ignores the active camera scope, set RenderParams.camera = cam to restrict; document the chosen behavior. |
| ExecuteAlways triggers Rebuild churn / GC in edit mode | 3 | 3 | 9 | Rebuild only on OnEnable + explicit context-menu, never in OnBeginCameraRendering. Per-frame path allocates nothing. |
| SceneView camera renders before field built (edit mode order) -> null deref | 2 | 3 | 6 | Null-guard the callback; Rebuild lazily in OnEnable. |
| Leftover [Obsolete] DrawMeshInstanced warning | 2 | 2 | 4 | grep for DrawMeshInstanced -> zero; only RenderMeshInstanced remains. |

## Effort

M

## Scene-window verification gate

1. read_console after compile -> ZERO errors AND the DrawMeshInstanced obsolete warning is GONE (grep the source: no DrawMeshInstanced remains).
2. Open GrassInteractDemo.unity. In EDIT mode (not playing), the grass is VISIBLE in the Scene view. Move the Scene camera - grass culls/LODs correctly from the Scene camera viewpoint.
3. Enter Play mode: grass still renders in BOTH Game and Scene views.
4. Frame Debugger / draw-call count: no double-submission per camera (one instanced draw set per visible chunk per camera).
5. Profiler: no per-frame GC alloc from the render loop.

Done only when: grass visible in Scene view in edit mode, obsolete warning gone, no per-frame GC, no double draws.
