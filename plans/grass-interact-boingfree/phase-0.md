# Phase 0 - Decouple BoingKit

Cross-ref: plans/grass-interact-boingfree/plan.md - plans/reports/brainstorm-grass-interact-boingfree-20260601.md (section A).
Activate first: t1k-unity-base-code-conventions, t1k-unity-base-mcp-skill, unity-urp.

## Objective

Sever every BoingKit dependency from the grass system while keeping the static field rendering. Establish the shared GrassFieldSpace world-XZ <-> UV mapping that every later phase (trample + density) keys off, so the two top-down maps can never drift. After this phase the grass renders exactly as before but with zero Boing references and a no-op deform stub.

## Files owned

Modified:
- Assets/GrassInteract/Runtime/GrassInteractField.cs - remove reactorField field, the Boing null/mode warnings, and the field arg passed to Render.
- Assets/GrassInteract/Runtime/GrassRenderer.cs - drop the BoingReactorField field param, the fieldProps MPB binding, boundFieldResourceSetId, UpdateShaderConstants call, and the using BoingKit.
- Assets/GrassInteract/Runtime/GrassLODConfig.cs - remove positionSampleMultiplier / rotationSampleMultiplier fields + accessors + the Field Sampling (Boing) header. Add a GrassFieldSpace accessor helper (FieldOrigin/FieldSize from transform + fieldBounds) OR expose fieldBounds for the helper.
- Assets/GrassInteract/Shaders/GrassInteractInstanced.shader - remove all 3 BoingKit.cginc includes and all 3 GrassInteract_ApplyLean calls; swap the GrassInteractBend.hlsl include for GrassInteractDeform.hlsl in all 3 passes.
- Assets/GrassInteract/GrassInteract.asmdef - remove "BoingKit" from references.

Renamed/created:
- Assets/GrassInteract/Shaders/GrassInteractBend.hlsl -> GrassInteractDeform.hlsl - replace body with a no-op GrassInteract_ApplyDeform(inout float3 posWS, inout float3 nrmWS, float3 pivotWS) that does nothing yet (filled in Phases 2-3). Include guard GRASSINTERACT_DEFORM_INCLUDED. Delete the old .hlsl + its .meta.
- Assets/GrassInteract/Runtime/GrassFieldSpace.cs (NEW) - static helper + a small struct: stores fieldOriginXZ + fieldSizeXZ; methods WorldToUv(float3) / UvToWorld(float2) and a BindGlobals() that sets Shader.SetGlobalVector("_GrassFieldRect", (originX, originZ, sizeX, sizeZ)). Mirror the same math in an HLSL snippet inside GrassInteractDeform.hlsl (GrassField_WorldToUv) so C# and shader agree. #nullable enable.

## Implementation steps

1. Create GrassFieldSpace.cs: a readonly struct GrassFieldSpace { Vector2 OriginXZ; Vector2 SizeXZ; } with Vector2 WorldToUv(Vector3 worldPos) and Vector3 UvToWorld(Vector2 uv, float y). Add static int ShaderId _GrassFieldRect = Shader.PropertyToID("_GrassFieldRect") and instance method BindGlobals() -> Shader.SetGlobalVector. Field rect = centered on the GrassInteractField transform position, size = config.FieldBounds (matches current ChunkGrid centering: origin +/- half).
2. Rename GrassInteractBend.hlsl to GrassInteractDeform.hlsl (and its .meta). Replace the Boing lean body with: include guard, a no-op void GrassInteract_ApplyDeform(inout float3 posWS, inout float3 nrmWS, float3 pivotWS) {}, plus a float2 GrassField_WorldToUv(float3 posWS) using a CBUFFER/global float4 _GrassFieldRect. Document that wind (Phase 2) + trample (Phase 3) fill this in.
3. Shader: in all 3 passes (UniversalForward, ShadowCaster, DepthOnly) delete the #include ".../BoingKit.cginc" line and the #include of GrassInteractBend.hlsl; add #include ".../GrassInteractDeform.hlsl". Replace GrassInteract_ApplyLean(posWS, nrmWS, pivotWS) with GrassInteract_ApplyDeform(posWS, nrmWS, pivotWS). Keep pragma target 4.5 (defer the 3.5 drop per risk table).
4. GrassRenderer.cs: remove using BoingKit, the BoingReactorField field param from Render, fieldProps MPB, boundFieldResourceSetId, the UpdateShaderConstants block. DrawMeshInstanced still passes a (now empty or removed) MPB - keep an empty MaterialPropertyBlock only if needed for the existing overload, else drop it. (Phase 1 rewrites this method fully; here just make it compile Boing-free.)
5. GrassLODConfig.cs: delete positionSampleMultiplier/rotationSampleMultiplier + their accessors + the Boing header. Add FieldBounds-based helper if GrassFieldSpace needs it (FieldBounds already exists - reuse).
6. GrassInteractField.cs: remove the reactorField SerializeField + all Boing warnings; in Rebuild build a GrassFieldSpace from transform.position + config.FieldBounds and call BindGlobals(); LateUpdate calls grassRenderer.Render(cam, chunks, config) with no field arg.
7. GrassInteract.asmdef: remove "BoingKit" reference.
8. Editor asmdef is NOT touched here (demo builder still references Boing until Phase 6) - leave Editor/GrassInteract.Editor.asmdef as-is so it keeps compiling.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|---|---|-------|------------|
| Editor asmdef still references BoingKit, but demo builder also references Boing types - removing only runtime ref could break Editor compile | 3 | 4 | 12 | Do NOT remove the Editor asmdef Boing ref in Phase 0 (deferred to Phase 6). Verify Editor assembly still compiles after runtime ref removal. |
| GrassFieldSpace math disagrees with ChunkGrid centering -> later UV drift | 3 | 5 | 15 | Derive GrassFieldSpace rect from the exact same origin +/- halfBounds expression ChunkGrid.Build uses. Add an inline comment cross-linking the two. This is the structural mitigation for the plan-level score-20 risk. |
| Leftover Boing using/include slips through -> compile error | 2 | 2 | 4 | grep -rin boing across Assets/GrassInteract/Runtime + Shaders must return zero hits before the gate. |
| Stale .meta after .hlsl rename -> shader fails to find include | 2 | 3 | 6 | Rename both .hlsl and .hlsl.meta together; let Unity reimport; read_console for shader errors. |

## Effort

M

## Scene-window verification gate

1. Via t1k-unity-base-mcp-skill: refresh_unity scripts + poll editor_state.isCompiling false; read_console -> ZERO errors/warnings (the prior Boing null-field warning must be gone).
2. grep -rin "boing" Assets/GrassInteract/Runtime Assets/GrassInteract/Shaders -> zero matches. (Editor/ + Demo/ + .unity may still match until Phase 6.)
3. Open GrassInteractDemo.unity. In PLAY mode the static grass field still renders (no bend - expected; deform is a no-op stub). No magenta/error shader.
4. Confirm GrassInteract.asmdef has no BoingKit reference and the GrassInteract runtime assembly compiles standalone.

Done only when: zero console errors, zero Boing refs in runtime+shaders, static grass renders in Play mode.
