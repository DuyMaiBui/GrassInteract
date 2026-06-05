# Phase 5 - Editor brush tool

Cross-ref: plan.md - brainstorm-grass-interact-boingfree-20260601.md (section F). Blocked by Phase 4 (paints the GrassLayer density map).
Activate first: t1k-unity-base-code-conventions, unity-terrain, t1k-unity-base-mcp-skill.

## Objective

A GrassPainterWindow EditorWindow + SceneView.duringSceneGui hook that lets the user paint/erase grass density directly onto any collider (Unity Terrain, mesh, plane) via SceneView raycast, with adjustable radius/strength/falloff, a Handles disc gizmo, density + preview overlays, and Save that writes the GrassLayer density Texture2D back to disk deterministically.

## Files owned

Created:
- Assets/GrassInteract/Editor/GrassPainterWindow.cs (NEW) - EditorWindow under Tools/GrassInteract/Grass Painter. Holds the active GrassLayer, mode (Paint/Erase), radius, strength, falloff, and overlay toggles. Subscribes SceneView.duringSceneGui on OnEnable/show; unsubscribes on OnDisable. Throttled Apply.

(No runtime files modified - this is editor-only. Uses GrassFieldSpace + GrassLayer from earlier phases.)

## Implementation steps

1. Window UI (OnGUI): ObjectField for the target GrassLayer; mode toggle Paint/Erase; sliders radius (world metres), strength (0..1), falloff (0..1); toggles showDensityOverlay, showPreviewInstances; a Save button + a Revert. Validate the layer densityMap is readable+uncompressed (offer a one-click "fix import settings" that sets TextureImporter.isReadable + TextureImporterCompression.Uncompressed + single-channel R8 + point/bilinear, then reimport).
2. SceneView.duringSceneGui handler: get current Event e; build ray via HandleUtility.GUIPointToWorldRay(e.mousePosition) (the design doc says GUIPointToWorldRay - use it). Physics.Raycast(ray, out hit, maxDist) against any collider (no layer mask, or an optional paint mask). Draw a Handles.color disc at hit.point with the brush radius (and a falloff inner ring). Consume mouse-down/drag (e.Use()) so painting does not select objects (use GUIUtility.hotControl / a passive control id).
3. Paint stamp: convert hit world pos -> field UV via GrassFieldSpace.WorldToUv (SAME rect as runtime). Map brush radius (world) -> pixel radius (UV * texture resolution). Stamp a soft circular kernel into a CPU pixel buffer (Color32[] or float[] cached on the window): density += strength * falloffWeight (Paint) or -= (Erase), clamped 0..1. THROTTLE Texture2D.SetPixels/Apply: write into the CPU buffer every move, but only call SetPixels32+Apply on a time/distance cadence (e.g. every N ms or every K px moved) and on mouse-up - never per drag pixel (risk table).
4. Overlays: showDensityOverlay - draw the density map projected on the field rect (a Handles/GL quad or a temporary debug material). showPreviewInstances - optionally rebuild + show the field via the existing GrassInteractField in the scene (or a lightweight preview); cheapest: just trigger the field Rebuild on save.
5. Save: ensure CPU buffer flushed -> densityMap.SetPixels32 + Apply(false) -> AssetDatabase: EditorUtility.SetDirty(densityMap) + AssetDatabase.SaveAssets(). Round-trip-safe: reloading the asset yields the same pixels (point/bilinear, uncompressed). Mark the GrassLayer dirty too if any field changed.
6. Determinism check: after Save, a GrassInteractField Rebuild produces grass matching the painted density (same seed -> same layout).

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Per-drag-pixel Apply() stalls the editor | 3 | 3 | 9 | CPU pixel buffer; throttle Apply to cadence + mouse-up only. SetPixels32 (not per-pixel SetPixel). |
| Painting selects/deselects scene objects (event not consumed) | 3 | 2 | 6 | Use a passive control id + HandleUtility.AddDefaultControl; e.Use() on mouse down/drag in paint mode. |
| Brush UV != runtime UV (painted spot != grass spot) | 2 | 5 | 10 | Brush converts world->UV via the same GrassFieldSpace.WorldToUv runtime uses. No independent mapping. |
| densityMap import settings wrong after paint (compressed on reimport) | 3 | 4 | 12 | The fix-import-settings button enforces readable+uncompressed; Save does not change import settings; warn if texture is compressed. |
| Saved map not byte-reproducible -> non-deterministic reload | 2 | 4 | 8 | Uncompressed R8 + Apply(false); verify reload pixels == pre-save buffer in the gate. |
| Raycast misses (no collider) -> no paint, silent confusion | 2 | 2 | 4 | Show the disc gizmo only when the ray hits; status text "no collider under cursor" otherwise. |

## Effort

L

## Scene-window verification gate

1. read_console -> ZERO errors after compile.
2. Open a scene with the Ezereal Unity Terrain (has a TerrainCollider). Open Tools/GrassInteract/Grass Painter, assign a GrassLayer.
3. Paint over the terrain: a Handles disc follows the cursor; dragging stamps density; Erase removes it. Editor stays responsive while dragging (throttle working).
4. Save: the density Texture2D asset is written; reopen/reimport -> identical pixels (deterministic).
5. Trigger the GrassInteractField Rebuild -> grass appears exactly where painted, snapped to terrain (Phase 4 + Phase 5 integration).
6. Reload the scene/asset -> same grass layout (saved layer reloads deterministically).

Done only when: painting on the terrain produces saved grass that reloads deterministically, editor stays responsive, painted-vs-grass alignment correct.
