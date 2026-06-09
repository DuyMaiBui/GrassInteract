---
title: Live-Ops KPI Framework
skill: game-liveops
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Live-Ops KPI Framework

## Core Retention Metrics

### Retention Benchmarks by Genre
| Genre | D1 | D7 | D30 |
|-------|----|----|-----|
| Puzzle/Casual | 35-45% | 15-20% | 5-8% |
| RPG/Mid-core | 40-50% | 18-25% | 7-12% |
| Shooter/Action | 30-40% | 12-18% | 4-8% |

### Intervention Playbook
| KPI Below Threshold | Root Cause Check | Fix |
|--------------------|-----------------|-----|
| D1 < 40% | FTUE too long or confusing | Cut FTUE to <5 min; guarantee first win |
| D7 < 15% | No daily habit formed | Add daily quest + login reward streak |
| D30 < 7% | No long-term aspiration | Season pass or ranking reset needed |
| DAU/MAU < 0.20 | Daily content exhausted | Add daily deal + rotating shop |
| Session count < 2/day | Sessions too long | Add micro-session entry points |

---

## Live-Ops Event KPIs

### Event Health Metrics
- **Attach rate**: % of DAU who start the event within first 48h. Target: ≥60%
- **Completion rate**: % of players who reach final milestone. Target: 40-70% (outside = tuning issue)
- **Event ARPDAU**: compare to baseline week. Target: +20-40% during Tier 1 events
- **D+7 post-event retention**: players who completed event should show +5-10% D7 vs non-participants

### Revenue Attribution
- **Event IAP conversion**: players who engage with event should convert at 2× baseline rate
- **Event bundle attach**: limited-time bundles launched alongside events should sell 60-80% of inventory in first 24h
- **Battle pass attach rate**: season launch week = highest BP purchase rate (20-35% of eligible players)

---

## Live-Ops Health Dashboard

### Weekly Metrics Review
```
Monday morning review:
- DAU vs last week (same day) — flag if >10% swing
- D7 retention cohort (players who installed 7 days ago)
- Weekly challenge completion rate
- Rotating shop sell-through rate
- Event participation if active

Red flags to escalate:
- DAU down >15% week-over-week (no event scheduled)
- D7 < 12% two weeks in a row
- Event attach rate < 40% at 48h post-launch
- Event completion rate > 80% (event too easy)
```

---

## LTV Modeling for Live-Ops

### Simplified LTV Impact Estimate
```
Base LTV (no live-ops)    = ARPU × avg days retained
With daily live-ops       = Base LTV × 1.3-1.5
With seasonal events      = Base LTV × 1.5-2.0
With battle pass engaged  = Base LTV × 2.5-3.5 (highest cohort)
```

### CAC Payback Period Impact
- Every +1% D30 retention improvement = ~4% reduction in effective CAC payback
- A game hitting D30=10% vs D30=6% can justify 40% higher CPI bids on same UA channels
- Strong live-ops → higher LTV → higher sustainable CPI → better inventory access on Meta/Google/AppLovin
