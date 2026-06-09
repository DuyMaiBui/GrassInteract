---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Balance Spreadsheet

## DPS Calculator

```
ATK            = PhysATK or MagATK (derived stat)
DEF            = target Defense (derived stat)
DamagePerHit   = max(1, ATK - DEF × reductionRate)
CritMultiplier = 1 + CritRate × (CritDamage - 1)
DPS            = DamagePerHit × AttackSpeed × CritMultiplier
```

**Spreadsheet layout (columns)**:
| A: UnitType | B: ATK | C: DEF | D: ReductionRate | E: AS | F: CritRate | G: CritDmg | H: DPS |
|-------------|--------|--------|-----------------|-------|-------------|------------|--------|
| Formula in H: `=MAX(1,B-C*D)*E*(1+F*(G-1))` |

## EHP Calculator

```
DamageReduction = min(DEF_Percent, 0.75)    // hard cap 75%
EHP             = MaxHP / (1 - DamageReduction)
```

**Spreadsheet layout**:
| A: UnitType | B: MaxHP | C: DEF% | D: EHP |
|-------------|---------|---------|--------|
| Formula in D: `=B/(1-MIN(C,0.75))` |

## TTK Calculator

```
TTK = TargetEHP / AggressorDPS
```

**Full matchup matrix (N×N)**:
- Rows = Aggressors, Columns = Targets
- Each cell: `=EHP[col] / DPS[row]`
- Color-code: red <2s (one-shot risk), green 3-10s (balanced), yellow >15s (grindy)

## Item Power Budget

```
Budget = BaseBudget × RarityMult × SlotMult
```

**Spreadsheet layout**:
| A: ItemName | B: BaseBudget | C: Rarity | D: RarityMult | E: SlotSize | F: SlotMult | G: TotalBudget | H: AllocatedPoints |
|-------------|-------------|---------|------------|---------|----------|------------|----------------|
| Formula in G: `=B*D*F` |
| Formula in H: `=SUM(stat columns)` |
| Validation: H should equal G (flag if over/under budget) |

RarityMult lookup: Common=1.0, Uncommon=1.3, Rare=1.7, Epic=2.2, Legendary=3.0
SlotMult lookup: 1×1=0.6, 1×2=1.0, 2×2=1.8, L-shape=1.4, 2×3=2.5

## Encounter Difficulty Rating

```
EncounterRating = EnemyCount × AvgEnemyEHP × AvgEnemyDPS / PlayerEHP / PlayerDPS
// Balanced encounter: rating ≈ 1.0
// Easy: < 0.7 | Hard: 1.3-1.7 | Spike: > 2.0
```

**Per-encounter table**:
| A: Index | B: EnemyCount | C: AvgHP | D: AvgATK | E: PlayerHP | F: PlayerATK | G: Rating |
|----------|-------------|---------|---------|-----------|------------|---------|
| Formula in G: `=B*C*D/(E*F)` — simplified; adjust for DEF/EHP |

## Expected Damage Per Encounter

```
ExpectedDmgTaken = EnemyCount × EnemyDPS × TimeToKillAllEnemies
// TimeToKillAllEnemies = SUM(EnemyEHP[i] / PlayerDPS)
```

Use to estimate HP potions needed per encounter and shop economy sizing.

## Gold Economy Per Session

```
GoldEarned  = SUM(EncounterReward[i] for i in run)
GoldSpent   = ItemsPurchased × AvgItemCost + Rerolls × RerollCost
GoldBalance = GoldEarned - GoldSpent
// Target: GoldBalance > 0 at run end (player never fully broke)
// Target: GoldBalance < 30% of best-item cost (not swimming in gold)
```

**Run economy table**:
| A: Encounter | B: GoldReward | C: ShopAppears | D: GoldSpent | E: RunningBalance |
|-------------|-------------|--------------|------------|-----------------|
| Formula in E: `=E[prev]+B-D` |

## Drop Rate Simulation

Expected items of rarity R over N encounters:
```
Expected(R, N) = N × BaseRate(R) × AvgLuckMult × AvgAreaMult
// With pity: adjust upward by ~1/(PityThreshold × BaseRate) items
```

**Quick simulation** (no code needed):
| A: Rarity | B: BaseRate | C: N=10 encounters | D: N=50 | E: N=100 |
|-----------|-----------|------------------|--------|--------|
| Common | 55% | `=B*10` | `=B*50` | `=B*100` |
| Rare | 12% | 1.2 | 6.0 | 12.0 |
| Legendary | 1% | 0.1 | 0.5 | 1.0 |

If Legendary expected < 1.0 at run end → pity timer essential.
