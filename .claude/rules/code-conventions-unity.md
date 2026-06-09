---
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Code Conventions — Unity / C#

Extends core `code-conventions.md`. The1Studio-specific patterns.

## Naming (Deviates from Microsoft C# Standard)
- **Private fields:** `camelCase` — NO underscore prefix. Use `this.` for disambiguation
- **DOTS system fields:** `m_` prefix for `ComponentLookup<T>` fields (Unity convention)
- **Public fields / Properties / Methods:** `PascalCase`
- **Constants:** `UPPER_SNAKE_CASE`
- **Enums:** `PascalCase` values
- **Namespaces:** `ProjectName.Feature.Component` — max 3-4 levels
- **`this.` prefix:** ALWAYS use for member access (mandatory, not optional)

## DI & Architecture
- **VContainer ONLY** — do NOT use Zenject (legacy, conditional `#if GDK_ZENJECT` only)
- **Events:** VContainer `SignalBus` — type-safe, decoupled
- **Subscribe/Unsubscribe:** use named methods, NOT lambdas (reference equality)
- **Resolve:** `this.GetCurrentContainer().Resolve<T>()` or constructor injection

## Async & Reactive
- **UniTask** preferred over coroutines for all new async code
- **R3** for reactive data binding and subscriptions
- Dispose subscriptions in `OnDestroy()` — track `IDisposable`

## Serialization
- `[SerializeField] private` — always private, no underscore prefix
- Attribute order: `[Tooltip]` → `[Range]` → `[SerializeField]`
- Default values inline: `[SerializeField] private float speed = 5f;`

## DOTS / ECS
- Attribute order: `[BurstCompile]` → `[RequireMatchingQueriesForUpdate]` → `[UpdateInGroup]`
- Job struct fields: `public` with `[ReadOnly]`/`[WriteOnly]` annotations
- Update lookups in `OnUpdate`: `m_Lookup.Update(ref state);`

## MonoBehaviour
- Lifecycle methods (`Awake`, `Start`, `OnEnable`, `OnDestroy`): `private` visibility
- Null safety: use `#nullable enable` in new files
- Lazy init: `this.field ??= new();`

## File Organization
- UPM packages: `Runtime/`, `Editor/`, `Samples~/`
- One `.asmdef` per module — no global assembly definitions

## Living Document
If unsure about a convention not covered here, ask the user for their preference and update this file with the answer. Conventions grow from real decisions.
