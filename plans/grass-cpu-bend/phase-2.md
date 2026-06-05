# Phase 2: Flat scatter + single-LOD renderer

**Effort: L** | **Blocked by: Phase 1** | **Blocks: Phase 3**

## Goal

Replace the spatial-chunk model with a FLAT instance list. Build-time scatter into
`Matrix4x4[] baseMatrices` (pivot at blade base, y=0) + parallel `Vector3[] basePositions`,
partitioned ONLY into the API-mandated <=1023 render slabs (reusing `InstanceBatchPool`). Rework
`GrassRenderer` to draw all slabs with ONE global-LOD mesh chosen by camera distance to the field
center, under ONE field-wide `worldBounds`. Wire `GrassInteractField` to build + render the flat set
STATICALLY (no motion yet) and DROP all shader-global binding. Placement MUST remain byte-stable vs
the Phase 0 baseline.

## File ownership

- `Runtime/GrassScatter.cs` (NEW - replaces ChunkGrid; ChunkGrid itself is deleted in Phase 4, kept
  compiling alongside until then).
- `Runtime/GrassRenderer.cs` (REWORK - flat slab loop, single global LOD, one bounds).
- `Runtime/GrassInteractField.cs` (REWORK - build via GrassScatter; drop chunks + all shader globals
  + `GrassFieldSpace.BindGlobals`; render flat slabs statically).
- `Runtime/GrassFieldSpace.cs` (REWORK - DROP `BindGlobals` + the `_GrassFieldRect` global +
  `FieldRectId`; KEEP `MinXZ/SizeXZ/WorldToUv/UvToWorld`).
- `Runtime/GrassLODConfig.cs` (REWORK - keep wind+bend fields as plain tunables; ADD `recoveryRate`
  float; keep LOD meshes/distances, shadow, bounds fields). The C# consumers of wind/bend/recovery
  are added in Phase 3 - this phase only adds the field + accessor so the API is ready.

## Concrete steps

### GrassScatter.cs (new)

1. Mirror `ChunkGrid.Build` signature: `public static GrassScatterResult Build(GrassLayer layer,
   Vector3 origin, InstanceBatchPool pool)`. Define a small `GrassScatterResult` carrying
   `Matrix4x4[][] baseSlabs`, `int[] slabCounts`, `Vector3[][] basePositionSlabs` (parallel to
   baseSlabs), the total blade count, and the field-wide `Bounds worldBounds`. Keeping positions
   slab-parallel to matrices lets the simulator iterate slabs uniformly.
2. **Preserve the EXACT seeded draw order** from ChunkGrid so placement is byte-stable: per candidate,
   draw in this order - localX = rng.NextDouble()*bounds.x - halfX, localZ = same, accept =
   rng.NextDouble(); reject if accept > density (density via `GrassFieldSpace.WorldToUv` +
   `densityMap.GetPixelBilinear`); then yaw = rng.NextDouble()*360, scale = Lerp(minScale, maxScale,
   rng.NextDouble()). Same `new System.Random(layer.Seed)`. Keep the ground-snap raycast
   (RAY_START_HEIGHT 1000, RAY_MAX_DISTANCE 5000, groundMask) with the field-plane-Y fallback + the
   one-time no-hit warning.
3. Build the base matrix as `Matrix4x4.TRS(worldPos, Quaternion.Euler(0, yaw, 0), scale*Vector3.one)`
   - this is the baseYawScale with pivot at the blade base (y=0). Store worldPos into the parallel
   basePositions. Do NOT bucket into spatial chunks - append to a single flat list.
4. Partition the flat list into <=1023 slabs via `InstanceBatchPool.Rent()` (matrices) plus plain
   `new Vector3[1023]` position slabs (positions are not pooled - build-time only, read every frame;
   sized to match). Last slab is partial; track slabCounts.
5. Compute ONE field-wide worldBounds: XZ = the field rect (origin +/- halfBounds) expanded by lateral
   pad; Y spans [minSnappedY, maxSnappedY + bladeReachY] where
   bladeReachY = config.MaxBladeHeight*maxScale + config.BendHeadroom (covers bent+wind headroom so
   nothing is wrongly culled). Track min/max snapped Y across all kept blades.
6. Add a `ReturnSlabs(GrassScatterResult, InstanceBatchPool)` mirroring `ChunkGrid.ReturnSlabs` to
   recycle the matrix slabs on rebuild (position slabs are GC-dropped).

