# Phase 3: GrassBendSimulator (the heart)

**Effort: L** | **Blocked by: Phase 2** | **Blocks: Phase 4**

## Goal

Add `GrassBendSimulator` - the C# heart that rebuilds the per-instance matrices every frame so grass
sways (wind) and leans away from interactors (bend) and recovers, with ALL state readable in C# (no
GPU readback). Convert `GrassInteractor` from a `GrassTrampleMap` registrant to a plain static-registry
source the simulator reads. Wire the field to own the simulator and feed its output slabs to the
renderer. After this phase the full new path is LIVE and verified - which is the gate that lets Phase 4
delete the old path.

## File ownership

- `Runtime/GrassBendSimulator.cs` (NEW - the per-frame pass; owns base arrays + bendState + phase +
  renderMatrices output slabs).
- `Runtime/GrassInteractor.cs` (REWORK - drop `GrassTrampleMap.Register/Unregister`; add a static
  registry list with OnEnable/OnDisable add/remove; retarget the dev-build self-diagnostics warning
  from "no GrassTrampleMap" to "no GrassBendSimulator/Field"; keep worldRadius/strength/WorldPosition
  + gizmos).
- `Runtime/GrassInteractField.cs` (REWORK - own a `GrassBendSimulator`; drive `simulator.Step(dt)` then
  render `simulator.RenderSlabs`; this is the SECOND and final touch of this file, sequenced after
  Phase 2).

## Concrete steps

### GrassInteractor.cs (rework first - the simulator depends on the registry)

1. Replace the `GrassTrampleMap.Register/Unregister` calls in OnEnable/OnDisable with a static
   registry on the interactor itself: `private static readonly List<GrassInteractor> active = new();`
   plus `public static IReadOnlyList<GrassInteractor> Active => active;`. OnEnable adds (idempotent -
   guard Contains), OnDisable removes. Keep `WorldPosition`, `Radius`, `Strength`, and all gizmos.
2. Retarget the dev-build self-diagnostics `Update()`: replace `!GrassTrampleMap.HasActiveInstance`
   with a check that no enabled `GrassInteractField` (or no `GrassBendSimulator`) exists to consume
   interactors - warn "active but no GrassInteractField/GrassBendSimulator in the scene; nothing
   reads this interactor". Keep the zero-radius and zero-strength warnings.

### GrassBendSimulator.cs (new - the heart)

3. Constructor takes the `GrassScatterResult` (base matrix slabs + parallel position slabs + counts)
   and the `GrassLODConfig` (wind dir/strength/freq/noiseScale, bendStrength, flatten, recoveryRate).
   It OWNS: the base slabs + base position slabs (references), a per-blade `Vector2[] bendState`
   (current lean, slab-parallel), a per-blade `float[] phase` (precomputed wind phase per blade), and
   the OUTPUT `Matrix4x4[][] renderSlabs` (reused, allocated once - zero per-frame GC).
4. Precompute `phase[i]` once from the base position XZ (e.g. dot(posXZ, (0.37,0.21)) *
   windNoiseScale * 2pi) so each blade sways out of lockstep - mirrors the old shader spatialPhase so
   the look is preserved.
5. **`Step(float dt)` - the single per-frame pass** over every blade (slab b, index k -> flat i):
   - windTilt = sin(Time + windFreq*... + phase[i]) * windStrength, applied along windDir (a small
     XZ lean vector). Runs for ALL blades (the perf ceiling - escape hatch documented).
   - bendTarget = Vector2.zero. For each interactor in `GrassInteractor.Active`: d =
     distance(basePos[i].xz, interactor.posXZ); if d < interactor.Radius: falloff = 1 - d/Radius;
     awayDir = normalize(basePos[i].xz - interactor.posXZ); bendTarget += awayDir * falloff *
     interactor.Strength * bendStrength. **Early-out:** if NO interactor is in range AND bendState[i]
     is already ~zero, skip the MoveTowards (leave upright) - this is the "only near interactors" win.
   - bendState[i] = Vector2.MoveTowards(bendState[i], bendTarget, recoveryRate * dt). (When bendTarget
     is zero - interactor gone - this recovers toward upright at recoveryRate.)
   - Compose the lean: totalLean = windTilt(as XZ vector) + bendState[i]. Convert to a rotation ABOUT
     THE BASE: build `Rot = Quaternion` tilting the up-axis toward (totalLean.x, totalLean.y) - e.g.
     angle = atan-style magnitude, axis = perpendicular in XZ; OR the cheaper small-angle form
     `Quaternion.Euler(totalLean.y*k, 0, -totalLean.x*k)` tuned so lean magnitude maps to degrees.
     Then `renderSlabs[b][k] = Matrix4x4.TRS(basePos, Rot, scale*one) * <baseYawScale-rotation-only>`
     - i.e. rebuild T(basePos) * Rot(lean about base) * R(yaw) * S(scale). Since the base matrix
     already encodes yaw+scale+position, the clean form is: extract yaw+scale from the base matrix
     ONCE at construction (store `Quaternion baseYaw[i]`, `float baseScale[i]`), then each frame
     `renderSlabs[b][k] = Matrix4x4.TRS(basePos[i], Rot(lean) * baseYaw[i], baseScale[i]*one)`. This
     keeps the rigid lean a single rotation about the base pivot (y=0), exactly as locked.
