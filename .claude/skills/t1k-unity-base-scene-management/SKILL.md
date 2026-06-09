---
name: t1k:unity:base:scene-management
description: Scene loading (async/additive), transitions, DontDestroyOnLoad patterns, and bootstrap architecture for Unity 6. Use when managing scenes or loading screens.
effort: medium
keywords: [scene management, loading, scene, unity]
version: 2.2.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity Scene Management — Loading, Transitions & Architecture

Scene management reference for Unity 6. For DOTS SubScenes, see `dots-ecs`.

## Basic Loading

```csharp
using UnityEngine.SceneManagement;

SceneManager.LoadScene("GameLevel");                               // Sync by name
SceneManager.LoadScene(1);                                        // By build index
SceneManager.LoadScene("UI_Overlay", LoadSceneMode.Additive);    // Keep current scene
```

All scenes must be added to File → Build Settings.

## Async Loading

```csharp
AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
op.allowSceneActivation = false;       // Hold at 90% until ready
while (op.progress < 0.9f) yield return null;
op.allowSceneActivation = true;
```

**Gotcha**: `progress` caps at 0.9 when `allowSceneActivation = false` — this is intentional.

→ See `references/scene-patterns.md` for full loading-bar coroutine, additive patterns, event callbacks.

## Bootstrap Architecture

```
Bootstrap.unity (index 0) → loads Persistent.unity (additive, never unloads)
                           → loads MainMenu.unity (additive)
                           → unloads Bootstrap
```

Use additive scenes for: UI layers, audio managers, lighting, gameplay zones.

→ See `references/scene-patterns.md` for Bootstrap code, DontDestroyOnLoad pattern, fade transition.

## Key Callbacks

```csharp
SceneManager.sceneLoaded += OnSceneLoaded;
SceneManager.sceneUnloaded += OnSceneUnloaded;
SceneManager.activeSceneChanged += OnActiveSceneChanged;
// Always unsubscribe in OnDisable
```

## Common Gotchas

1. **Scene not in Build Settings**: `LoadScene` fails silently
2. **Static references survive**: Reset static variables manually on scene load
3. **Additive scene lighting**: Each scene bakes own lightmaps — use one lighting scene
4. **FindObjectOfType**: Searches ALL loaded scenes — be explicit

## Related Skills & Agents
- `unity-addressables` — Addressable scene loading
- `unity-audio` — Persistent audio across scenes
- `dots-ecs` — DOTS SubScenes (use `dots-implementer` agent)
- `dots-battlefield` — Scene setup (use `dots-environment` agent)

## Reference Files
| File | Contents |
|------|----------|
| `references/scene-patterns.md` | Full code: async loading, additive, events, bootstrap, fade transition, gotchas |