### GrassRenderer.cs (rework)

7. Constructor: keep the LOD-distance precompute + mesh snapshot + material RenderParams
   (rp.camera = null, shadow mode, receiveShadows = false). Drop nothing from the param setup.
8. New `Render(Vector3 lodReferencePos, Matrix4x4[][] renderSlabs, int[] slabCounts, Bounds worldBounds)`:
   - Pick ONE LOD mesh for the WHOLE field: sqrDist = (fieldCenter - lodReferencePos).sqrMagnitude ->
     SelectLod. Field center comes from worldBounds.center or is passed in.
   - Set rp.worldBounds = worldBounds ONCE (field-wide).
   - Loop all slabs: `Graphics.RenderMeshInstanced(rp, mesh, 0, renderSlabs[b], slabCounts[b])`.
   - PRESERVE the critical discipline (keep the class doc-comment lessons): do NOT set rp.matProps
     (silently renders nothing under RenderGraph); caller drives this from the player loop.
   - In Phase 2 the renderSlabs ARE the baseSlabs (static). Phase 3 swaps in the simulator output.

### GrassInteractField.cs (rework)

9. Replace chunks + ChunkGrid.Build with GrassScatterResult + GrassScatter.Build. Remove ALL
   shader-global PropertyToID fields + BindDeformGlobals + the GrassFieldSpace(...).BindGlobals() call
   + the default-black trample-texture seed. Keep WarnIfMultipleEnabledFields (still one field per
   scene for sanity - update its message: the reason is now the simulator, not shader globals). Keep
   ExecuteAlways, the OnEnable/OnDisable/OnDestroy editor-tick wiring, LateUpdate (play) +
   EditorRenderTick (edit). In Phase 2 RenderGrass renders the STATIC baseSlabs via the reworked
   renderer. Update OnDrawGizmosSelected to draw the single field-wide bounds (drop per-chunk cubes).

### GrassFieldSpace.cs + GrassLODConfig.cs

10. GrassFieldSpace: delete BindGlobals, FieldRectId, and the _GrassFieldRect doc reference; keep the
    rest. GrassLODConfig: add `[SerializeField] private float recoveryRate = 4f;` with a tooltip + a
    `public float RecoveryRate => this.recoveryRate;` accessor; keep all existing fields + accessors
    (wind/bend now feed C#, not shader). Update the trample-section tooltips to say "consumed by
    GrassBendSimulator (C#)".

## In-editor verification gate

1. `read_console`: ZERO compile errors (ChunkGrid/GrassChunk still exist and compile; nothing dangles).
2. Demo scene renders the flat grass set STATICALLY in BOTH Game + Scene view, edit AND play.
3. **Byte-stable placement check:** with demo seed 12345 / bounds 40x40 / scaleRange 0.7..1.3 /
   target 20000, the kept-blade count and the first 8 baseMatrices MATCH the Phase 0 baseline
   (temporary Debug.Log of the first slab, removed after the check). If they differ, the draw order
   drifted - fix before proceeding.
4. One field-wide bounds is drawn by the gizmo; grass is not wrongly culled when the camera frames the
   field edge.

## Rollback

Back up the four reworked files into `plans/grass-cpu-bend/_backup/phase-2/`; `GrassScatter.cs` is new
(delete it to revert). ChunkGrid/GrassChunk are still present, so restoring the four files returns to
the Phase 1 state cleanly.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Placement drifts vs baseline (seed draw-order changed) | 3 | 4 | 12 | Replicate the EXACT NextDouble() draw sequence; verify first-8 matrices + kept count byte-equal vs Phase 0 baseline before proceeding. |
| Removing shader globals breaks the still-present GrassTrampleMap (reads _GrassFieldRect) | 2 | 3 | 6 | GrassTrampleMap is deleted in Phase 4; in Phase 2 it may log "rect zero" harmlessly. If noisy, disable that component in the demo scene until Phase 4 (do NOT delete yet). Note this in the verify step. |
| Single field-wide bounds wrongly culls edge blades | 2 | 3 | 6 | Bounds Y spans actual min/max snapped Y + bladeReachY; XZ expanded by lateral pad. Verify at camera framing the field edge. |
| Position slabs double memory (parallel to matrices) | 2 | 1 | 2 | Vector3 slabs at 20k blades ~240KB - negligible. Documented as the cost of CPU-readable interaction. |
