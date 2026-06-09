---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
---

# Anti-Patterns to Avoid

| Anti-Pattern | Correct Pattern |
|-------------|-----------------|
| `GameObject.Find("name")` | Find by component type or cache reference |
| `GetComponent<T>()` in Update | Cache in Start/OnCreate |
| `SystemAPI.HasComponent<T>(entity)` in foreach | Cache `ComponentLookup<T>` |
| Inline `"shader_name"` string | Named constant |
| `if (x == 0.5f)` | `if (x == SomeThreshold)` |
| `new Material(Shader.Find(...))` repeated | Create once, cache/reuse |
| `string` concatenation in hot paths | `FixedString` or StringBuilder |
| `FindObjectOfType<T>()` | Dependency injection or singleton pattern |
| `GetComponent<T>() ?? AddComponent<T>()` | `var c = Get..; if (c == null) c = Add..;` — `??`/`?.` bypass Unity's null override |
| `obj == null` / `!obj` in hot paths | `ReferenceEquals(obj, null)` or `obj is null` — avoids native interop (~5x faster). Only use `== null`/`!obj` when destroyed-detection needed |
| `Camera.main` in Update/LateUpdate | Cache in `Awake()`/`Start()`: `_mainCam = Camera.main;` — calls `FindGameObjectWithTag` every time |
| `if (myObj != null)` on UnityEngine.Object | `if (myObj)` — implicit bool is cheaper; `== null` calls native bridge via overridden operator |
| Type name collision (e.g. `InventoryGridCell` in 2 namespaces) | `using GridCellUI = DOTSUI.InventoryGridCell` to disambiguate |

## `init`-Only Setters and `record` Types — Not Supported in Unity Mono

Unity's Mono / .NET Standard 2.1 scripting profile does **not** ship `System.Runtime.CompilerServices.IsExternalInit`. Any type using `{ get; init; }` or C# positional `record` types compiles to `CS0518: Predefined type 'System.Runtime.CompilerServices.IsExternalInit' is not defined or imported` in Unity 6 and earlier.

| Anti-Pattern | Correct Pattern |
|---|---|
| `public T X { get; init; }` | `public T X { get; private set; }` (or `{ get; set; }`) |
| Positional `record` (`record Foo(int X)`) | Plain `[Serializable]` class with public fields |

**Shim (only if `init`/`record` semantics are truly needed):** add one file to the project:
```csharp
namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }
```
**Note for JSON DTOs:** `JsonUtility` serializes public fields only; prefer `[Serializable]` classes with public fields over `init`-based records.

Evidence: 2026-06-08, StickmanForge_IdleRPG, `GeminiRequestDtos.cs` — `{ get; init; }` produced CS0518 in Unity 6; fixed to `{ get; private set; }`.

## Partial-Class File Naming — Concatenate, Do NOT Dot-Suffix

The pre-commit hook in DOTS-AI (and inherited by consumer projects) blocks dot-suffix file names. Use concatenated PascalCase for partial-class files.

**Bad:**
- `MySystem.Camera.cs`
- `MySystem.UI.cs`

**Good:**
- `MySystemCamera.cs`
- `MySystemUI.cs`

**Why:** dot-suffix forms work in some IDEs but break the project's pre-commit hook (file-name validator rejects multiple dots in stem). Concatenated form works everywhere — Unity asmdef discovery, hooks, OS filesystems, IDE refactors.

**Apply to:** all `partial class` / `partial struct` splits across runtime, editor, and tests. Evidence: SceneSetupUtility (5 partials), ColorFitSceneSetup (5 partials), InventoryGridUtility (4 partials), NeighborEffectSystem (2 partials) all use concatenated naming.