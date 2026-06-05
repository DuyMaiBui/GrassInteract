# Phase 0: Pre-delete reference sweep + baseline

**Effort: S** | **Blocked by: nothing** | **Blocks: all phases**

## Goal

Establish a safe-to-refactor baseline: enumerate every call-site of the types/globals slated for
deletion (so Phase 4 leaves nothing dangling), confirm the blade mesh pivot is at y=0 (so rigid lean
about base needs no offset bake), and snapshot the current working render as a regression reference.
No production code changes in this phase.

## File ownership

- READ-ONLY this phase. Produces one artifact: `plans/grass-cpu-bend/baseline.md` (the sweep
  inventory + render baseline). No `Assets/` files are modified.

## Concrete steps

1. **Reference sweep** (per development-principles "Pre-Delete Reference Check"). Run the grep across
   all source + scene files for every doomed symbol:
   ```
   grep -rln -E "GrassTrampleMap|GrassChunk|ChunkGrid|GrassInteractDeform|TrampleUpdate|_GrassTrample|_GrassWind|_GrassBend|_GrassFlatten|BindGlobals|_GrassFieldRect" \
     Assets/GrassInteract --include=*.cs --include=*.shader --include=*.hlsl --include=*.unity
   ```
   Record the hit list in `baseline.md`. Known referencing files (verified this session):
   - `Runtime/GrassTrampleMap.cs` (DELETE), `Runtime/GrassChunk.cs` (DELETE),
     `Runtime/ChunkGrid.cs` (DELETE), `Shaders/GrassInteractDeform.hlsl` (DELETE),
     `Shaders/TrampleUpdate.shader` (DELETE).
   - `Runtime/GrassInteractField.cs` (REWORK - drops trample/wind/bend globals + `BindGlobals`).
   - `Runtime/GrassRenderer.cs` (REWORK - drops per-chunk loop; doc-comment mentions trample).
   - `Runtime/GrassInteractor.cs` (REWORK - drops `GrassTrampleMap.Register/Unregister`).
   - `Runtime/GrassFieldSpace.cs` (REWORK - drops `BindGlobals` + `_GrassFieldRect`; keeps `WorldToUv`).
   - `Runtime/GrassLODConfig.cs` (REWORK - wind/bend become C# tunables; add `recoveryRate`).
   - `Runtime/GrassLayer.cs` (doc-comment only references ChunkGrid - update comment, no logic change).
   - `Editor/GrassInteractDemoBuilder.cs` (REWORK - removes GrassTrampleMap creation).
   - `Editor/GrassPainterWindow.cs` (uses `GrassFieldSpace` ctor + `WorldToUv` + `field.Rebuild()` -
     ALL KEPT; verified it does NOT call `BindGlobals` and references no deleted type. No change needed
     beyond confirming this in the sweep.).
   - `Demo/GrassInteractDemo.unity` (has a GrassTrampleMap component - removed via Phase 4 rebuild,
     NOT hand-edited YAML).
2. **Confirm GrassPainterWindow safety.** Grep just that file to confirm its only `GrassFieldSpace`
   usage is the ctor + `WorldToUv` (kept) and it calls `field.Rebuild()` (kept). Record verdict.
3. **Verify blade pivot at y=0.** Read `Editor/GrassBladeMeshBuilder.cs`: confirm `BLADE_HEIGHT = 1.0`
   and the vertex loop runs `y = t * BLADE_HEIGHT` with `t` in [0,1] (base row at y=0). This proves
   rigid lean about base needs NO offset bake. Record one line: "pivot at y=0 confirmed - no offset
   bake".
4. **Render baseline snapshot.** Open the demo scene (`Demo/GrassInteractDemo.unity`) in the current
   (pre-refactor) state. With the field live in Scene + Game view (edit mode), record into
   `baseline.md`: visible blade count target (demo layer = 20000 targetInstances, seed 12345,
   fieldBounds 40x40, scaleRange 0.7..1.3), the resulting kept-blade count if observable, batch/slab
   count, and whether grass renders in BOTH views. Capture the first 8 placement matrices if cheaply
   readable (a tiny throwaway `Debug.Log` of `ChunkGrid.Build` output first slab is acceptable; remove
   it after). This is the byte-stable-placement reference Phase 2 checks against.

## In-editor verification gate

- `baseline.md` exists and lists: (a) the full sweep hit-list with per-file disposition
  (DELETE/REWORK/KEEP/comment-only), (b) "pivot at y=0 confirmed", (c) the render baseline
  (renders in both views = yes; demo seed/bounds/count; first-N matrices if captured).
- No `Assets/` file content changed (any throwaway `Debug.Log` added for matrix capture is removed;
  `read_console` shows no new errors).

## Rollback

Read-only phase. If a throwaway log was added for matrix capture, delete that line. Nothing else to
revert.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Sweep misses a reference (dangling symbol surfaces in Phase 4) | 2 | 3 | 6 | Use the exact alternation regex above across cs+shader+hlsl+unity; cross-check against the known list; Phase 4 re-runs the same sweep post-delete to confirm zero hits. |
| Blade pivot is NOT at y=0 (lean pivots wrong) | 1 | 4 | 4 | Verified this session it IS at y=0; this step is a confirm, not a discovery. If somehow false, add a base-offset bake in Phase 2 scatter (one matrix pre-multiply). |
| Baseline placement not captured -> Phase 2 drift undetectable | 2 | 3 | 6 | Capture demo seed/bounds/count at minimum (deterministic inputs); first-N matrices are a bonus check. Phase 2 can re-derive expected placement from the same seeded draw order. |
