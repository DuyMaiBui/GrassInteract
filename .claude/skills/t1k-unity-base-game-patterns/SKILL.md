---
name: t1k:unity:base:game-patterns
description: Unity MonoBehaviour game patterns — object pooling, state machines, command pattern, ScriptableObjects, save systems, scene management, input handling.
effort: medium
keywords: [game patterns, design patterns, architecture, unity, wheel-friction, sidewaysFriction, friction cache, vcontainer, registration override, circular dependency]
version: 2.5.1
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity Game Patterns

## When This Skill Triggers

- Implementing game mechanics or systems (MonoBehaviour context)
- Setting up object pooling, state machines
- Designing save/load systems
- Managing scenes (additive loading, transitions)
- Handling input (New Input System, touch)
- Creating data-driven systems with ScriptableObjects

## Quick Reference

| Task | Reference |
|------|-----------|
| Object pooling, state machines, command, procedural dynamic mesh | [Core Patterns](references/core-patterns.md) |
| ScriptableObjects, config, save systems | [Data & Persistence](references/data-persistence.md) |
| Scene management, input, coroutine alternatives | [Systems](references/game-systems.md) |
| Project hierarchy, scene architecture, canvas config | [Mobile Setup](references/mobile-setup.md) |
| Player settings, quality/physics/audio, perf budgets | [Mobile Optimization](references/mobile-optimization.md) |
| VContainer registration override, circular dependency | [VContainer Registration Override](references/vcontainer-registration-override.md) |

> Note: `System.Linq` is forbidden in runtime code (GC alloc). Use `foreach` in runtime; `System.Linq` only in editor/tests.

## Critical Rules

1. **Pool everything that spawns/despawns frequently** — Use `UnityEngine.Pool.ObjectPool<T>`
2. **ScriptableObjects for data** — Config, items, levels, balancing — NOT MonoBehaviours
3. **State machines for complex state** — Game states, AI, UI flow
4. **New Input System** — Always use for cross-platform (touch + gamepad + keyboard)
5. **UniTask over coroutines** — Async/await with cancellation support
6. **VContainer for DI** — All services via constructor injection (see `theone-studio-patterns`)
7. **No System.Linq at runtime** — GC alloc; use `foreach` or `ZLinq` (see `zlinq` skill)

## Key Patterns

### Object Pool (VContainer-friendly)
```csharp
public sealed class BulletPool : IInitializable, IDisposable
{
    readonly ObjectPool<Bullet> _pool;

    public BulletPool(Bullet prefab)
    {
        _pool = new ObjectPool<Bullet>(
            createFunc: () => Object.Instantiate(prefab),
            actionOnGet: b => b.gameObject.SetActive(true),
            actionOnRelease: b => b.gameObject.SetActive(false),
            defaultCapacity: 20, maxSize: 100);
    }

    public Bullet Get() => _pool.Get();
    public void Release(Bullet b) => _pool.Release(b);
    public void Dispose() => _pool.Dispose();
}
```

### Simple State Machine
```csharp
public abstract class GameState { public abstract UniTask Enter(); public abstract void Exit(); }
// States: MenuState, PlayState, PauseState, GameOverState
// Managed via GameStateService with VContainer
```

## Gotchas
- **Unity fake null**: Never use `??` or `is null` with `UnityEngine.Object` — Unity overrides `==` to detect destroyed objects, but `??`/`is null` bypasses this and treats destroyed objects as non-null
- **Coroutine on disabled GO**: `StartCoroutine()` throws if the MonoBehaviour or its GameObject is inactive. Check `gameObject.activeInHierarchy` before starting
- **ScriptableObject shared in builds**: SO assets are shared instances — mutating fields at runtime affects all references. Clone with `Instantiate()` if per-instance data is needed
- **Per-frame polyline geometry (rope/tube/trail)**: build ONE dynamic `Mesh` (`MarkDynamic()` + bounded `SetVertices`/`SetIndices(array, start, length)` overloads, mitered rings with a parallel-transport frame to avoid twist) — do NOT `Graphics.RenderMeshInstanced` N built-in cylinders (~3000 vs ~210 verts, and primitives leave gaps at corners). Per-instance texture → `MaterialPropertyBlock` `_BaseMap`, not a material instance. See [Core Patterns → Procedural Dynamic Mesh](references/core-patterns.md)
- **Wheel friction cache poisoned by `Awake` grip multiply**: Multiplying `WheelCollider.sidewaysFriction.stiffness` in code at `Awake` (a grip multiplier) BEFORE a later lazily-populated friction-default cache (e.g. `CarVehicle.CacheDefaultFriction`) runs poisons that cache — it captures the already-inflated value as the "default" baseline, so runtime zone/upgrade friction multipliers then scale off the wrong baseline (e.g. 1.5x too high). **Fix (SSOT):** author the baseline grip on the `WheelCollider` **prefab** (single source of truth) and remove the `Awake` code-side multiply. If a runtime multiplier knob is genuinely required, capture the defaults (call the cache) BEFORE any code-side multiply. Prefab-SSOT is preferred.
- **VContainer duplicate registration ≠ last-wins**: two `RegisterInstance`/`Register<T>()` calls for the same concrete type do NOT override — VContainer throws `VContainerException: Conflict implementation type` at container build (boot crash). Library-default + consumer-override needs `IContainerBuilder.Exists` guard on the library side AND the consumer registering BEFORE calling the library's `Register*` extension. See [VContainer Registration Override](references/vcontainer-registration-override.md) for the ordering pattern and the circular-dependency (`VContainerException: Circular dependency detected!`) fix via SignalBus.

## Related Skills

- `theone-studio-patterns` — VContainer DI, SignalBus events, service patterns
- `theone-unity-standards` — Code quality, naming conventions
- `unity-mobile-ui` — UI state management, input handling
- `zlinq` — Zero-alloc LINQ alternative for runtime code
- `dots-ecs` — ECS patterns (DOTS context, not MonoBehaviour)
