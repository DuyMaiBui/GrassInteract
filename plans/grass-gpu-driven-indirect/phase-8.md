# Phase 8 - Edit-mode render parity (beginCameraRendering for the indirect path)

Effort: S. Depends on: Phase 5 (and ideally Phase 7 for the final tier). Blocks: nothing (parallel-safe with Phase 6). Terminal before review.
Goal: re-prove the hard-won CPU-path Scene-view rendering discipline for the GPU indirect path. The Scene view (edit mode) MUST render the high tier with correct colors - not black, not empty - from a fresh domain reload. This is the R5 mitigation.

## Background (the CPU-path lesson to re-apply)

GrassInteractField already encodes the discipline for the CPU path: draws are issued from the PLAYER LOOP in play (LateUpdate, camera=null = all cameras) and from RenderPipelineManager.beginCameraRendering per-camera in edit mode - NOT from EditorApplication.update. Issuing an immediate-mode instanced draw from EditorApplication.update (outside a camera render) leaves the material UnityPerMaterial cbuffer unbound -> blades render BLACK; under RenderGraph (Unity 6 default) such draws from beginCameraRendering for the CPU path were the working answer. Phase 8 verifies the SAME call-site discipline holds for RenderMeshIndirect + compute dispatch.

## Scope - file ownership

MODIFIED:
- Assets/GrassInteract/Runtime/GrassInteractField.cs - ensure the edit/play driver routes GrassGpuEngine.Submit through the SAME sites as the CPU engine: play -> LateUpdate (camera=null); edit -> OnEditBeginCameraRendering (targetCamera = the rendering camera; SceneView + Game camera types only, skip preview/reflection). The compute cull dispatch must run at a point where its results are ready for the indirect draw in the same frame/camera context.
- (possibly) Assets/GrassInteract/Runtime/GrassGpuEngine.cs - if the cull command buffer + RenderMeshIndirect must be issued together from the camera-render callback (so the cbuffer/material binds), structure Submit to run cull-then-draw within that callback. Decide: dispatch cull once per frame (shared) vs per-camera; for edit-mode per-camera submit, ensure the visible-index buffers are valid for the camera being rendered (cull uses that camera frustum).

UNCHANGED: shaders, compute, ChunkedBladeBuffer, both engines logic.

## Edit-mode specifics to verify

- Cull frustum source in edit mode = the camera passed to OnEditBeginCameraRendering (the Scene-view camera), so the Scene view culls to what it actually sees. Play mode camera=null path uses Camera.main (as the CPU path does for LOD ref) - confirm the indirect cull picks a sensible camera for the all-cameras case.
- Material cbuffer binding: RenderMeshIndirect from inside beginCameraRendering must bind _BaseColor/_TipColor (the CPU-path black-blade failure mode). Confirm colors are correct in the Scene view.
- Domain reload: after a script recompile / fresh open, the buffers re-bake on OnEnable and the Scene view renders without entering Play.

## Verification gate (live-editor evidence)

1. set_active_instance GrassInteract FIRST. High tier active (Auto on dev machine or ForceGpu).
2. EDIT MODE (do NOT enter Play): the Scene view shows the grass field with CORRECT colors (base->tip gradient), correct placement, 3 LODs by Scene-camera distance. Capture a Scene-view screenshot - assert not black, not empty.
3. Trigger a script recompile (or close/reopen the scene) to force a domain reload; confirm the Scene view re-renders the field correctly without Play.
4. Move the Scene-view camera -> LOD selection + frustom cull update live (blades outside the Scene-camera frustum are culled; near blades show LOD0).
5. Move a GrassInteractor in edit mode -> blades lean in the Scene view (edit-mode upload from Phase 6 works).
6. Enter Play -> the field still renders (LateUpdate/all-cameras path); exit Play -> Scene view still renders (no driver desync).

Pass = Scene view renders correct color + LOD + cull + interactor in edit mode, survives a domain reload, and play<->edit transitions keep rendering.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Indirect draw from beginCameraRendering renders black (cbuffer unbound) - the CPU-path failure mode | 3 | 3 | 9 | Issue cull+draw from inside the camera-render callback (same site that fixed the CPU path); assert correct colors in step 2. |
| Cull results not ready for the indirect draw in the same callback -> empty field | 2 | 4 | 8 | Run the cull command buffer immediately before the RenderMeshIndirect in the same Submit, same camera context; verify non-empty in step 2/4. |
| Per-camera edit-mode submit double-dispatches cull (N cameras) -> cost / flicker | 2 | 2 | 4 | Target only the rendering camera per callback (as the CPU path does); accept N small dispatches in edit mode (editor-only cost). |
| Domain reload leaves buffers released -> Scene view blank until Play | 2 | 3 | 6 | Re-bake + re-allocate buffers in OnEnable (both modes); step-3 reload check catches a missed re-init. |

## Rollback

Revert the GrassInteractField edit/play routing to CPU-only (the high tier simply is not submitted in edit mode); CPU tier edit-mode rendering is already proven and untouched.
