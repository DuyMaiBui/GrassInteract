---
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Unity Mono Runtime Spawning — Use IObjectPoolManager + Prefabs

> **Scope:** This rule applies to the **Unity Mono (MonoBehaviour / GameObject) path only**.
> It does **NOT** apply to **DOTS / ECS** code — DOTS spawning is governed by `EntityManager` /
> `EntityCommandBuffer` patterns in the `dots-ecs` and `dots-entity-command-buffer` skills.
> If you are writing an `ISystem`, `IComponentData`, or any code under a DOTS module, stop and
> use the DOTS spawning rules instead.

## Rule

In MonoBehaviour / GameObject code, never use `new GameObject(...)` or `Object.Instantiate(...)` to spawn runtime objects. Create a prefab, register it as an Addressable, and spawn/recycle through `TheOne.Pooling.IObjectPoolManager`.

| Want to spawn... | Use |
|---|---|
| A view bound to a `MonoBehaviour` (UI element, world prop, FX overlay) | Prefab + `IObjectPoolManager.Spawn<T>(key, ..., parent, spawnInWorldSpace: false)` |
| A pure entity registered with TheOne's `IEntityManager` (Mono entity layer) | `IEntityManager.Spawn<T>()` |
| A short-lived addressable VFX | `IObjectPoolManager.Spawn(key, position)` |
| A **DOTS entity** | **Out of scope — use `EntityManager.CreateEntity` / ECB. See `dots-ecs`.** |

## Why

- `new GameObject` + `AddComponent` chains hit Unity's managed boundary every spawn and produce GC pressure. Pool spawn reuses a pre-loaded instance.
- `Load(key, count)` once at scene start + `Unload(key)` on teardown gives the addressable system clean lifecycle hooks. Hand-instantiated objects skip that and can leak across scene/level reloads.
- Use `nameof(MyView)` as the pool key so a rename refactor updates the key — magic strings rot.
- Pools are infrastructure the kit already provides on the Mono side. Hand-instantiating bypasses the addressable-group registration that the build pipeline relies on, and breaks the consistent pattern other Mono systems in the codebase already use.

## How — UI prefab via `IObjectPoolManager`

1. Create the `MonoBehaviour` view with serialized fields and a `Configure(...)` method for spawn-time setup:
   ```csharp
   public sealed class MyGhostView : MonoBehaviour
   {
       [SerializeField] private Image image = null!;
       public RectTransform RectTransform => (RectTransform)this.transform;
       public void Configure(Vector2 size, Vector2 anchored, Sprite? sprite) { ... }
   }
   ```

2. Create the prefab and wire serialized fields. Use Unity MCP (`manage_gameobject` → `manage_prefabs.create_from_gameobject`) or the editor.

3. Register the prefab as an Addressable with `m_Address: <ViewName>` in the appropriate group asset under `Assets/AddressableAssetsData/AssetGroups/`. The address MUST equal `nameof(MyView)` so the pool key and the addressable address stay in lockstep.

4. Resolve `IObjectPoolManager` from the DI container: `Container.Resolve<IObjectPoolManager>()`.

5. Pre-load in `OnSpawn` / setup, sized for worst case:
   ```csharp
   this.objectPoolManager.Load(nameof(MyView), count: WORST_CASE_COUNT);
   ```

6. Spawn / recycle at runtime:
   ```csharp
   var instance = this.objectPoolManager.Spawn<MyView>(
       nameof(MyView),
       position: null, rotation: null,
       parent: container, spawnInWorldSpace: false);
   instance.Configure(...);
   // ... later:
   this.objectPoolManager.Recycle(instance.gameObject);
   ```

7. Unload on teardown (`OnRecycle`, scene unload, scope dispose):
   ```csharp
   this.objectPoolManager.Unload(nameof(MyView));
   ```

## When NOT to apply

- **DOTS / ECS code.** Pure entities without a `MonoBehaviour` view use `EntityManager.CreateEntity` / `EntityCommandBuffer` per Unity DOTS norms. `IObjectPoolManager` is a Mono-side construct and has no role in the ECS world.
- **Hybrid baking authoring.** Authoring `MonoBehaviour`s that exist only to be baked into entities are not "runtime spawns" — they live in subscenes and are converted at bake time. No pool involved.
- **Editor-only tooling** (`#if UNITY_EDITOR`). Editor scripts may instantiate freely — there is no pool runtime in editor flows.
- **One-off bootstrap GameObjects** spawned exactly once per scene by the bootstrapper itself (root canvas, DontDestroyOnLoad service host). No recycle phase → pool is overkill. Document the choice in a code comment.

## Test

If `new GameObject(...)` or `Instantiate(...)` in your **Mono runtime path** can be replaced with `pool.Spawn<>(...)` without changing observable behaviour, the original was a violation. If the spawn is genuinely one-off, editor-only, or DOTS-side, the rule does not apply — say so explicitly in a code comment so the next reader doesn't "fix" it.

## Related

- `rules/code-conventions-unity.md` — The1Studio Unity / C# conventions (Mono side).
- `modules/unity-base/skills/unity-game-patterns` — pooling, state machines, service locator (Mono).
- `modules/unity-base/skills/unity-addressables` — Addressable group registration that the pool keys depend on.
- **DOTS counterpart:** `modules/dots-core/skills/dots-ecs` and `modules/dots-core/skills/dots-entity-command-buffer` — entity creation/destruction patterns. Do not cross-apply this Mono rule there.
