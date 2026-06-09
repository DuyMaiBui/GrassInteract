# Plan: ScatterLayer Split + InstanceLayer Rigid Tilt

Supersedes `plans/scatterlayer-composition-refactor.md` (Part A) and extends it with Part B.
Design source: `plans/reports/scatterlayer-split-instance-tilt-brainstorm.md`.

## Objective
- **Part A** — Finish the composition refactor: each concrete layer embeds the already-`[Serializable]`
  config structs as real fields, removing the ~22-field triplication. Two independent layers sharing
  feature *structs*, not a base.
- **Part B** — Give `InstanceScatterLayer` instances a whole-instance **rigid tilt-away + recover**
  response to moving interactors, simulated in C# (Burst), drawn via GPU instancing, scaling to tens
  of thousands. Replace `MeshScatterEngine` with a new `InstancedPropEngine`.

## Locked decisions
- Keep class names `DensityScatterLayer` / `InstanceScatterLayer`.
- Density grass interactor behavior UNCHANGED (`GrassBendSimulator` untouched).
- No serialization migration — wipe `Demo/TerrainScatterConfig.asset` config, re-author fresh.
- Tilt = rigid tilt away + timed spring-back (`recoveryRate`); recovery state persists in C# NativeArray.
- Compute in C# Burst `IJobParallelFor`; draw GPU-instanced; reuse unchanged `GrassCull.compute`.
- Replace `MeshScatterEngine` entirely; port `InstanceColliderPool` + `InstanceFrustumCuller`; collider-tilt optional (default off).
- Add `com.unity.burst` + `com.unity.mathematics` as explicit manifest deps.

## Approach refinement (from routing read)
Engine routing today is keyed on `layer.InteractsWithDeform`, and `MeshScatterEngine` is the *no-deform*
path. The new engine makes routing **type-based**: `InstanceScatterLayer → InstancedPropEngine`;
`DensityScatterLayer → existing grass tiers`. `InstancedPropEngine` is a **fork of `MeshScatterEngine`**
(keep its `ChunkedInstanceBuffer` bake + `GrassCull.compute` cull + `RenderMeshIndirect` + collider pool)
PLUS a Burst tilt sim that writes a **compact per-instance tilt buffer** (one quaternion/axis-angle per
instance) the vertex shader applies as a rigid rotation about the instance pivot. Base transforms stay
static (no full re-upload); only the small tilt buffer uploads per frame. Cull AABB margin is expanded by
the max-tilt reach so a tilted instance never pops.

> Implementation note: **compact tilt buffer** is the recommended primary (best bandwidth). **Full
> per-frame matrix re-upload** is the simpler fallback if shader-side tilt proves fiddly — both honor
> "simulate in C#, draw GPU-instanced". Cook may pick the fallback only with a logged reason.

---

## Phase 1 — Part A: Embed config structs (kill triplication)
**Files:** `DensityScatterLayer.cs`, `InstanceScatterLayer.cs` (own); `ScatterLayer.cs` (read-only verify).
- Replace the ~22 loose duplicated fields in EACH concrete layer with embedded serialized structs:
  `[SerializeField] private ScatterRenderConfig render; ... wind; deform; bounds; placement;`
- Accessors return the field directly: `public override ScatterWindConfig Wind => this.wind;`
  (struct is now SSOT; no per-access `new`).
- Keep type-specific fields (Density: densityMap/targetInstances/procedural; Instance: authored/collider).
- Re-apply Odin `[BoxGroup]` placement via struct fields (already on the structs).
- Drop `FormerlySerializedAs` on the moved fields (no migration — assets re-authored in Phase 9).
**Verify:** compile clean; both layers expose all abstract accessors.

## Phase 2 — Part A: `ScatterInstanceTiltConfig` (Instance only)
**Files:** new `ScatterInstanceTiltConfig.cs`; `InstanceScatterLayer.cs`.
- New `[Serializable] struct ScatterInstanceTiltConfig { bool affectedByInteractors; float tiltStrength;
  float maxTiltAngle; float recoveryRate; float radiusPadding; bool colliderFollowsTilt; }`.
- `InstanceScatterLayer` composes it (`[SerializeField] private ScatterInstanceTiltConfig tilt;`) + accessor.
- `DensityScatterLayer` does NOT get it (reinforces independence).
**Verify:** compile; Density unchanged.

## Phase 3 — Manifest deps
**Files:** `Packages/manifest.json`.
- Add `com.unity.burst` + `com.unity.mathematics` (versions compatible with URP 17.3 / Unity 6).
**Verify:** package resolve clean; no version conflicts.

## Phase 4 — Part B: Burst tilt simulator
**Files:** new `InstanceTiltSimulator.cs` (+ `InstanceTiltJob` Burst struct).
- Persistent `NativeArray<TiltState>` (base pivot pos, current tilt axis-angle/quat) sized to instance count.
- Per-frame `IJobParallelFor`: for each instance gather tilt-away from in-range interactors (interactors
  uploaded to a `NativeArray<InteractorGpu>`), `MoveTowards` toward target at `recoveryRate`; write the
  compact tilt into an output `NativeArray` mirrored to a `GraphicsBuffer`.
