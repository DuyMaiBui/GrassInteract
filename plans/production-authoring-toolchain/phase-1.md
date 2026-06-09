# Phase 1 (A) — Data Model: PhysicMaterial per instance + layer default

Effort: **M** · Blocked by: nothing · Blocks: Phase 4 (PhysicMaterial payload)

## Goal

Each `InstanceRecord` carries a per-instance collider **PhysicMaterial** (physics friction/bounce — NOT a
render material), object-ref-indexed exactly like the override collider mesh. Add a layer-default
PhysicMaterial and wire the runtime pool to apply it. Bump the blob to V3 with a clean V2→V3 migration
that preserves the existing V1→V2 chain.

## Reuse check

EXTENDS existing files only — no new runtime file. The blob codec, `objectRefs` indexing, working-list
edit API, and V1→V2 migration already exist and are followed verbatim for V3.

## File ownership

### Modified
- `Assets/GrassInteract/Runtime/AuthoredInstancesData.cs`
  - `InstanceRecord`: add `[System.NonSerialized] public int colliderMaterialRefIndex;` (-1 = layer default), documented like `colliderMeshRefIndex`.
  - `COLLIDER_BYTES`: `16 → 20` (add 4 bytes for `colliderMaterialRefIndex`). Update the doc comment block (header says 12 B optional block in prose but constant is 16 — fix prose to 20 and enumerate the 5 ints: generateCollider(4)+colliderConvex(4)+colliderScale(4)+meshRefIdx(4)+matRefIdx(4)).
  - `VERSION_BYTE`: `2 → 3`.
  - `PackBlob`: write `rec.colliderMaterialRefIndex` after `colliderMeshRefIndex` inside the collider block.
  - Add `UnpackBlobV3()` (V3 collider block reads 5 ints incl. matRefIdx; no-collider records default `colliderMaterialRefIndex = -1`).
  - Add `MigrateV2ToV3()`: re-uses the V2 unpacker shape but for the OLD 16 B block (4 ints), then sets `colliderMaterialRefIndex = -1` on every record. Emit one-shot per-layer `Debug.LogWarning` (mirrors the V1→V2 warning style).
  - `UnpackBlob()` dispatch: `blob[0] == 3 → UnpackBlobV3`; `blob[0] == 2 → MigrateV2ToV3` (the V2 reader logic, then matRefIdx=-1); else → `MigrateV1ToV2` then promote to V3 in-memory (V1→V2→V3 chain). Keep V2 byte-reader logic available for the V2 path even though VERSION_BYTE is now 3.
  - `CountFromBlob`: add `CountFromBlobV3` (collider block = 20 B); keep `CountFromBlobV2` (16 B) + `CountFromBlobV1`. Dispatch on `blob[0]`.
  - `SetColliderConfig`: add a `colliderMaterialRefIndex` parameter (default -1) so Phase 4 can write the per-instance material; `ClearColliderConfig` resets it to -1.
  - `objectRefs` already stores `UnityEngine.Object` — a `PhysicMaterial` is an `Object`, so `EnsureObjectRef`/`GetObjectRef` work unchanged for materials.

- `Assets/GrassInteract/Runtime/InstanceScatterLayer.cs`
  - Add `[SerializeField] private PhysicMaterial? defaultColliderMaterial;` (mirrors `defaultColliderMesh`) + `public PhysicMaterial? DefaultColliderMaterial => this.defaultColliderMaterial;`.
  - Tooltip: "Fallback collider PhysicMaterial when a record's material override is null."

- `Assets/GrassInteract/Runtime/InstanceColliderPool.cs`
  - `Init`: add a `PhysicMaterial? defaultMaterial` parameter; store `this.defaultMaterial`.
  - `Acquire` + `ApplyTransformAndMesh`: add a `PhysicMaterial? materialOverride` parameter; in `ApplyTransformAndMesh` set `mc.sharedMaterial = materialOverride ?? this.defaultMaterial` (only assign when changed, matching the mesh/convex change-guard pattern).
  - Caller in the instanced-prop collider-spawn path must resolve `record.colliderMaterialRefIndex` via `AuthoredInstancesData.GetObjectRef` cast to `PhysicMaterial` and pass it through. (Confirm the caller during cook — the pool is `Acquire`d from the collider-driving system; grep `InstanceColliderPool` callers.)

