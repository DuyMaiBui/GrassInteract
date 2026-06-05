# DeformMode → Wind/Interact Bools Refactor

## Status: code on disk, gate deferred to main loop

## Files edited
- Runtime/ScatterLayer.cs — DeformMode enum + deformMode field deleted; affectedByWind + affectedByInteractors bool fields added; InteractsWithDeform kept as derived (wind || interactors); AffectedByWind + AffectedByInteractors accessors added
- Runtime/MeshScatterEngine.cs — ID_WindEnabled + ID_InteractorsEnabled ShaderIDs added; affectedByWind + affectedByInteractors snapshot fields added; interactsWithDeform kept as cached OR-helper; build path sets _WindEnabled + _InteractorsEnabled on 3 LOD materials; MaterialGroup constructor signature updated to (windFlag, interactorsFlag, ID_WindEnabled, ID_InteractorsEnabled); Step() gate changed to affectedByWind only; Submit globals split into independent wind block (if affectedByWind) and interactor block (if affectedByInteractors)
- Runtime/GrassGpuEngine.cs — ID_WindEnabled + ID_InteractorsEnabled ShaderIDs added; Build sets _WindEnabled + _InteractorsEnabled on 3 LOD material instances from layer.AffectedByWind / layer.AffectedByInteractors
- Shaders/ScatterInstanced.shader — all 3 passes (UniversalForward, ShadowCaster, DepthOnly): Properties block _InteractsWithDeform replaced with _WindEnabled + _InteractorsEnabled (default 0); CBUFFER / loose uniform declarations updated; gate logic split into independent if (_WindEnabled >= 0.5) wind block and if (_InteractorsEnabled >= 0.5) interactor loop; lean composition gated on (wind || interactors)
- Shaders/GrassInteractIndirect.shader — all 3 passes: Properties block adds _WindEnabled + _InteractorsEnabled (default 1 — preserves existing always-on grass behavior); CBUFFER (Pass 0) + loose uniforms (Pass 1 + 2) updated; wind block wrapped in if (_WindEnabled >= 0.5); interactor loop wrapped in if (_InteractorsEnabled >= 0.5); trail deform loop left unconditional
- Editor/GrassInteractDemoBuilder.cs — line 273: so.FindProperty("deformMode").enumValueIndex = 0 replaced with so.FindProperty("affectedByWind").boolValue = true + so.FindProperty("affectedByInteractors").boolValue = true

## Files created
- Editor/MigrateDeformModeToWindInteract.cs — migration menu at Tools/GrassInteract/Migrate/DeformMode - Wind/Interact Bools

## Migration map
- deformMode=Auto + Grass kind → wind=true,  interact=true
- deformMode=Auto + Mesh kind  → wind=false, interact=false
- deformMode=On               → wind=true,  interact=true
- deformMode=Off              → wind=false, interact=false

## CPU tier (GrassCpuEngine)
Deferred — GrassCpuEngine was not modified. The CPU bend simulator (GrassBendSimulator) gates wind and interactor contributions in C# before uploading positions, so per-blade CPU work is already conditional by nature. Adding AffectedByWind / AffectedByInteractors checks at the simulator-call level is a future polish step; the GPU tier is the default rendering path on this host.

## Notes / gotchas
- ID_InteractsWithDeform is kept dead in MeshScatterEngine (no SetFloat call) so old .mat files that still carry the property in their YAML do not produce warnings; Unity ignores writes to non-existent shader properties.
- GrassInteractIndirect.shader defaults _WindEnabled=1 and _InteractorsEnabled=1 in the Properties block (not 0) so existing grass materials that predate this refactor continue to deform without requiring engine re-build or migration.
- ScatterInstanced.shader defaults both flags to 0 (mesh shader was already static-by-default; engine sets flags at Build time).
- The MaterialGroup slow-path (authored renderer overrides) derives windFlag/interactorsFlag from this.affectedByWind / this.affectedByInteractors, which are snapshots taken earlier in the same Build call — no ordering hazard.
- Trail deform loop in GrassInteractIndirect.shader is intentionally left unconditional (no _TrailEnabled gate) — trail deform has its own count guard (_GrassTrailSegmentCount == 0 → loop no-ops).
