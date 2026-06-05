# Phase A -- Runtime types cleanup + migration deletion

- Effort: M
- Parallel-safe: No (root dependency for every other phase)
- Blocking risks: schema-migration (15), ScatterField-cascade (25). Both have IN-PHASE mitigations enumerated below.

## Scope

Strip Odin attributes from runtime types, redistribute fields per brainstorm §1, drop ScatterKind enum, collapse to single material, extend AuthoredInstanceRecord schema per D1 (hybrid), update MeshScatterEngine routing, fix ScatterField cascade, delete legacy migration scripts + demo assets.

## File ownership

- Modify: Runtime/ScatterLayer.cs, DensityScatterLayer.cs, InstanceScatterLayer.cs, AuthoredInstancesData.cs, TerrainScatterConfig.cs, MeshScatterEngine.cs, ScatterField.cs
- Modify: Editor/ScatterAssetPostprocessor.cs (type detection only -- C will append the naming-convention method later)
- Read-only verify: Runtime/GrassCpuEngine.cs, GrassGpuEngine.cs (confirm no references to layer.Kind / GrassMaterial / MeshMaterial remain after edit)
- DELETE: Editor/ScatterAssetMigrator.cs, MigrateScatterLayerTypes.cs, MigrateDeformModeToWindInteract.cs, ScatterFieldRebuildLayerHarness.cs
- DELETE: Demo/TerrainScatterConfig.asset + every sub-asset (Grass.asset, Rock.asset, Grass_data.asset, Rock_data.asset)
- DELETE: Demo/GrassInteractDemo.unity reference to the deleted config (or leave + log re-author warning)

## Pre-conditions

- Branch created off main.
- Working tree clean.
- `t1k modules update` already run if any kit-shipped rules referenced.
- Unity Editor compiling and running before changes -- baseline `read_console` shows zero errors.

## Step-by-step tasks

### A.0 -- HIGH-RISK gate: blob round-trip smoke (BEFORE schema edits)

Reason: Risk score 12 (schema migration; downgraded from 15 after D1 strict-V2 resolution removed the alternative-preservation requirement).

1. Create EditMode test `Tests/Editor/AuthoredInstancesDataBlobRoundtripTests.cs` (NEW; lives permanently under Tests/, not removed at phase end).
2. **V2 fresh round-trip test**: build a `List<InstanceRecord>` with 3 V2 records: (a) no overrides, (b) collider override mesh + scale set, (c) collider override with convex true. PackBlob -> UnpackBlob -> assert byte-equal AND record-equal.
3. **V1->V2 migration test**: hand-craft a V1 blob (no version header, 12-byte collider block, 12-byte renderer block per the legacy layout) containing 2 records: one with collider+renderer, one with renderer only. UnpackBlob -> assert: position/rotation/scale preserved, collider fields migrated (meshRefIndex preserved, convex preserved, generateCollider derived from old `enabled` bit), RendererOverride DROPPED without throwing, ONE warning logged per layer enumerating dropped record indices. Add a THIRD V1 record with non-uniform scale (1, 2, 3) -- assert it collapses to uniform 2.0 AND a second one-shot non-uniform-collapse warning fires.
4. Run BOTH tests before editing AuthoredInstancesData.cs. The V2 test will fail initially (expected); the V1 migration test runs against the in-memory legacy layout. After A.4, both pass.

### A.1 -- ScatterLayer.cs base class refactor

