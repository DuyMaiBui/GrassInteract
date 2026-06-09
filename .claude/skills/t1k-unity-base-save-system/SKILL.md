---
name: t1k:unity:base:save-system
description: Save/load system patterns — JSON/binary serialization, versioning, migration, cloud save, and encryption for Unity 6. Use when implementing game persistence.
effort: medium
keywords: [save system, persistence, data, unity]
version: 2.2.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---

# Unity Save System — Persistence Patterns

Save system patterns for Unity 6. No built-in save framework — must implement.

## Storage Locations

```csharp
string savePath = Application.persistentDataPath;
// Windows: AppData/LocalLow/<company>/<product>/
// iOS/Android: platform Documents/files dirs

// PlayerPrefs — settings ONLY, never game saves:
PlayerPrefs.SetInt("MusicVolume", 80);
```

→ See `references/save-patterns.md` for all platform paths, full paths per OS.

## Save Data Architecture

```csharp
[System.Serializable]
public class SaveData {
    public int version = 1;        // For migration
    public string timestamp;
    public PlayerSaveData player;
    public List<QuestSaveData> quests;
    public WorldSaveData world;
}
```

**Gotcha**: `JsonUtility` doesn't support `Dictionary` or `float3`. Use `float[]` arrays or Newtonsoft.Json.

## Serialization Methods

| Method | Use When | Notes |
|--------|----------|-------|
| `JsonUtility` | Simple data, no Dictionary | Fast, no package needed |
| Newtonsoft.Json | Complex types, polymorphism | More flexible |
| `BinaryWriter` | Performance-critical | Manual field writing |
| ~~BinaryFormatter~~ | Never | Deprecated, insecure |

→ See `references/save-patterns.md` for full SaveManager, binary, versioning, auto-save, and encryption code.

## Key Patterns

- **Versioning**: Include `int version` in SaveData, migrate in `LoadWithMigration()`
- **Auto-save**: `InvokeRepeating` + `OnApplicationPause` + `OnApplicationQuit`
- **Encryption**: XOR for obfuscation; `System.Security.Cryptography.Aes` for real security
- **Atomic write**: Write to temp file, then rename to avoid corruption

## Common Gotchas

1. **WebGL**: No filesystem — use `PlayerPrefs` or `IndexedDB` via JS interop
2. **Threading**: File I/O on main thread causes hitches — use `async/await`
3. **iOS**: iCloud backup includes `persistentDataPath` by default
4. **Save corruption**: Always write temp → rename (atomic operation)

## Related Skills & Agents
- `unity-addressables` — Loading saved asset references
- `unity-mobile` — Platform-specific save paths
- `unity-scene-management` — Save/load scene state
- `dots-rpg` — DOTS game state (use `dots-implementer` agent)

## Reference Files
| File | Contents |
|------|----------|
| `references/save-patterns.md` | Full code: JSON, binary, versioning, auto-save, encryption, gotchas |
