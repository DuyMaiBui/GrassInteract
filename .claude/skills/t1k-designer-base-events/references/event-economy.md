---
title: Event Economy — Earn Rates and Shop Pricing
skill: game-events
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Event Economy

## Earn Rate Model

### Target Earn Rate Per Day
```
Daily quest completion (full): 120 event currency
Event mode drop (3 runs avg):  100 event currency
Login bonus:                    30 event currency
Total daily earn:               250 event currency
```

### Event Duration → Total Free Earn
| Duration | Total free earn | Shop target (70%) |
|----------|-----------------|-------------------|
| 3-day minor | 750 | 525 |
| 7-day standard | 1750 | 1225 |
| 14-day major | 3500 | 2450 |

### Shop Pricing Formula
```
Exclusive cosmetic:    total_free_earn × 0.65 (earnable by dedicated f2p)
Premium re-run item:   total_free_earn × 0.30
Standard re-run item:  total_free_earn × 0.15
Consumable (×5 limit): total_free_earn × 0.08 per unit
```

---

## Currency Flow Model

```
Player plays daily                  Player buys top-up
        |                                   |
  Earn event currency ←──────────────────── +
        |
  Spend in event shop
        |
  Exclusive cosmetic (1× only)
  Re-run items (purchase-limited)
  Consumables (purchase-limited)
        |
  Remaining currency → expires at end of grace period
```

**Grace period**: 7 days after event ends. Prevent last-day panic spending.

---

## Event Shop Sell-Through Targets

| Item type | Target sell-through |
|-----------|-------------------|
| Event exclusive | 55-70% of eligible players |
| Premium re-run | 20-35% |
| Consumables | 40-60% |

If exclusive sell-through < 40%: event currency earn rate was too low or event was too short.
If exclusive sell-through > 80%: event was too easy — reduce earn rate or add higher aspirational tier.
