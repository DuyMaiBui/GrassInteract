# Phase 6 - Demo rewire + README + cleanup

Cross-ref: plan.md - brainstorm-grass-interact-boingfree-20260601.md (sections A + success criteria). Blocked by Phases 1,2,3,5 (consumes GrassInteractor, GrassTrampleMap, GrassLayer, painter).
Activate first: t1k-unity-base-code-conventions, t1k-unity-base-mcp-skill.

## Objective

Bring the demo end-to-end onto the new Boing-free stack, remove the last BoingKit reference (Editor asmdef), and update the README to match reality. After this phase the demo scene runs the full pipeline (painted density -> instanced grass -> wind sway -> trample trail) with zero Boing references anywhere in GrassInteract.

## Files owned

Modified:
- Assets/GrassInteract/Demo/GrassInteractDemoEffector.cs - keep the circular-mover behavior, but the moving object now carries a GrassInteractor (instead of a BoingEffector) so it writes the trample map. Either AddComponent<GrassInteractor> requirement or document that the demo object has both the mover + a GrassInteractor. Drop any Boing using.
- Assets/GrassInteract/Editor/GrassInteractDemoBuilder.cs - remove using BoingKit + all BoingReactorField/BoingEffector creation; build instead: a GrassLayer asset (with a generated default density Texture2D - e.g. full or circular density), wire GrassTrampleMap onto a scene object, attach GrassInteractor to the effector, assign GrassLayer to the GrassInteractField. Update CreateOrUpdateConfig to write only render/LOD fields (placement moved to GrassLayer). Replace the flat Plane ground with (optionally) keeping the plane (has a collider for ground-snap) OR documenting the Ezereal terrain path.
- Assets/GrassInteract/Editor/GrassInteract.Editor.asmdef - remove "BoingKit" from references (LAST Boing ref in the whole module).
- Assets/GrassInteract/README.md - rewrite to describe the Boing-free architecture: GrassFieldSpace, GrassLayer (density), GrassTrampleMap + GrassInteractor (trample), wind tunables, Scene-window/edit-mode rendering, the Grass Painter tool, and the demo build steps.
- Assets/GrassInteract/Demo/GrassInteractDemo.unity - rebuilt by the demo builder (no Boing objects); old serialized Boing refs gone.
- Assets/GrassInteract/Demo/GrassInteractDemoConfig.asset - regenerated as render-only GrassLODConfig; a new GrassLayer asset created alongside.

## Implementation steps

1. GrassInteractDemoEffector: remove Boing dependency; ensure the moving demo object has a GrassInteractor component (RequireComponent or builder-added). The mover just animates the transform; the GrassInteractor (Phase 3) does the trample registration.
2. GrassInteractDemoBuilder: delete the BoingReactorField + BoingEffector creation block + field.Effectors wiring. Add: create a default density Texture2D (readable, uncompressed R8) - full-field density or a painted-circle default - save as an asset; create a GrassLayer asset referencing it + the render config; create a scene GameObject with GrassTrampleMap; on the effector add GrassInteractor (radius/strength matching the old effector feel); assign GrassLayer to the GrassInteractField. Keep the ForceSynchronousImport pattern for new assets (existing builder gotcha - same-run reference serialization).
3. CreateOrUpdateConfig: write only the render/LOD fields (lodMeshes, lodMaxDistances, shadow mode, maxBladeHeight, bendHeadroom, wind tunables). Remove the SerializedProperty writes for positionSampleMultiplier/rotationSampleMultiplier (deleted in Phase 0) and for placement fields now on GrassLayer.
4. Editor asmdef: remove "BoingKit" reference. Verify the Editor assembly compiles (no Boing types remain anywhere in Editor/).
5. README rewrite: architecture overview (two maps over one field rect), component list + responsibilities, how to paint a layer, how to build the demo, the car-on-terrain use case, performance notes (>=10k blades, no per-frame GC, obsolete warning gone). Verify any URLs per url-verification.md.
6. Final cleanup grep: grep -rin boing across the ENTIRE Assets/GrassInteract/ (Runtime+Editor+Demo+Shaders+README+.unity+asmdef) -> ZERO matches. Remove residual serialized Boing refs from any asset.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Removing Editor asmdef Boing ref before all Boing Editor code gone -> compile break | 3 | 4 | 12 | Remove the demo builder Boing code FIRST (step 2), THEN the asmdef ref (step 4). read_console after each. |
| Demo builder same-run asset-reference serialization fails (config/layer null) | 3 | 3 | 9 | Reuse the existing ForceSynchronousImport pattern for the new GrassLayer + density texture assets before the scene references them. |
| README drifts from final code (names/steps wrong) | 2 | 3 | 6 | Write README last, after demo verified; cross-check component names against the shipped files. |
| Old GrassInteractDemo.unity retains dangling Boing component -> load error | 2 | 3 | 6 | Builder creates a fresh scene (NewScene) - no carried-over Boing objects. Confirm scene loads clean. |
| Default density texture not readable/uncompressed -> demo grass black | 2 | 4 | 8 | Builder sets import settings on the generated density texture (readable, uncompressed R8) and reimports before use. |

## Effort

M

## Scene-window verification gate

1. read_console -> ZERO errors after compile and after demo build.
2. grep -rin "boing" Assets/GrassInteract/ -> ZERO matches across ALL subfolders + README + .unity + asmdefs.
3. Run Tools/GrassInteract/Build Demo Scene -> scene builds without error; GrassInteract.Editor asmdef has no BoingKit reference.
4. Open the built GrassInteractDemo.unity: grass visible in Scene view in EDIT mode (wind swaying).
5. Enter Play: the moving effector (GrassInteractor) leaves a recovering flattened trample trail; idle areas sway with wind; >=10k blades render with no per-frame GC.
6. README accurately describes the shipped components + the paint + demo workflow; any URLs verified.

Done only when: demo scene runs end-to-end (paint -> render -> wind -> trample) Boing-free, zero Boing refs project-wide, README accurate, no console errors.
