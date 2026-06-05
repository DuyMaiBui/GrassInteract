---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Demo Wiki Page Template

## Mandatory Section Order (from game-designer agent)

Every `docs/wiki/Demo-{Name}.md` MUST contain these 14 sections in order:

```markdown
# Demo: {DemoName}

> One-liner: What this demo proves (e.g., "Proves DOTS RPG library supports 2D top-down auto-battle at 2K units")

## Overview
- Key feature 1
- Key feature 2
- Key feature 3

## Design Principles
| Principle | Choice | Rationale |
|-----------|--------|-----------|
| Rendering | URP/Unlit + cutout | SRP Batcher compatibility |
| Navigation | Agents Nav 4.4.4 | Crowd scale |

## Scene Structure
- **Main Scene**: {Name} — Camera, Lighting, UI Canvas, Navigation mesh
- **SubScene**: {Name}SubScene.unity — all ECS entities (spawners, singletons)
  - SpawnerBlue, SpawnerRed (TeamSpawnerAuthoring)
  - CameraTarget (CameraTargetAuthoring)
  - ArenaConfig (ArenaConfigAuthoring)

## Unit Types
| Type | Count/Side | HP | ATK | Speed | Special |
|------|-----------|-----|-----|-------|---------|
| Melee | 25 | 150 | 30 | 4.5 | — |
| Ranged | 10 | 80 | 45 | 3.0 | 12-unit range |
| Boss | 1 | 500 | 60 | 2.0 | Phase at 50% HP |

## Demo-Specific Components
| Component | Purpose |
|-----------|---------|
| `{Name}Tag` | Marks entities belonging to this demo |
| `EncounterIndex` | Tracks which encounter the player is on |

## Systems
### New Systems (Demo-specific)
| System | Group | Purpose |
|--------|-------|---------|
| `{Name}WinConditionSystem` | SimulationSystemGroup | Detect all enemies dead → trigger win |

### Modified Library Systems (via Authoring)
| System | Change |
|--------|--------|
| `DerivedStatsSystem` | StatFormulaConfig tuned for this demo |

### Unchanged (Reused)
- `CombatSystem`, `ProjectileSystem`, `NavigationSystem` — no demo-specific changes

## Editor Tools
| Menu Path | Script | Purpose |
|-----------|--------|---------|
| `Tools/{Name}/Setup Scene` | `{Name}SceneSetup.cs` | Full scene regeneration |
| `Tools/{Name}/Create Unit Prefabs` | `{Name}UnitPrefabCreator.cs` | Generate all unit prefabs |
| `Tools/{Name}/Build Behavior Trees` | `{Name}BDPTreeBuilder.cs` | Attach BDP trees to prefabs |

## How to Run
1. Open `Assets/Demos/{Name}/Scenes/{Name}.unity`
2. Enter Play mode
3. Observe [what to watch for]

## How to Recreate (if scene corrupted)
1. `Tools/{Name}/Create Unit Prefabs`
2. `Tools/{Name}/Build Behavior Trees`
3. `Tools/{Name}/Setup Scene`
4. Clear `Library/EntityScenes/` → re-enter Play

## Game Flow
```
Idle → Spawn (both teams) → Battle → WinCondition
         ↓                              ↓
    [teams fight]              [all enemies dead → ShowResult]
```
Win condition: [describe]
Lose condition: [describe, if applicable]

## Troubleshooting
| Symptom | Cause | Fix |
|---------|-------|-----|
| Units stand idle | BDP trees missing | Run "Build Behavior Trees" after "Create Unit Prefabs" |
| Units invisible | Camera farClipPlane < CameraHeight | Set `farClipPlane = CameraHeight + 50f` |
| Stats at 0 | DerivedStatsSystem missing required component | Verify all 7 required components present |

## Library Coverage
| Module | Active | Systems Used |
|--------|--------|-------------|
| Combat | Yes | CombatSystem, DamageProcessingSystem |
| Navigation | Yes | AgentNavigationBridgeSystem |
| AI | Yes | AIUpdateTierSystem, BehaviorTreeSystemGroup |
| Spawning | Yes | TeamSpawnerSystem |
| Stats | Yes | DerivedStatsSystem |
| Inventory | No | — |

## Related Documentation
- [Architecture overview](Architecture.md)
- [Library reference](Domain-DotsRpgLibrary.md)
```

## How to Gather Info from Code

### Unit Counts (from PrefabCreator or SceneSetup)
```bash
grep -n "count\|Count\|units\|Units" "Assets/Demos/{Name}/Editor/{Name}UnitPrefabCreator.cs"
grep -n "count\|Count\|units\|Units" "Assets/Demos/{Name}/Editor/{Name}SceneSetup.cs"
```

### Stat Values (from PrefabCreator)
```bash
grep -n "baseStats\|HP\|ATK\|DEF\|Speed\|Range" "Assets/Demos/{Name}/Editor/{Name}UnitPrefabCreator.cs"
```

### Arena Size
```bash
grep -n "ArenaSize\|Width\|Height\|arena" "Assets/Demos/{Name}/Editor/{Name}SceneSetup.cs"
```

### System Class Names
```bash
grep -rn "class.*System" "Assets/Demos/{Name}/Runtime/" --include="*.cs" | grep -v "//\|Test"
```

### MenuItem Paths
```bash
grep -rn 'MenuItem("' "Assets/Demos/{Name}/Editor/" --include="*.cs"
```
