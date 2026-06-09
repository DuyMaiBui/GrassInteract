# Brainstorm: ScatterLayer Split + InstanceLayer Rigid Tilt

Date: 2026-06-09 · Status: design agreed, pending plan

## Problem statement
Refactor `ScatterLayer` so `DensityScatterLayer` and `InstanceScatterLayer` are two fully
independent concrete layers (no inheritance relationship), sharing only *feature configs*
(wind, interactor-deform). Add a NEW feature: `InstanceScatterLayer` instances perform a
whole-instance **rigid tilt** that responds to a moving interactor's position and recovers.

## Decisions captured (from user)
- Scope: structural refactor + new InstanceLayer feature. Density grass interactor behavior **unchanged**.
- "Rotation impact" = **whole-instance tilt away + recover** (not per-blade bend, not yaw-spin, not permanent).
- Migration: **none** — wipe old config on `TerrainScatterConfig.asset`, re-author the demo fresh.
- Class names: **keep** `DensityScatterLayer` / `InstanceScatterLayer`.
- Compute site: **C# simulation** of pos+rot, drawn via **GPU instancing**.
- Engine: **replace `MeshScatterEngine` entirely** with the new instanced-prop engine.
- Scale: **tens of thousands+** instances → C# sim must be **Burst-jobified**, cull stays on GPU.
- Collider tilt: **optional toggle** (default off).

## Current-state findings
- Composition refactor ~80% done but has a DRY defect: `ScatterWindConfig`/`ScatterDeformConfig`/
  `ScatterBoundsConfig`/`ScatterRenderConfig`/`ScatterPlacementConfig` are already `[Serializable]`
  structs designed to be embedded, yet BOTH layers re-declare ~22 shared fields loosely and rebuild
  the struct on every accessor call. Same fields live in 3 places.
- Two render paths exist: density grass = `GrassCpuEngine` + `GrassBendSimulator` + `GrassRenderer`
  (C# per-blade sim → `Graphics.RenderMeshInstanced`); props = GPU-indirect `MeshScatterEngine`
  (static baked TRS, per-vertex shader bend).
- Burst/Collections/Mathematics available transitively via URP 17.3 (MeshScatterEngine already uses
  `NativeArray`). No asmdef — runtime is in `Assembly-CSharp`.

## Design — Part A (structural)
Each concrete layer embeds the config structs as real serialized fields:
`[SerializeField] private ScatterWindConfig wind;` → `public override ScatterWindConfig Wind => this.wind;`
Struct becomes SSOT; ~22-field triplication removed. Layers share feature *structs*, not a base.
New `ScatterInstanceTiltConfig` (affectedByInteractors, tiltStrength, maxTiltAngle, recoveryRate,
radiusPadding) composed ONLY by `InstanceScatterLayer`.

## Design — Part B (`InstancedPropEngine` replaces `MeshScatterEngine`)
Per-frame: (1) Burst `IJobParallelFor` over persistent `NativeArray<TiltState>` — tilt-away from
in-range interactors + `MoveTowards` recovery, state persists → real spring-back; (2) job writes
`NativeArray<Matrix4x4>`/packed; (3) `GraphicsBuffer.SetData` upload; (4) reuse unchanged
`GrassCull.compute` GPU chunk frustum-cull + LOD; (5) `Graphics.RenderMeshIndirect` per LOD;
(6) port `InstanceColliderPool` + `InstanceFrustumCuller`, collider-tilt optional.

Tilt math = existing `LeanRotation` small-angle map applied to whole-instance TRS about its pivot.

## Tradeoffs / risks
- Per-frame NativeArray→GraphicsBuffer upload (~few MB/frame at tens of thousands). Desktop/console OK;
  watch mobile. Escape hatch: GPU-compute stateful tilt (HLSL, no upload) if mobile bandwidth bites.
- Dirty-region / sparse upload (only instances near an interactor) as a later optimization.
- Add `com.unity.burst` + `com.unity.mathematics` as explicit manifest deps (don't rely on transitive).
- Replacing `MeshScatterEngine` means porting its collider pool/culler — keep behavior parity.

## Next step
Hand to `/t1k:plan` for phased implementation (config-struct embed → strip duplicated fields →
ScatterInstanceTiltConfig → InstanceBendJob (Burst) → InstancedPropEngine → ScatterField routing →
collider port → demo re-author → compile/validate).
