---
name: t1k:unity:base:addressables
description: Addressable asset system — async loading, memory management, groups/labels, remote content delivery, migration from Resources.Load. Use when implementing asset management.
triggers:
  - Addressables
  - AssetReference
  - LoadAssetAsync
  - asset bundle
  - remote content
  - content update
  - asset group
  - Resources.Load migration
  - asset label
  - InstantiateAsync
effort: high
context: fork
keywords: [addressables, asset management, loading, unity]
version: 2.5.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity Addressables — Asset Management

Addressables replaces `Resources.Load()` with address-based async loading, reference-counted memory, and remote content delivery.

**Key rule**: Every Load must pair with Release to avoid memory leaks.

## Load Patterns

```csharp
// Single asset:
var handle = Addressables.LoadAssetAsync<Texture2D>("bg_texture");
yield return handle;
Texture2D bg = handle.Result;
Addressables.Release(handle);  // CRITICAL: always release

// Instantiate GameObject:
var handle = Addressables.InstantiateAsync("enemy_prefab", pos, rot, parent);
yield return handle;
Addressables.ReleaseInstance(handle.Result);  // Release when done

// Batch by label:
var handle = Addressables.LoadAssetsAsync<Sprite>("ui", null);
yield return handle;
Addressables.Release(handle);

// Inspector reference:
[SerializeField] AssetReference characterModel;
var handle = characterModel.LoadAssetAsync<GameObject>();
yield return handle;
Instantiate(handle.Result);
characterModel.ReleaseAsset();

// Scene loading:
var handle = Addressables.LoadSceneAsync("level_01", LoadSceneMode.Single);
yield return handle;
Addressables.UnloadSceneAsync(handle);
```

## Loading Methods

| Method | Use Case | Return |
|--------|----------|--------|
| `LoadAssetAsync<T>(key)` | Single asset | `AsyncOperationHandle<T>` |
| `InstantiateAsync(key)` | GameObject | `AsyncOperationHandle<GameObject>` |
| `LoadAssetsAsync<T>(labels, cb, merge)` | Batch by label | `AsyncOperationHandle<IList<T>>` |
| `LoadSceneAsync(address)` | Scene | `AsyncOperationHandle<SceneInstance>` |

## Setup & Groups

1. Inspector: Check "Addressable" on asset, set address + group + labels
2. **Window > Asset Management > Addressables > Groups** — organize assets
3. Packing: Pack Together (one bundle) / Pack Separately (per-asset)
4. Profiles: Local (shipped) vs Remote (CDN with `[BuildTarget]` variable)

## Labels & Batch Loading

```csharp
// Union — assets with ANY label:
Addressables.LoadAssetsAsync<GameObject>(new[] { "ui", "fx" }, null, MergeMode.Union);

// Intersection — assets with ALL labels:
Addressables.LoadAssetsAsync<GameObject>(new[] { "ui", "common" }, null, MergeMode.Intersection);
```

## Memory Management

- Bundle unloads only when ref count = 0
- Multiple assets from same bundle: all must release before memory freed
- Avoid load/release churn in hot paths — load once, reuse
- `Resources.UnloadUnusedAssets()` — slow (50-100ms), loading screens only

## Content Updates

```csharp
var check = Addressables.CheckForCatalogUpdates(autoRelease: false);
yield return check;
if (check.Result.Count > 0) {
    yield return Addressables.UpdateCatalogs(check.Result);
}
Addressables.Release(check);
```

Editor: **Build > Update a Previous Build** — only changed bundles regenerated.

## Migration from Resources.Load

```csharp
// OLD: var tex = Resources.Load<Texture2D>("textures/bg");
// NEW:
var handle = Addressables.LoadAssetAsync<Texture2D>("bg_texture");
yield return handle;
Texture2D tex = handle.Result;
Addressables.Release(handle);
```

## Common Gotchas

