---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Tuning Knobs

## Primary Combat Parameters

| Knob | Low Value Effect | High Value Effect | Recommended Range | Cap |
|------|-----------------|-------------------|-------------------|-----|
| ATK (base) | Grindy, damage feels weak | One-shot risk | 15-50 | Context-dependent |
| DEF (base) | Squishy, high lethality | Unkillable tanks | 3-25 | 75% DR effective cap |
| MaxHP | Low TTK, punishing | Slow, grindy fights | 80-300 (base) | — |
| AttackSpeed | Strategic, positional | Spammy, micro-intensive | 0.5-2.0 atk/s | 4.0 atk/s hard cap |
| CritRate | Consistent damage | Variance-heavy, frustrating | 5-35% | 75% soft, 90% hard |
| CritDamage | Low reward for crits | Gambling-reliant | 1.5-2.5× | 3.0× hard cap |
| MoveSpeed | Strategic, positional | Chaotic, hard to read | 3.0-7.0 units/s | 12 units/s |

## ATK/DEF Ratio

```
Ratio = EffectiveATK / EffectiveDEF
```

| Ratio | Combat Feel | Common Use |
|-------|------------|-----------|
| < 1.5 | Very tanky, grindy | Tower defense, survival |
| 1.5-3.0 | Balanced | Standard RPG, auto-battler |
| 3.0-5.0 | Bursty, readable TTK | Action RPG, roguelike |
| > 5.0 | One-shot risk | Arena shooter, dodge-or-die |

## Cooldown Design

| Duration | Design Intent | Example Abilities |
|----------|--------------|------------------|
| 0.5-2s | Spammable, reactive | Basic ability, dodge |
| 3-8s | Tactical, meaningful decisions | Secondary skill |
| 10-20s | Strategic resource | Burst heal, crowd control |
| 30-60s | Game-changer | Ultimate ability |
| Per-encounter | Run-defining | Boss phase skip, nuke |

**Rule**: If an ability is always used on cooldown → too short. If players forget to use it → too long.

## HP Scaling Per Level

| Growth Rate | Formula | Effect at Level 10 vs Level 1 |
|------------|---------|------------------------------|
| 10% linear | `HP × (1 + L × 0.1)` | 2× HP |
| 15% linear | `HP × (1 + L × 0.15)` | 2.5× HP |
| Exponential 1.1× | `HP × 1.1^L` | 2.6× HP |
| Exponential 1.15× | `HP × 1.15^L` | 4.0× HP |

Target: Level 10 boss should feel significantly harder than Level 1, but not 10× harder.
Recommended: 2.5-4× HP growth from Level 1 to max level.

## Loot Generosity

| Setting | Items per 10 encounters | Shop frequency | Player feel |
|---------|------------------------|----------------|------------|
| Stingy | 3-5 items | Every 8 enc. | Scarcity, tension |
| Normal | 6-9 items | Every 5 enc. | Steady progress |
| Generous | 10-14 items | Every 3 enc. | Power fantasy |
| Flooded | 15+ items | Every 2 enc. | Choice paralysis, trivialized |

**Roguelike sweet spot**: 7-10 items per 10-encounter run (avg 0.7-1.0 per encounter).

## Stat Modifier Caps

| Modifier Type | Soft Cap | Hard Cap | Why |
|--------------|----------|----------|-----|
| Flat ATK bonus | — | 200% of base | Prevents infinite stack |
| PercentAdd ATK | 100% total add | 200% | Diminishing returns above 100% |
| PercentMult ATK | — | 3.0× | Beyond this, content becomes trivial |
| DEF% (damage reduction) | 60% (diminishing) | 75% | Prevents unkillable |
| CritRate | 50% (diminishing) | 75% | Reduces variance to acceptable level |
| MoveSpeed | — | 12 units/s | Gameplay readability |
| AttackSpeed | — | 4.0 atk/s | Animation/hitbox reliability |

## Interaction Effects (Common Combinations)

| Knob A | Knob B | Interaction | Watch For |
|--------|--------|-------------|-----------|
| High ATK | Low DEF across board | Snowball — first hit often decisive | Insert early DEF scaling |
| High CritRate | High CritDamage | Multiplicative explosion | Both need individual caps |
| High AttackSpeed | Lifesteal | Full HP regen mid-combat | Cap lifesteal at 30% |
| High MoveSpeed | Large arenas | Trivializes positioning | Scale arena size with speed |
| Low CooldownDuration | AoE ability | Spam clears encounters instantly | Min 3s on any AoE |
| High loot drop | Short run | Players have too many items before first boss | Reduce drops in first 3 encounters |

## Applying Changes Safely

1. Change one knob at a time
2. Re-run stat audit grep after change
3. Run automated balance tests: `dotnet test` or Unity Test Runner
4. Verify TTK range in matchup matrix (recalculate affected rows)
5. Playtest 3+ full runs at recommended level
6. If balance test fails → revert, investigate, re-tune smaller increment
