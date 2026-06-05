# Phase H -- Instance runtime: pooled MeshColliders + frustum culling

- Effort: S
- Parallel-safe with: F + G after A. Runtime-only -- independent of editor stack.
- Blocks: I (validation runs Play-mode collider hit test)

## Scope

Replace MeshScatterEngine's `BuildColliders` in-engine GameObject spawn with a runtime-owned `InstanceColliderPool` (pooled MeshCollider GameObjects, per-layer cap, prewarmed) + `InstanceFrustumCuller` (per-frame XZ-cell + frustum filter to only activate colliders near the camera).

Mandatory pattern: `rules/mono-pool-spawn-unity.md` requires using `TheOne.Pooling.IObjectPoolManager` for Mono spawns. Check first whether TheOne.Pooling is available in this project; if not, this phase ships a focused single-purpose pool (NOT a general object pool) and adds a `// TODO[lib]` note for migration to the shared infrastructure.

## File ownership

- NEW: `Assets/GrassInteract/Runtime/InstanceColliderPool.cs` (MonoBehaviour OR static container -- decision in H.1).
- NEW: `Assets/GrassInteract/Runtime/InstanceFrustumCuller.cs`.
- Modify: `Assets/GrassInteract/Runtime/MeshScatterEngine.cs` -- replace inline `BuildColliders` GameObject creation with a delegation to `InstanceColliderPool.RebuildFor(layer, scatter)` + register the pool with the culler.

## Pre-conditions

- Phase A merged (per-record collider fields exist).
- Determine if `TheOne.Pooling.IObjectPoolManager` is available:
  - Check `Packages/manifest.json` for any `com.theone.pooling` package.
  - If present: H uses it per the kit rule.
  - If absent: H ships a private pool with a documented migration path.

## Step-by-step tasks

### H.0 -- TheOne.Pooling availability probe

1. `grep -rln "TheOne.Pooling" Assets/ Packages/manifest.json` -- if any hits, use shared pool.
2. Document choice in EDITOR-UI-GUIDE.md (Phase I).

### H.1 -- InstanceColliderPool

1. `InstanceColliderPool` -- a MonoBehaviour parented under the ScatterField's runtime GO. One pool instance per layer.
2. Configured at Build time with: `(InstanceScatterLayer layer, int prewarmCount, int hardCap)`. `prewarmCount` derived from `Mathf.Min(layer.AuthoredInstances.Count, hardCap)`. `hardCap` from a new config field `layer.MaxActiveColliders = 256` default.
3. Per-record metadata: position, rotation, scale, mesh (override or default), convex. Stored as a struct array.
4. API: `void EnableCollidersForRecords(NativeArray<int> recordIndices)` -- enables N pooled GOs at the matching records' transforms. `void DisableAll()`.
5. Internal: a stack of pooled inactive GOs; on Enable, pop + position + activate; on Disable, deactivate + push back.

### H.2 -- InstanceFrustumCuller

1. Per-frame component running in LateUpdate.
2. Maintains a NativeArray of (recordIndex, position) tuples on Build.
3. Each frame: get Camera.main; build frustum planes (`GeometryUtility.CalculateFrustumPlanes` -- non-alloc form); compute the subset of records whose XZ position falls inside the frustum AND within `layer.MaxColliderDistance` (new layer field, default 50m); cap at `hardCap`.
4. Sort by distance to camera if cap is hit -- closest N kept.
5. Pass the resulting indices to `InstanceColliderPool.EnableCollidersForRecords`.
6. Cell-grid optimization: bucket records into 4m XZ cells at Build; only test cells whose centre is inside the frustum.

### H.3 -- MeshScatterEngine wiring change

1. In `Build`: if `layer is InstanceScatterLayer instLayer` AND `instLayer.AnyRecordWantsCollider()` (new helper), spawn the pool + culler under the field's GO.
2. Delete the old `BuildColliders` inline GameObject creation loop. Document the move in MeshScatterEngine.cs's class-comment.
3. In `Dispose`: tear down the pool + culler GameObjects via SafeDestroy.

### H.4 -- New ScatterLayer accessor + InstanceScatterLayer field

1. Add `[SerializeField] private int maxActiveColliders = 256;` + `[SerializeField] private float maxColliderDistance = 50f;` on `InstanceScatterLayer`.
2. Public accessors `MaxActiveColliders`, `MaxColliderDistance`. Read by H.1 + H.2.
3. Update E's DefaultsSection (Phase E -- coordinate via E's PR or follow-up commit if E already merged) to include these two fields. If E already shipped, file a small follow-up in H.

## Validation criteria

1. Compile clean.
2. Play-mode smoke: scene with InstanceScatterLayer holding 100 records, half with `generateCollider = true`. Enter Play mode. Confirm MeshCollider GameObjects appear (count <= maxActiveColliders). Move the camera away -- count drops. Move camera back -- count rises.
3. Raycast test: spawn a runtime cube, drop it onto a record's expected position; verify it lands on top (collider works).
4. Performance: 5000-record layer should not drop below 60 FPS on a desktop dev rig. Profile with Unity Profiler 30s capture; report GC.Alloc spikes if any.
5. Edit mode: confirm NO collider GameObjects spawn in edit mode (pool gated behind `Application.isPlaying`).
6. Commit before summary.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Pool prewarm at Build spikes frame time on large layers | 3 | 3 | 9 | Prewarm in coroutine batches (e.g. 32/frame); records pre-pool over multiple frames. |
| Per-frame frustum culling allocates via Camera.main lookup | 3 | 2 | 6 | Cache Camera.main once; subscribe to `Camera.onPostRender` to invalidate; mention in comment. |
| Sort-by-distance allocates on cap-hit | 2 | 2 | 4 | Use `NativeSortExtension.Sort` on the NativeArray; no managed alloc. |
| Pool not cleaned up on scene unload -> leaked GameObjects | 3 | 4 | 12 | InstanceColliderPool parent itself is destroyed in MeshScatterEngine.Dispose; pool's OnDestroy releases all children. Add EditMode test that loads + unloads a scene 3 times and asserts zero leaked GameObjects via `GameObject.FindObjectsByType<MeshCollider>()`. |

## Effort: S

Estimate 2-3 hours. Self-contained; runtime-only; bounded risk surface.