### Created
- `Assets/GrassInteract/Tests/Editor/GrassInteract.EditorTests.asmdef`
  - References: `GrassInteract` (runtime). `optionalUnityReferences`/`precompiledReferences` for `nunit.framework.dll`; `includePlatforms: ["Editor"]`. Define constraint `UNITY_INCLUDE_TESTS`.
- `Assets/GrassInteract/Tests/Editor/AuthoredInstancesDataBlobTests.cs`

## Unity-stdlib / decoupling

`PhysicMaterial` is `UnityEngine` stdlib. Zero third-party added. Runtime files keep no `UnityEditor`
usage. Passes `library-third-party-decoupling`.

## Tests (MANDATORY — EditMode)

`AuthoredInstancesDataBlobTests.cs`:
1. **V3 round-trip** — build a working list with mixed records (no-collider, collider w/ meshRef, collider w/ meshRef + matRef), `PackBlob`, force re-read via `OnAfterDeserialize`-equivalent, assert all fields incl. `colliderMaterialRefIndex` survive.
2. **V2→V3 migration** — hand-craft a V2 blob (`VERSION_BYTE=2`, 16 B collider block), unpack, assert every collider record has `colliderMaterialRefIndex == -1` and all other fields preserved; assert subsequent `PackBlob` writes `VERSION_BYTE=3` with 20 B block.
3. **V1→V2→V3 chain** — hand-craft a V1 blob (44 B header, 12 B collider, 12 B renderer dropped), assert it migrates through to V3 with matRefIdx=-1 and renderer data dropped (warning emitted).
4. **COLLIDER_BYTES boundary** — assert `InstanceRecord.COLLIDER_BYTES == 20`; assert `ByteSize()` = 36 (no collider) / 56 (with collider).
5. **CountFromBlob parity** — `CountFromBlobV3` matches `WorkingList.Count` for a V3 blob without unpacking the full list.
6. **Pool material assignment** — construct an `InstanceColliderPool`, `Init` with a default `PhysicMaterial`, `Acquire` with `materialOverride=null` → `mc.sharedMaterial == default`; `Acquire` with an override → `mc.sharedMaterial == override`. (PlayMode-free: pool only spawns GameObjects; run as an EditMode test that creates+destroys a temp GO. If GO lifecycle makes this flaky as EditMode, mark it PlayMode and note so.)

Run via Unity Test Runner (EditMode). Zero failures required before phase done.

## Risk table

| Risk | L | I | Score | Mitigation |
|------|:-:|:-:|:-:|------------|
| V3 migration corrupts V1→V2→V3 chain (data loss) | 3 | 5 | 15 | **HIGH** — tests 1-3 cover every chain hop; version-byte dispatch on `blob[0]`; V2 reader preserved verbatim. Do NOT mark done until all blob tests pass. |
| COLLIDER_BYTES off-by-one (16 vs 20) desyncs Count vs Unpack | 3 | 4 | 12 | Single named constant `COLLIDER_BYTES` consumed by pack, unpack, and count; test 4 + 5 pin it. |
| Pool caller path for matOverride missed | 3 | 3 | 9 | Grep `InstanceColliderPool` callers during cook; pass matRef through the same site that resolves meshRef. |

## Success criteria (verifiable)

- `InstanceRecord.COLLIDER_BYTES == 20`, `VERSION_BYTE == 3`.
- All 6 EditMode tests pass (`run_tests` EditMode, zero failures).
- A layer with `defaultColliderMaterial` set, plus a record with a per-instance PhysicMaterial override, produces a runtime `MeshCollider` whose `sharedMaterial` matches the override (default when override null) — verifiable in Play mode by inspecting a spawned `InstanceCollider` GameObject.
- Existing V2 authored assets load without error and re-save as V3 (one migration warning, no data loss).