1. **Unmatched Load/Release**: Lost handle = permanent memory leak
2. **Partial bundle unload**: One asset released doesn't free bundle if others loaded
3. **Asset churn**: Repeated load/release in loops — load once, reuse handle
4. **Label explosion**: Too many label combos = too many bundles. Plan early (2-3 per asset)
5. **Diagnostics**: Enable in Settings > Diagnostics to debug load failures
6. **Runtime core + addressable UI double init**: If an addressable UI depends on a separate runtime core prefab, initialize shared pools/controllers once in the core and make the UI `Init()` skip them when the core is already initialized. Re-running pool init can duplicate generated item instances/children and make inventory icons or equipped slots render incorrectly.
7. **Scene UI used as data catalog**: Before lazy-loading/removing a scene UI prefab, grep for static resolvers and serialized references to its child components. If other systems use UI cells as sprite/data sources, warm the Addressable after the permitted lifecycle event or route those flows through async load before dereferencing.
8. **Editor automation exit behavior**: Batch extraction helpers may call `EditorApplication.Exit(1)` only when `Application.isBatchMode`; menu-driven Editor workflows should log errors and keep Unity open so users do not lose unsaved work.
9. **Batchmode project lock**: Unity batchmode cannot open a project already open in another Unity instance. Detect and ask before killing editor processes; never remove lock files as a shortcut.
10. **Bootstrap chicken-and-egg — settings must exist before group-creation tools run**: Any project-side migration menu item (e.g., `Tools/Addressables/Create All Demo Groups`) typically calls `AddressableAssetSettingsDefaultObject.GetSettings(create: false)` and aborts with a "Addressables not initialized" modal if the asset is missing. Via MCP this looks like an `execute_menu_item` *timeout* (the modal blocks the bridge), not a clear error. **Always bootstrap the settings asset FIRST** before invoking any group-creation tool:
    ```csharp
    // via mcp__UnityMCP__execute_code
    var t = System.AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .First(x => x.FullName == "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
    var settings = t.GetMethod("GetSettings", new[] { typeof(bool) }).Invoke(null, new object[] { true });
    UnityEditor.AssetDatabase.SaveAssets();
    ```
    Then re-run the migration menu item (or invoke it directly via reflection — see gotcha 11). The MCP `manage_addressables` tool exposes `list_groups`/`get_group`/`build`/`analyze` but has **no `init`/`create_settings` action** — file an MCP-gap issue if the team wants one.
11. **Modal-blocked menu items**: `mcp__UnityMCP__execute_menu_item` blocks on any modal the menu raises (confirmation dialogs, "missing settings" warnings, file pickers). The MCP request will *time out*, not return an error. When you've ruled out long-running work as the cause, suspect a modal — fall back to `mcp__UnityMCP__execute_code` and invoke the menu's underlying static method via reflection (most editor tools delegate to a `private static` worker that bypasses any UI gate).
12. **`AssetReferenceBaker.ResolveBakedEntityRequired` needs BOTH valid GUID AND group registration** (2026-05-23, BattleDemo2D no-troops regression). The bake-time helper in `com.the1studio.dots-core/Runtime/Baking/AssetReferenceBakerExtensions.cs` performs **two** checks: (a) `prefab.RuntimeKeyIsValid()` — non-empty GUID; (b) `settings.FindAssetEntry(prefab.AssetGUID) != null` — actually registered in an Addressables group. Editor-side helpers that only set the GUID (e.g., a project's `AddressablesAdapter.Wrap(go)`) do **not** add the group entry. Skipping the registration step in the regen pipeline produces an `InvalidOperationException` at bake time and the SubScene artifact is never written — Play mode shows zero entities and (because the exception is logged from a worker process) often nothing visible in the main Editor console. **Canonical regen order**: ① Create Unit Prefabs → ② **`Tools/Addressables/Register All Demo Prefabs`** (project-side menu) → ③ Build Behavior Trees → ④ Setup Scene → SubScene auto-bakes cleanly. Registration is idempotent; re-running is free.

## Library Convention — Addressables-First (unity-dots-library v2.0.0+)

Since unity-dots-library v2.0.0 (2026-05-23) the library is **Addressables-only** for all prefab/asset wiring. Consumer projects that depend on the library MUST follow this contract:

- **Authoring fields:** use `AssetReferenceGameObject` (or other `AssetReference*`) — never `[SerializeField] GameObject` for runtime-instantiated content.
- **Bake helper (SSOT):** `AssetReferenceBakerExtensions.BakeAssetReference(ref Baker, AssetReferenceGameObject)` lives in `com.the1studio.dots-core`. Bakers MUST call it instead of hand-rolling weak-reference baking.
- **Enforcement gate:** `DOTSCore.Tests.Addressables.AddressablesEnforcementTests` fails the build on any `Resources.Load(`, `AssetDatabase.LoadAssetAtPath`, or non-ECS `Object.Instantiate(GameObject)` in Runtime asmdefs outside the per-project allowlist.
- **Allowlist:** legitimate Mono-UI `Instantiate` carve-outs go in the consumer project's `Assets/Editor/AddressablesEnforcement.Allowlist.cs` with `File`+`Line` entries citing the audit finding.
- **Build hook required:** consumer projects MUST ship an `AddressablesBuildPlayerProcessor` or the Android APK ships an empty catalog → runtime `InvalidKeyException`.
- See the consumer project's `CLAUDE.md` § "Addressables Mandate" and `Packages/unity-dots-library/MIGRATION.md` for the full consumer upgrade procedure.

## Related Skills & Agents
- `unity-scene-management` — Addressable scene loading
- `unity-mobile` — Remote content delivery optimization
- `unity-save-system` — Saving asset references
- `dots-graphics` — ECS mesh/material loading via Addressables
