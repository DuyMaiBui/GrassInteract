---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# GDD Template

## 1. Game Overview
| Field | Value |
|-------|-------|
| Title | |
| Genre | (e.g., Roguelike, Action RPG, Auto-Battler) |
| Platform | (PC / Mobile / Console) |
| Target Audience | (e.g., core gamers 18-35, casual mobile) |
| Core Loop | [Action] → [Reward] → [Upgrade] → [Harder Action] |
| Session Length | (e.g., 10-15 min per run) |

## 2. Game Pillars
3-5 non-negotiable tenets. Every feature must serve at least one pillar.

| Pillar | Description | Examples |
|--------|-------------|---------|
| P1: Name | One-sentence principle | Feature A, B |
| P2: Name | One-sentence principle | Feature C, D |

## 3. Gameplay Mechanics

### Core Loop
```
[Entry] → [Main Activity] → [Decision] → [Outcome]
               ↓                               ↓
           [Reward]                      [Failure State]
               ↓
         [Progression] → [Harder Entry]
```

### Combat
- Attack types: melee / ranged / AoE / projectile
- Targeting: nearest / priority / area
- Cooldowns: active abilities vs passive
- Status effects: stun, burn, slow, etc.

### Inventory / Items
- Grid size: N×M cells; item shapes: 1×1, L-shape, 2×1
- Slot types: weapon, armor, accessory, consumable
- Stack rules: consumables stackable, legendaries unique

### Progression
- Level range: 1-N; stat growth per level: [formula]
- Unlock gates: level N required for feature X

## 4. Game Flow

### Phase State Machine
```
MainMenu → Run: Encounter → [Reward] → NextEncounter
                    ↓ win              ↓ loss
               RunComplete         GameOver → Retry
```

- Win: [defeat final boss / survive N rounds]
- Lose: [HP reaches 0]
- Encounters per run: N; boss at: [indices]; rest/shop at: [indices]

## 5. UI/UX Design

### Screen Flow
```
MainMenu → Play → Mode Select → Game → Pause → Resume / Quit
         → Settings / Profile
```

### HUD Elements
| Element | Location | Updates When |
|---------|----------|-------------|
| HP bar | Top-left | HP changes |
| Inventory | Bottom 35% | Item picked up |
| Round counter | Top-center | Encounter starts |

- Back navigation: always available
- Modal confirm for destructive actions (quit run)

## 6. Economy & Monetization
| Currency | Source | Sink |
|----------|--------|------|
| Gold | Encounters, chests | Shop, upgrades |
| Gems | Milestones, IAP | Premium shop |

Drop rates: Common 60%, Uncommon 25%, Rare 10%, Epic 4%, Legendary 1%
Pity timers: Rare 20 enc., Epic 50 enc., Legendary 100 enc.
Shop: 3-5 items/visit; reroll = GoldBase × visit_count

## 7. Technical Requirements
| Requirement | Target |
|-------------|--------|
| Target FPS | 60 (mobile), 60+ (PC) |
| Memory Budget | 512MB (mobile), 2GB (PC) |
| Draw Call Budget | <100 (mobile), <500 (PC) |
| Load Time | <3s transitions |
| Save Format | MemoryPack binary |

## 8. Art & Audio Direction
- Renderer: URP only; palette: [description]; style: [pixel/illustrated/3D mesh/billboard]
- Music: [adaptive/stinger/loop]; SFX: attack, death, UI confirm, UI cancel, item pickup
- Ambient: per-biome loops

## 9. Content Matrix

| Enemy | HP | ATK | DEF | Speed | Special |
|-------|-----|-----|-----|-------|---------|
| Basic | 100 | 20 | 5 | 4 | — |
| Ranged | 60 | 30 | 2 | 3 | 10-unit range |
| Boss | 500 | 50 | 20 | 2 | Phase at 50% |

| Encounter Range | Type | Enemy Count | Scaling |
|----------------|------|-------------|---------|
| 1-3 | Tutorial | 5-10 | None |
| 4-9 | Normal | 10-20 | +30%/enc |
| 10 | Mini-boss | 1 + 5 adds | ×2 |

## 10. Balance Parameters
| Parameter | Value | Min | Max | Notes |
|-----------|-------|-----|-----|-------|
| BaseATK | 20 | 10 | 50 | Scales with STR |
| BaseDEF | 5 | 0 | 30 | Cap 75% DR |
| HPPerLevel | 15% | 5% | 25% | Exponential feel |
| EncounterScaling | 1.3× | 1.1× | 2.0× | Per encounter index |
| LootGeneration | 1.0 avg | 0.5 | 2.0 | Luck-adjusted |

→ See `game-balance-tools` skill for formulas and validation methods