- Tilt math = port of `GrassBendSimulator.LeanRotation` (small-angle, clamped to `maxTiltAngle`).
**Verify:** edit-mode unit check — a synthetic interactor near one instance produces non-zero tilt that
decays to zero over `recoveryRate` after removal.

## Phase 5 — Part B: `InstancedPropEngine` (replaces MeshScatterEngine)
**Files:** new `InstancedPropEngine.cs` (fork of `MeshScatterEngine`); delete `MeshScatterEngine.cs` in Phase 8.
- Keep `ChunkedInstanceBuffer` bake + `GrassCull.compute` cull + per-LOD `RenderMeshIndirect`.
- Own an `InstanceTiltSimulator`; `Step(dt)` advances the Burst sim; `Submit` uploads the interactor
  buffer + tilt buffer and binds it (`Shader.SetGlobalBuffer`) before the indirect draws.
- Expand the cull AABB margin by max-tilt reach so tilted instances never pop.
**Verify:** compile; render parity vs old MeshScatterEngine when `tiltStrength=0` (static look unchanged).

## Phase 6 — Part B: shader rigid tilt
**Files:** `ScatterInstanced.shader` (+ any shared HLSL include).
- Read the per-instance tilt buffer (indexed by instance id) and apply a rigid rotation about the
  instance pivot BEFORE the existing transform path; keep the existing per-vertex wind bend intact.
**Verify:** in Play, a moving interactor visibly tilts nearby props away; they spring back over `recoveryRate`.

## Phase 7 — Part B: collider port + optional collider tilt
**Files:** `InstancedPropEngine.cs`, `InstanceColliderPool.cs` (read), `InstanceFrustumCuller.cs` (read).
- Port the pooled + culled collider runtime from MeshScatterEngine into the new engine.
- When `colliderFollowsTilt` is true, the culler composes the per-instance tilt (from the sim's NativeArray)
  into the collider transform; default off → colliders stay at base.
**Verify:** colliders present/culled as before; with toggle on, a tilted prop's collider matches the visual.

## Phase 8 — Routing + delete MeshScatterEngine
**Files:** `ScatterField.cs`; delete `MeshScatterEngine.cs`.
- Route `InstanceScatterLayer → InstancedPropEngine` (type-based); remove the `!InteractsWithDeform →
  MeshScatterEngine` branch in `Rebuild` + `RebuildLayer`.
- Ensure a no-deform `DensityScatterLayer` still routes to the grass tiers (GPU/CPU) — no orphan path.
- Pre-delete reference grep for `MeshScatterEngine` across runtime + tests + editor.
**Verify:** compile; no dangling references; both layer types build engines.

## Phase 9 — Demo re-author + validation
**Files:** `Demo/TerrainScatterConfig.asset`, demo scene.
- Wipe stale config on the asset; re-author Density + Instance layers fresh with tuned values.
- Compile via MCP `read_console` (full error set); runtime validate: interactor moves across the instance
  field → props tilt away + recover; density grass behavior byte-unchanged.
**Verify:** zero console errors; both behaviors confirmed in Play; perf sane at target instance count.

---

## Risk Assessment
| Risk | Likelihood | Impact | Score | Mitigation |
|------|-----------|--------|-------|------------|
| Per-frame tilt-buffer upload bandwidth at tens of thousands | 3 | 3 | 9 | Compact tilt buffer (1 quat/instance); dirty-region/sparse upload as follow-up; GPU-compute stateful tilt as escape hatch (HLSL, no upload) |
| Shader-side rigid tilt math wrong (pivot/order) | 3 | 4 | 12 | Parity test at tiltStrength=0; fallback to full-matrix re-upload path (documented) |
| Cull pops on tilt (AABB too tight) | 3 | 3 | 9 | Expand chunk AABB margin by max-tilt reach; verify at max maxTiltAngle |
| Embedding structs silently resets demo asset values | 5 | 2 | 10 | Expected — Phase 9 re-authors; communicate "values reset" up front |
| Burst not resolving (transitive-only today) | 2 | 4 | 8 | Phase 3 adds explicit deps; verify resolve before Phase 4 |
| Deleting MeshScatterEngine breaks no-deform Density path | 2 | 4 | 8 | Phase 8 routes no-deform Density to grass tiers; pre-delete grep + tests |

## Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| 1: Embed structs | S | Mechanical; both layers |
| 2: Tilt config | S | Instance only |
| 3: Manifest deps | S | Verify resolve before P4 |
| 4: Burst sim | M | New Burst job + state mgmt; unit-checkable |
| 5: InstancedPropEngine | M | Fork MeshScatterEngine + wire sim |
| 6: Shader tilt | M | Highest-risk math (pivot/order) |
| 7: Collider port | S | Mostly move + optional toggle |
| 8: Routing + delete | S | Grep-gated deletion |
| 9: Demo + validate | M | Re-author + full Play validation |
| Total | ~L (1wk) | Critical path: 1→3→4→5→6→8→9 (P2,P7 parallelizable) |

## Cook handoff
`/t1k:cook plans/scatterlayer-split-instance-tilt-plan.md`
