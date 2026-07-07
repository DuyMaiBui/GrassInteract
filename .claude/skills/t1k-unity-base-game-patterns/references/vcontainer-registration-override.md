---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# VContainer — Registration Override & Circular Dependency Gotchas

Verified against `jp.hadashikick.vcontainer` (~1.13+) in a real project (Arrow3D, PR #46 + The1Studio/UITemplate PR #958).

## Duplicate same-type registrations do NOT last-win — they crash at build

Registering the same **concrete type** twice (e.g. two `builder.RegisterInstance(new Foo())` calls, or two `Register<Foo>()` calls) does NOT silently overwrite the first with the second. `Registry.Build` folds duplicate same-type registrations into a collection registration (to support `IEnumerable<T>` injection), and `CollectionInstanceProvider.Add` throws:

```
VContainerException: Conflict implementation type
```

This throws at container **build** time — i.e. root `LifetimeScope.Awake()` — a boot crash, not a graceful override.

**Do not trust the "overwritten by the later registration" framing** some VContainer internals comments use (that applies to key-mapping/interface-overwrite paths, not to two registrations of the same concrete type). If you need "later registration wins" semantics for a concrete type, you must guard it explicitly (see below) — VContainer does not do it for you.

## Library-default + consumer-override pattern

When a library needs to register a default instance that a consumer may want to override, guard the registration with `IContainerBuilder.Exists`:

```csharp
// Library-side default (only registers if the consumer hasn't already):
if (!builder.Exists(typeof(MyOptions)))
{
    builder.RegisterInstance(new MyOptions());
}
```

`IContainerBuilder.Exists(Type, bool includeInterfaceTypes = false, bool findParentScopes = false)` has been available since VContainer ~1.13.

**Ordering constraint — the consumer MUST register its override BEFORE calling the library's `Register*` extension.** `Exists` only sees registrations made so far in the builder; if the library's guard runs before the consumer's override, the guard sees "not registered yet" and installs its own default anyway, which then collides with the consumer's later registration (see the crash above). Document this ordering constraint with a code comment at the consumer call site:

```csharp
// MUST run before builder.RegisterMyLibrary() — the library guards its
// default with IContainerBuilder.Exists(typeof(MyOptions)) and only
// skips its own registration if this one already ran.
builder.RegisterInstance(new MyOptions { ... });
builder.RegisterMyLibrary();
```

## Circular dependency at container build

A class implementing interface `I` must never (even transitively) constructor-inject a service that itself injects `I` back. VContainer detects the cycle at build time:

```
VContainerException: Circular dependency detected!
```

Real case: `GameplayService : IGameplayService` injected `MidGameAutoSaveService(IEntityManager, ..., IGameplayService)` — a direct cycle once `MidGameAutoSaveService` was itself a dependency reachable from `GameplayService`'s own registration graph.

**Fix pattern:** break the cycle with event/`SignalBus`-driven wiring instead of a direct interface injection. The dependent service subscribes to a signal instead of injecting the interface back:

```csharp
// Instead of: MidGameAutoSaveService(IGameplayService gameplayService)
// Subscribe to a signal the gameplay service publishes on state change:
public sealed class MidGameAutoSaveService : IInitializable, IDisposable
{
    private readonly SignalBus signalBus;

    public MidGameAutoSaveService(SignalBus signalBus, /* other deps, no IGameplayService */)
    {
        this.signalBus = signalBus;
    }

    public void Initialize() => this.signalBus.Subscribe<GameplayStateChangedSignal>(this.OnGameplayStateChanged);
    public void Dispose() => this.signalBus.Unsubscribe<GameplayStateChangedSignal>(this.OnGameplayStateChanged);

    private void OnGameplayStateChanged(GameplayStateChangedSignal signal) { /* ... */ }
}
```

## Evidence

- The1Studio/Arrow3D PR #46
- The1Studio/UITemplate PR #958
