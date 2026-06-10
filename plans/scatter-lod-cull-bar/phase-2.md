# Phase 2 — Cull fix across 3 engines + shared compute uniform

Effort: **M** · Blocked by Phase 1 (`RenderCullDistance` accessor) · Parallel-safe with Phase 3 (disjoint files).

## Objective

Replace the hidden derived cull formula (`Mathf.Max(lod1MaxSqrDist * 4f, minCullSqr)`, with an `Application.isPlaying`
branch making edit ≠ play) with the explicit `RenderCullDistance²` in all three engines. After this, a layer with
`renderCullDistance = 500` culls at exactly 500m, and edit-mode == play-mode.

## Files owned

- `Assets/GrassInteract/Runtime/InstancedPropEngine.cs`
- `Assets/GrassInteract/Runtime/GrassGpuEngine.cs`
- `Assets/GrassInteract/Runtime/GrassRenderer.cs`
- `Assets/GrassInteract/Shaders/GrassCull.compute` — **NO source edit required.** The `maxCullSqrDistance` uniform
  (declared line 46) and its `SetComputeFloatParam` feed already exist in both GPU engines. Only the C# *value* feeding
  it changes. Listed here for ownership/awareness only — confirm no edit lands in it.

## Change instructions

### 1. `InstancedPropEngine.cs` — replace the derived cull (lines 184-190)

The engine snapshots LOD distances and derives the cull. Current code:
```csharp
            float[] dists = layer.Render.LodMaxDistances;
            float d0 = dists.Length > 0 ? dists[0] : 12f;
            float d1 = dists.Length > 1 ? dists[1] : 30f;
            this.lod0MaxSqrDist = d0 * d0;
            this.lod1MaxSqrDist = d1 * d1;
            float minCullSqr = Application.isPlaying ? 250000f : 1e8f;
            this.maxSqrDistance = Mathf.Max(this.lod1MaxSqrDist * 4f, minCullSqr);
```
Replace the last two lines (189-190) with:
```csharp
            // Explicit per-layer cull boundary (SSOT: ScatterRenderConfig.RenderCullDistance). Edit == play — no isPlaying branch.
            float cull = layer.Render.RenderCullDistance;
            this.maxSqrDistance = cull * cull;
```
> Keep lines 184-188 (the `dists`/`d0`/`d1`/`lod0MaxSqrDist`/`lod1MaxSqrDist` LOD-switch snapshot) unchanged — those are
> the LOD0→1 and LOD1→2 switches, still sourced from `LodMaxDistances`. Only the FAR cull derivation changes.
> `this.maxSqrDistance` still flows to the compute uniform unchanged at line 329 → 515 → `SetComputeFloatParam(..., "maxCullSqrDistance", maxSqrDistance)` (line 537).

### 2. `GrassGpuEngine.cs` — replace the derived cull (lines 204-210)

Current code:
```csharp
            float[] dists = render.LodMaxDistances;
            this.lod0MaxSqrDist = dists.Length > 0 ? dists[0] * dists[0] : 144f;  // default 12m
            this.lod1MaxSqrDist = dists.Length > 1 ? dists[1] * dists[1] : 900f;  // default 30m
            // Editor: generous distance so SceneView zoom-out doesn't hide everything.
            // Play: 500 m minimum coarse cull beyond the last LOD boundary.
            float minCullSqr = Application.isPlaying ? 250000f : 1e8f;
            this.maxSqrDistance = Mathf.Max(this.lod1MaxSqrDist * 4f, minCullSqr);
```
Replace lines 207-210 (the two comment lines + the two cull lines) with:
```csharp
            // Explicit per-layer cull boundary (SSOT: ScatterRenderConfig.RenderCullDistance). Edit == play — no isPlaying branch.
            float cull = render.RenderCullDistance;
            this.maxSqrDistance = cull * cull;
```
> Keep lines 204-206 (LOD0/LOD1 squared-switch snapshot) unchanged. `this.maxSqrDistance` flows to the uniform unchanged
> at line 368 → 595 → `SetComputeFloatParam(..., "maxCullSqrDistance", maxSqrDistance)` (line 619). Both GPU engines now
> feed the SHARED `GrassCull.compute` `maxCullSqrDistance` from `RenderCullDistance²` — the shared-shader risk is closed by
> doing both in this one phase.

### 3. `GrassRenderer.cs` — add an explicit far-cull boundary (CPU path)

`GrassRenderer` selects ONE LOD for the whole field by field-center distance (`SelectLod`, lines 112-121) and has no
explicit far cull today (the last LOD "covers all remaining distances"). Give it the explicit cull so the whole field is
skipped past `RenderCullDistance`.

a. Snapshot the cull in the constructor. After line 66 (the squared-threshold precompute loop) add:
```csharp
            float cull = render.RenderCullDistance;
            this.cullSqrDistance = cull * cull;
```
b. Add the backing field next to `lodMaxSqrDistances` (after line 35):
```csharp
        private readonly float cullSqrDistance;
```
c. In `Render(...)` (after line 99 computes `sqrDist`), skip the whole field when beyond cull:
```csharp
            float sqrDist = (worldBounds.center - lodReferencePos).sqrMagnitude;
            if (this.cullSqrDistance > 0f && sqrDist > this.cullSqrDistance)
                return; // whole field beyond explicit cull distance
            Mesh mesh = this.lodMeshes[this.SelectLod(sqrDist)];
```
> The `> 0f` guard preserves the legacy "never cull" behavior if an un-migrated asset somehow reaches the CPU path with
> cull == 0 (defensive; Phase 1 migration should make this unreachable). `SelectLod` itself is unchanged.

## Post-edit grep gate (edit == play proof)

After all three edits, prove no stale derivation remains:
```
grep -rn "isPlaying\|minCullSqr\|lod1MaxSqrDist \* 4\|250000f\|1e8f" Assets/GrassInteract/Runtime/
```
Expected: ZERO hits inside the cull-derivation paths of these three files (other unrelated `isPlaying` uses, if any,
are fine — inspect each).

## Verification steps (verify ONCE after batching all 3 edits)

1. Compile clean (`read_console` → 0 errors). No `GrassCull.compute` recompile is forced.
2. Set a test layer's `renderCullDistance = 500`. In the SceneView (edit mode), move the camera so an instance crosses 500m:
   it must be visible at ~499m and gone at ~501m — for BOTH a grass layer (`GrassGpuEngine`) and a prop layer
   (`InstancedPropEngine`).
3. Enter Play mode with the same layer/camera. The cull distance must be IDENTICAL to edit mode (no 1000m vs 500m,
   no 10000m editor floor). This is the bug's acceptance criterion.
4. For a CPU `GrassRenderer` field, confirm the whole field disappears past `renderCullDistance` and reappears within it.

## Per-phase risk

- **Shared-shader desync (score 12):** both GPU engines MUST land their `RenderCullDistance²` feed in the same change set so
  neither reads a stale `maxCullSqrDistance`. Verification step 2 exercises both a grass and a prop layer specifically.
- **Residual edit≠play (score 8):** the post-edit grep gate + verification step 3 are the guard. If any `isPlaying`
  cull branch survives, step 3 fails.
- Depends on Phase 1: if `RenderCullDistance` is 0 (un-migrated asset), step 2/3 would cull at 0. Run Phase 1 verification first.
