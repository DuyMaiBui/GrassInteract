---
name: t1k:unity:base:monobehaviour
description: MonoBehaviour lifecycle, coroutines, ScriptableObjects, singleton patterns, events, and DI patterns for Unity 6. Use when writing non-DOTS gameplay scripts.
effort: high
context: fork
triggers:
  - MonoBehaviour
  - coroutine
  - StartCoroutine
  - ScriptableObject
  - CreateAssetMenu
  - singleton
  - DontDestroyOnLoad
  - UnityEvent
  - OnEnable
  - OnDisable
  - Awake
  - Start
  - Update
  - FixedUpdate
  - LateUpdate
  - OnDestroy
  - OnCollisionEnter
  - OnTriggerEnter
  - InvokeRepeating
  - WaitForSeconds
keywords: [MonoBehaviour, component, unity, lifecycle]
version: 2.2.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity MonoBehaviour — Lifecycle, Patterns & Best Practices

Core reference for MonoBehaviour-based development in Unity 6 (non-DOTS). For ECS, see `dots-ecs`.

## Lifecycle Order (Single Frame Init)

```
Awake()           → Called once, even if disabled. Use for self-references
OnEnable()        → Called when enabled. Use for event subscriptions
Start()           → Called once, first frame active. Use for cross-references
```

## Lifecycle Order (Per Frame)

```
FixedUpdate()     → Physics tick (default 50Hz). Rigidbody forces here
Update()          → Once per frame. Input, game logic
LateUpdate()      → After all Update(). Camera follow, post-processing
```

## Lifecycle Order (Teardown)

```
OnDisable()       → When disabled. Unsubscribe events here
OnDestroy()       → When destroyed. Cleanup native resources
OnApplicationQuit() → App closing
```

## Coroutine Patterns

→ See [references/coroutine-patterns.md](references/coroutine-patterns.md) for all yield options, start/stop patterns, and the UniTask gotcha.

## ScriptableObject Patterns

```csharp
[CreateAssetMenu(fileName = "NewWeapon", menuName = "Game/Weapon Data")]
public class WeaponData : ScriptableObject {
    public string weaponName;
    public int damage;
    public float cooldown;
    public AnimationClip attackAnim;
}

// Runtime event channel (observer pattern):
[CreateAssetMenu(menuName = "Events/Void Event")]
public class VoidEventChannel : ScriptableObject {
    public event System.Action OnEventRaised;
    public void RaiseEvent() => OnEventRaised?.Invoke();
}
```

**Best practices**: Use SO for shared config data, event channels, enum-like sets. Never store runtime state in SO (it persists in Editor between plays).

## Singleton Patterns

```csharp
// Lazy persistent singleton
public class GameManager : MonoBehaviour {
    public static GameManager Instance { get; private set; }

    void Awake() {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy() {
        if (Instance == this) Instance = null;
    }
}
```

**Prefer**: ScriptableObject event channels or dependency injection over singletons for testability.

## Event Patterns

```csharp
// C# events (preferred for code-to-code):
public event System.Action<int> OnHealthChanged;
OnHealthChanged?.Invoke(currentHp);

// UnityEvent (preferred for Inspector wiring):
[SerializeField] UnityEvent<int> onHealthChanged;
onHealthChanged.Invoke(currentHp);

// Subscribe/unsubscribe in OnEnable/OnDisable:
void OnEnable() => player.OnHealthChanged += HandleHealthChanged;
void OnDisable() => player.OnHealthChanged -= HandleHealthChanged;
```

## Common Gotchas

1. **Awake vs Start order**: Awake order between objects is undefined. Use Awake for self-init, Start for cross-references
2. **Null after Destroy**: Unity overrides `==` operator. Use `if (obj)` not `if (obj != null)` for destroyed check. **NEVER use `??` or `?.`** with UnityEngine.Object — they bypass Unity's null override (C# sees destroyed objects as non-null). Use explicit `== null` checks instead: `var c = GetComponent<T>(); if (c == null) c = AddComponent<T>();`
3. **Null check performance**: `== null` and `!obj` both cross managed→native boundary (~5x slower than pure C# check). In hot paths (Update, FixedUpdate, per-frame loops) where destroyed-detection is not needed, use `ReferenceEquals(obj, null)` (explicit intent, hard to misuse) or `obj is null` (shorter but looks deceptively normal in Unity context). Only use `== null`/`!obj` when you genuinely need destroyed-object detection.
4. **Camera.main is slow**: Calls `FindGameObjectWithTag("MainCamera")` every invocation. Cache in `Awake()`/`Start()`: `private Camera _mainCam; void Awake() => _mainCam = Camera.main;`
5. **GetComponent is slow**: Cache results in Awake/Start. Never call in Update
6. **SendMessage is slow**: Use events or direct references instead
7. **DontDestroyOnLoad duplicates**: Always check `if (Instance != null)` in Awake
8. **Execution order**: Set via Script Execution Order settings or `[DefaultExecutionOrder(N)]` attribute
9. **Disabled components**: `Awake()` still runs. `Start()`, `Update()`, etc. do NOT run when disabled
10. **Time.deltaTime in FixedUpdate**: Use `Time.fixedDeltaTime` or just `Time.deltaTime` (auto-adjusts in FixedUpdate)
11. **Static events retain destroyed objects**: Always unsubscribe instance methods in `OnDestroy()` when subscribing to static events. A stale delegate can call into a destroyed `MonoBehaviour`; guard before `StartCoroutine()` if the event can fire during teardown or across no-domain-reload play sessions.
12. **Per-frame methods can beat lazy init**: If `Update()` reads fields initialized only by a lazy singleton getter or external `Init()`, initialize safe state inline/Awake too. Scene objects can receive `Update()` before any other script has touched `Class.inst`.
13. **Byte-backed enum unboxing — use `Convert.ToInt32`, NOT `(int)`**: When reading an enum value out of `object`/`SerializedProperty.boxedValue`/`FieldInfo.GetValue` and the enum's underlying type is `byte` / `sbyte` / `short` / `ushort` / `long` / `ulong` (any non-`int` integral backing), the direct cast `(int)boxedValue` throws `InvalidCastException: Specified cast is not valid` because C# unbox + numeric cast require the boxed type to match exactly. **Use `System.Convert.ToInt32(boxedValue)` instead** — handles all integral backings uniformly via `IConvertible`. Concrete example: an `EquipSlotType : byte` (used in DOTS inventory) round-trips fine through reflection write but fails on read with `(int)fv`. Pattern: `case SerializedPropertyType.Enum: prop.enumValueIndex = System.Convert.ToInt32(fv);` — never `(int)fv`. Same rule for any `[Serializable]` reflective populator (`ApplyStructFields`, `CopySerializedValues`, ScriptableObject editor inspectors) that walks enum fields generically.

## Related Skills & Agents
- `dots-ecs` — DOTS/ECS alternative (use `dots-implementer` agent)
- `unity-input-system` — Player input handling
- `unity-scene-management` — Scene lifecycle
- `unity-animation` — Animator integration
- `unity-code-conventions` — C# naming rules