1. Remove `using Sirenix.OdinInspector;`.
2. Remove `ScatterKind` enum + `kind` field + `Kind` accessor.
3. Remove `grassMaterial`, `meshMaterial` fields + their accessors. Add single field: `[SerializeField, Tooltip("Render material. Pipeline is selected by InteractsWithDeform: true -> grass shader (InstancedGrass/IndirectGrass); false -> mesh-prop shader (ScatterInstanced).")] private Material? material;` and accessor `public Material? Material => this.material;`.
4. Add `[UnityEngine.Serialization.FormerlySerializedAs("grassMaterial")]` on the new `material` field so existing grass-layer assets resolve. (Mesh-layer assets resolved meshMaterial -> material via Validate's auto-fix step in C.)
5. MOVE the following fields OUT of ScatterLayer (delete here, will land on DensityScatterLayer in A.2):
   - fieldBounds, scaleRange, seed, slopeRange, splatLayerIndex, splatThreshold, rotationOffsetEuler, randomPitchRange, randomRollRange, alignToNormal.
6. DELETE collider fields entirely (generateColliders, colliderMesh, colliderConvex). Per brainstorm: density doesn't collide; instance has per-record + per-layer defaults that land on InstanceScatterLayer in A.3.
7. KEEP: affectedByWind, affectedByInteractors, material (above), shadowCastingMode, windMode + wind tunables, bend/trample, bounds, chunkSize, lods, groundSnapMask.
8. Replace every `[BoxGroup]`/`[ShowIf]`/`[TitleGroup]`/`[TabGroup]` attribute usage with plain `[SerializeField]` + `[Tooltip]` only. Editor UI handles grouping in UXML.
9. Remove the `Validate` check on `grassMaterial == null` (move to D's editor validation lambda).
10. Keep OnValidate + NotifyChanged unchanged (editor-only block).

### A.2 -- DensityScatterLayer.cs gains the moved placement fields

1. Remove `using Sirenix.OdinInspector;` and Odin attributes.
2. Add fields previously on base: fieldBounds, scaleRange, seed, slopeRange, splatLayerIndex, splatThreshold, rotationOffsetEuler, randomPitchRange, randomRollRange, alignToNormal. ALL `[SerializeField] private` with FormerlySerializedAs shims pointing at the same field names ScatterLayer used.
3. Provide accessors (override base virtuals where applicable; new public properties otherwise). Keep accessor names byte-identical to the base ones removed so external code (engines) compiles unchanged. `FieldBounds`, `ScaleRange`, `Seed`, `SlopeRange`, `SplatLayerIndex`, `SplatThreshold`, `RotationOffsetEuler`, `RandomPitchRange`, `RandomRollRange`, `AlignToNormal`, `IsOriented`.
4. Make these accessors VIRTUAL on the base so `MeshScatterEngine` (which takes a `ScatterLayer`) can still call `layer.FieldBounds` etc. Implementation strategy: base class declares these as `public virtual` with sane defaults (FieldBounds = (100,100), ScaleRange = (1,1), Seed = 0, etc.); DensityScatterLayer overrides them; InstanceScatterLayer inherits the defaults.
5. Validate(): keep densityMap-readable + non-compressed checks (existing).

### A.3 -- InstanceScatterLayer.cs gains per-record defaults + default-collider fields

1. Remove `using Sirenix.OdinInspector;` and Odin attributes.
2. KEEP: authoredInstances (sub-asset ref), placeSpacing.
3. ADD: layer-default collider fields used when a record's `generateCollider` is true but no override is set:
   - `[SerializeField] private Mesh? defaultColliderMesh;` (fallback when record's colliderOverride is null).
   - `[SerializeField] private bool defaultColliderConvex = false;`.
   - Accessors: `Mesh? DefaultColliderMesh`, `bool DefaultColliderConvex`.
4. Validate: forward to base; add no new checks (record-level checks live in E's UI).

### A.4 -- AuthoredInstancesData.cs strict V2 schema (D1 final)

1. **One-shot rewrite to V2.** Bump blob FORMAT_VERSION to 2; write a 1-byte version header at offset 0 on Pack. V1 blobs (no header) are detected on UnpackBlob and migrated per-record on read; after the first SaveAssets the asset is byte-identical to a fresh V2 blob (V1 layout is gone forever for that asset).
2. **InstanceRecord struct (V2 final shape, strict brainstorm §2):** `Vector3 position`, `Quaternion rotation`, **`float scale` (uniform)**, `bool generateCollider`, `Mesh? colliderOverride` (via objectRef index; null = use layer default), `float colliderScale = 1f`, `bool colliderConvex`. Plus a `uint overrideMask` retained ONLY to gate per-record collider serialization (single bit: ColliderConfigured). RendererOverride is GONE.
3. **DELETE entirely:**
   - `RendererOverrideData` struct (entire type).
   - `InstanceOverrideMask.RendererOverride` enum value.
   - `RENDERER_BYTES` constant.
   - `SetRendererOverride` method.
   - Every read/write of `rec.rendererOverride.materialRefIndex` / `shadowMode` in Pack/Unpack.
   - `ColliderOverrideData` struct -- its fields move inline onto `InstanceRecord` per step 2 (strict brainstorm §2 has no nested struct; fields are first-class).
   - `Vector3 scale` is replaced by `float scale` -- if any record needs non-uniform scale post-migration, the user can re-author it via a runtime extension (not in v1 of this rewrite).
4. **V2 record byte layout (strict §2):** position(12) + rotation(16) + scale(4) + overrideMask(4) = 36 B fixed header. Optional collider block (12 B: generateCollider(1) + colliderConvex(1) + padding(2) + colliderScale(4) + colliderMeshRefIndex(4)) appended when overrideMask has the ColliderConfigured bit. Total: 36 B (no collider) or 48 B (with collider). Update `FIXED_BYTES` to 36 and `COLLIDER_BYTES` to 12.
5. **V1->V2 readback migration** (single private helper called from UnpackBlob when offset==0 byte is NOT the V2 marker):
   - For each V1 record, read the 44-byte fixed header. The V1 layout had Vector3 scale at offset 28..40 -- collapse to uniform via `record.scale = (v1.scale.x + v1.scale.y + v1.scale.z) / 3f` (average). Log one notice per layer mentioning the collapse if any V1 record had non-uniform scale (|max-min| > 0.001f).
   - If V1 ColliderOverride bit set: read the 12-byte V1 collider block -> map `enabled`->`generateCollider`, `convex`->`colliderConvex`, `meshRefIndex` preserved; synthesize `colliderScale=1f`. Set new ColliderConfigured bit.
   - If V1 RendererOverride bit set: SKIP the 12-byte renderer block (advance offset, do not populate); accumulate the record index for the warning log.
   - After the loop: if any RendererOverride records were skipped, emit ONE `Debug.LogWarning("[AuthoredInstancesData] Migrated <N> V1 records on layer '<name>'; RendererOverride data dropped on indices [<i, j, k>] per D1 strict-V2.")`.
   - If any non-uniform-scale records were collapsed, emit a second one-shot `Debug.LogWarning("[AuthoredInstancesData] Migrated <N> V1 records on layer '<name>'; non-uniform scale collapsed to uniform (average XYZ) on indices [<i, j>] per D1 strict-V2 §2.")`.
6. **Migration is implicit on first read** -- no explicit menu action. After UnpackBlob runs, the working list is V2; the next SaveAssets writes a V2 blob.
7. **Tests (A.0 covers):** fresh V2 round-trip; V1->V2 with mixed override + non-uniform scale paths; assert both warnings fire at most once each.

### A.5 -- TerrainScatterConfig.cs cleanup

1. Remove Odin attributes (`[TitleGroup]`, `[TabGroup]`, etc.).
2. Keep `CreateLayer(string)` method but RENAME and DUPLICATE into two:
   - `CreateDensityLayer(string layerName) -> DensityScatterLayer` -- creates the SO, auto-creates white-filled R8 512x512 density texture (the existing logic already does black-filled; change to white-filled per brainstorm §4), assigns Default_Material from Defaults/ (path injected by C's editor; here just a public setter), prefills lods[0] with default grass mesh.
   - `CreateInstanceLayer(string layerName) -> InstanceScatterLayer` -- creates the SO, auto-creates empty AuthoredInstancesData sub-asset, assigns Default_Material, prefills lods[0] with default prop mesh.
3. NOTE: actual default asset PATHS are resolved by the editor in C (the editor is the one that calls AssetDatabase.LoadAssetAtPath). The runtime methods take Material? and Mesh? parameters and never reach Resources.
4. Existing `CreateLayer` (single) is kept as a private helper that both new methods delegate to for the sub-asset wiring.

### A.6 -- MeshScatterEngine.cs routing rule changes + RendererOverride removal

1. Engine is invoked from ScatterField for layers with `InteractsWithDeform == false` (per brainstorm §1 engine route). ScatterField (A.7) makes the route decision; MeshScatterEngine itself does NOT need to read layer.Kind.
2. Remove every reference to `layer.MeshMaterial` -- replace with `layer.Material`.
3. **REMOVE `BuildMaterialGroups` slow-path entirely** (strict D1). Delete the `materialGroups` field, the `MaterialGroup` private class, every call to `BuildMaterialGroups` and `RecordFrameCommandsForGroup`, the per-group `Shader.SetGlobalBuffer` calls in Submit, and the fast-path-restore block at the end of Submit. The renderer now has ONE material per layer (`layer.Material`).
4. Remove every reference to `InstanceOverrideMask.RendererOverride` and `RendererOverrideData` in this file.
5. BuildColliders: ADD a guard at top: `if (layer is not InstanceScatterLayer instLayer) return;`. Density layers don't collide.
6. BuildColliders: REPLACE `layer.ColliderMesh` -> `instLayer.DefaultColliderMesh`. REPLACE `layer.ColliderConvex` -> `instLayer.DefaultColliderConvex`. REPLACE `layer.GenerateColliders` -> per-record check (iterate `instLayer.AuthoredInstances.GetRuntimeRecords()` and check each record's `generateCollider` bit).
7. BuildColliders: per-record collider scale = `record.scale.x * record.colliderScale` (uniform stand-in). Non-uniform `record.scale` is preserved as the rendered scale; collider scale is uniform per brainstorm §2.
8. NOTE: the runtime collider spawn for Instance layers is REPLACED by InstanceColliderPool in Phase H. Mark with `// TODO[Phase-H] -- replace with InstanceColliderPool` comment.
### A.7 -- ScatterField.cs (D4) routing rule change

1. Find the place where ScatterField decides which engine to instantiate per layer (currently switches on layer.Kind).
2. Replace with: `if (layer.InteractsWithDeform) -> grass engine` else `MeshScatterEngine`.
3. Every read of `layer.GrassMaterial` or `layer.MeshMaterial` -> `layer.Material`.
4. Engine constructor calls pass `layer.Material!` (with null-check + Validate warning when missing).

### A.8 -- ScatterAssetPostprocessor.cs extend type detection (C will append naming convention later)

1. Existing class watches TerrainScatterConfig imports. Keep that.
2. ADD detection for sub-asset rename events: in OnPostprocessAllAssets, walk `imported` list, for each TerrainScatterConfig parent path, enumerate its sub-assets via AssetDatabase.LoadAllAssetsAtPath, and if any sub-asset is a ScatterLayer whose name changed since the last cached name, log a notice (no rename action yet -- C adds the cascading rename in its naming convention method).
3. Leave a marked extension point: `private static void ApplyNamingConvention(TerrainScatterConfig cfg) { /* filled in Phase C */ }` -- C will populate without touching A's code.

### A.9 -- Delete legacy migration scripts + demo assets

1. `git rm Assets/GrassInteract/Editor/ScatterAssetMigrator.cs ScatterAssetMigrator.cs.meta`
2. `git rm Assets/GrassInteract/Editor/MigrateScatterLayerTypes.cs MigrateScatterLayerTypes.cs.meta`
3. `git rm Assets/GrassInteract/Editor/MigrateDeformModeToWindInteract.cs MigrateDeformModeToWindInteract.cs.meta`
4. `git rm Assets/GrassInteract/Editor/ScatterFieldRebuildLayerHarness.cs ScatterFieldRebuildLayerHarness.cs.meta`
5. `git rm Assets/GrassInteract/Demo/TerrainScatterConfig.asset TerrainScatterConfig.asset.meta`
6. `git rm Assets/GrassInteract/Demo/Grass.asset Grass.asset.meta Rock.asset Rock.asset.meta`
7. `git rm Assets/GrassInteract/Demo/Grass_data.asset Grass_data.asset.meta Rock_data.asset Rock_data.asset.meta`
8. Leave `GrassInteractDemo.unity` in place -- it will lose its config reference. Document in CHANGELOG.md: "Demo scene must be re-authored against a fresh TerrainScatterConfig (use + Density / + Instance buttons)."

## Validation criteria

1. **Compile clean (mandatory exit)**: `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` returns `compile_succeeded: true`. Subsequent `read_console` returns ZERO errors. If errors exist, do not declare A complete.
2. **No Odin in runtime**: `grep -rln "Sirenix" Assets/GrassInteract/Runtime/` returns empty.
3. **No ScatterKind references**: `grep -rln "ScatterKind\|\.Kind\b" Assets/GrassInteract/` returns ZERO hits in non-deleted files.
4. **No grass/mesh material split**: `grep -rln "grassMaterial\|meshMaterial\|GrassMaterial\|MeshMaterial" Assets/GrassInteract/Runtime/` returns empty.
5. **Blob roundtrip test GREEN** (A.0 + A.4 re-run).
6. **Open demo scene**: should load with config-reference warning + zero crashes (clean break).
7. Commit before summary (150K rule).

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| ScatterField has more layer.Kind reads than expected | 3 | 5 | 15 | Run `grep -n "layer\.Kind\|\.GrassMaterial\|\.MeshMaterial" Assets/GrassInteract/Runtime/` FIRST and fix every hit in one sweep. |
| V1->V2 migration silently drops RendererOverride data (per D1 strict-V2; intentional but user-visible) | 4 | 3 | 12 | A.4 step 6 enumerates dropped record indices in a single per-layer Debug.LogWarning; A.0 V1->V2 test asserts the warning fires + no exception. CHANGELOG entry (Phase I) flags as BREAKING. |
| V1->V2 migration collapses non-uniform scale to uniform average (per D1 strict-V2 §2 -- `float scale` is uniform) | 3 | 3 | 9 | A.4 step 5 emits a one-shot per-layer warning enumerating affected record indices. A.0 test includes a non-uniform V1 case. CHANGELOG entry (Phase I) flags as BREAKING for any user who relied on non-uniform record scale. |
| GrassCpuEngine/GrassGpuEngine secretly read MeshMaterial | 2 | 4 | 8 | A.6 read-only verify: grep both engines for `.MeshMaterial` -- if hits exist, expand A.6 scope to fix. |
| Sub-asset deletions break unrelated tests | 1 | 3 | 3 | Run any Tests/Editor/ assembly before A.9; if a test references demo assets, mark as ignored with `[Ignore("Demo re-author pending")]`. |

## Effort: M

Estimate 2-4 hours wall time for a focused single dev. Touches 9 files + 8 deletes + 1 EditMode test. Most risk is in the schema migration (A.0 gate).