6. Expose read accessors for verification: `public Vector2 GetBendState(int flatIndex)` and
   `public Matrix4x4[][] RenderSlabs => renderSlabs` + `int[] SlabCounts`. These satisfy success
   criterion #3 (read bendState/renderMatrices in C#, no GPU readback).

### GrassInteractField.cs (rework - second + final touch)

7. After `GrassScatter.Build`, construct `this.simulator = new GrassBendSimulator(scatterResult,
   config)`. In `RenderGrass` (both play LateUpdate + edit EditorRenderTick): compute dt
   (Time.deltaTime in play, a fixed 1/60 in edit - mirror the existing GrassTrampleMap pattern), call
   `this.simulator.Step(dt)`, then `this.grassRenderer.Render(lodRef, simulator.RenderSlabs,
   simulator.SlabCounts, scatterResult.WorldBounds)`. Rebuild reconstructs the simulator.

## In-editor verification gate

1. `read_console`: ZERO compile errors.
2. **Wind:** in edit AND play, all blades sway gently out of lockstep (no interactor needed). Confirm
   in both Game + Scene view.
3. **Interactor lean + recovery:** the demo orbiting effector visibly leans blades AWAY from it as it
   passes, and they recover to upright behind it.
4. **C# verification (no GPU readback):** add a temporary debug readout (removed after) logging
   `simulator.GetBendState(i)` for a blade under the effector - it must be non-zero while the effector
   is on it and decay toward zero after it leaves. This is the objective pass/fail for criterion #3.
5. **Perf:** at the demo 20k blades, the per-frame Step stays within frame budget (eyeball steady
   framerate; optionally log Step elapsed ms once/sec). Note 50k as the documented soft ceiling.

## Rollback

Back up `GrassInteractor.cs` + `GrassInteractField.cs` into `plans/grass-cpu-bend/_backup/phase-3/`;
`GrassBendSimulator.cs` is new (delete to revert). Restoring the two files + deleting the simulator
returns to the static-render Phase 2 state. The old GrassTrampleMap is still present (not yet deleted),
so the interactor could be reverted to its trample registration if needed.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Per-frame rebuild blows frame budget at scale | 3 | 3 | 9 | Early-out bend math + skip-recovery for upright blades; reuse renderSlabs (zero GC); precompute phase + baseYaw/baseScale once. Document 50k soft ceiling + wind-in-shader escape hatch. |
| Lean rotates from wrong pivot (not the base) | 2 | 4 | 8 | Blade pivot at y=0 (Phase 0 verified); compose TRS as T(basePos)*Rot*baseYaw with basePos at the blade base, so Rot is about y=0. Verify a leaned blade roots stay planted. |
| Rigid lean reads stiff | 3 | 2 | 6 | Locked tradeoff. Optional slight scale-squash on heavy bend; tune windStrength/bendStrength/recoveryRate in the config. Not a blocker. |
| Interactor registry races edit-mode domain reload (stale entries) | 2 | 2 | 4 | Mirror GrassTrampleMap pattern: RemoveAll(null) at the top of Step before iterating Active; OnDisable removes. |
| Small-angle Euler lean distorts at large bend | 2 | 2 | 4 | Clamp total lean magnitude to a max angle; if distortion shows, switch to the axis-angle form noted in step 5. |
