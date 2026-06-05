---
name: t1k:designer:base:liveops
description: Live-ops design — content cadence, daily/weekly/monthly events, retention loops, engagement metrics, calendar planning, KPI-driven content release, D7/D30 retention, UA scale-up moments, seasonal marketing beats
effort: medium
keywords: [live-ops, liveops, retention, calendar, weekly event, daily quest, engagement, content cadence, season, recurring content]
version: 1.13.0
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Game Live-Ops Design

## When This Skill Triggers
- Designing a live-ops calendar (daily/weekly/monthly)
- Planning content cadence or content release schedule
- Designing daily quests, weekly challenges, or login rewards
- Designing seasonal content or recurring events
- Planning retention loops around engagement KPIs
- Mapping live-ops calendar to UA/marketing moments
- Analyzing D7/D30 retention and planning interventions

## Decision Tree

| Intent | Path |
|---|---|
| Design daily/weekly cadence | [Content Cadence](#content-cadence) |
| Plan retention loop architecture | [Retention Loops](#retention-loops) |
| Map calendar to marketing moments | [Marketing Perspective](#marketing-perspective) |
| Define live-ops KPIs | [KPI Framework](#kpi-framework) |
| BPC case study reference | [Case Study: BackpackCrawler](#case-study-backpackcrawler) |
| Anti-patterns reference | [Anti-Patterns](#anti-patterns) |

## Content Cadence

### Daily Layer (24h cycle)
- **Daily quests**: 3 easy + 1 medium + 1 hard; complete 3/5 for milestone bonus; midnight local refresh
- **Login reward**: day 1-7 escalating; day 7 = premium reward (hard currency or rare item)
- **Daily deal**: 1 limited-quantity shop item at 30-50% off (creates urgency, drives store habit)
- **Ad offer**: 1 rewarded-ad bonus per day per slot (IAP-light engagement)

### Weekly Layer (7-day cycle)
- **Weekly challenge**: meta-objective tied to game mode; reward = cosmetic
- **Rotating shop**: weekly refresh of 5-8 items; 2 items purchasable per player per week
- **Mid-week surprise**: Wednesday minor event or double-drop to combat mid-week dip

### Monthly/Seasonal Layer (28-90 day cycle)
- **Season/battle pass**: 60-90 days; free track (10 rewards) + premium (30 rewards); narrative theme
- **Major event**: LTE anchored to IRL calendar or story beat — see `game-events` skill
- **Meta season**: ranked reset + season-exclusive cosmetic drives competitive retention

→ See `references/calendar-templates.md` for full annual calendar blueprint

## Retention Loops

### Core Loop Architecture
```
Daily habit → Weekly investment → Monthly aspiration
    |               |                    |
Login bonus    Weekly challenge     Season pass value
Daily quests   Rotating shop        Ranked reset
Daily deal     Social weekly        Exclusive cosmetic
```

### KPI Targets (mid-core F2P)
D1 50% (floor 40%) | D7 20-22% (floor 15%) | D30 8-10% (floor 6%) | DAU/MAU ≥0.25 | Session/DAU ≥2.5

**Industry context ([GameAnalytics 2025](https://www.gameanalytics.com/reports/2025-mobile-gaming-benchmarks)):** D7 median = 3.4-3.9%; top-quartile = 7-8%. Hitting 20%+ requires category-leader execution — do not treat 18-20% as a floor.

→ See `references/kpi-framework.md` for full metric definitions, thresholds, ARPDAU benchmarks, and intervention playbook

## KPI Framework

- **ARPDAU** (Average Revenue Per Daily Active User): event weeks should lift ARPDAU 15-30% vs baseline
- **LTV extension**: each major season adds ~14-21 days to LTV (season-engaged players 2× less likely to churn that month)
- **Event attach rate**: % of DAU who engage with active event; target ≥ 60% within 48h
- **Content completion rate**: % of event milestones completed; < 30% = too hard; > 80% = too easy

→ See `references/kpi-framework.md` for ARPDAU model, D7 dual-threshold, and per-segment lift expectations

## Marketing Perspective

Live-ops is not just retention — it is a paid UA / organic discovery flywheel.

### D7/D30 → CAC Payback
- Each +1% D7 improvement ≈ 3-5% reduction in effective CAC payback period
- Strong D30 (>8%) unlocks higher CPI bids on Meta/Google/AppLovin
- Live-ops calendar should be shared with UA team 4 weeks in advance

### Seasonal Events as Marketing Moments
- **App store featuring**: seasonal events → "New Content" badge → feature slot eligibility
- **TikTok-able beats**: design 1 visually unique moment per major event (skin reveal, countdown animation)
- **Influencer drops**: 5-10 mid-tier creators (100K-1M followers) for early access 3 days before launch

→ See `references/marketing-perspective.md` for UA calendar alignment, influencer playbook, real-world examples

## Case Study: BackpackCrawler / Backpack Inu

> Full detail: `references/kpi-framework.md` §BPC. Sources: Story.md §12, Systems.md §2.3-2.4 §20, `revenue-mix.json` (locked 2026-04-29, corrected 2026-05-02)

- **3 monthly major events**: "The Annual Picnic" (Wk 4), "Black Friday Returns" (Wk 8), "Quarterly Earnings" (Wk 12, 8-Act gauntlet + Mythic reward)
- **5 rotating weeklies**: Take Your Pet to Work Day, Casual Friday, All-Hands Meeting, Pizza Party, Office Olympics
- **Wk 11 dead-week mitigation**: "Mochi's Photo Album" mini-event (~15h prod) — lore re-collection, D7-equivalent retention lift
- **ARPDAU realistic base**: $0.17-0.19 (BP at 10% MAU conv, ad-free pass at 4% blended, rewarded 36% of mix)
- **D7 dual-threshold**: target 20% / floor 18% — auto-revert to Tier 2 IAA if D7 < 18% any week

## Recent Comparable Games — Live-Ops Cadence

→ Full table in `references/kpi-framework.md` §Benchmarks. Summary:
- **Survivor.io**: bi-weekly + seasonal peaks; $5-6M/mo at year 3 ([Gamesforum](https://www.globalgamesforum.com/news/how-survivor.io-continues-to-pull-in-5-million-a-month-three-years-later))
- **Royal Match**: daily jackpot events + weekend tournaments (Propeller Rush = +14.6% revenue); $2B+ 2024 ([PocketGamer](https://www.pocketgamer.biz/royal-match-earned-51-of-all-match-3-revenue-in-2024/))
- **Genshin Impact**: 6-week patch cycle; double-banner ~+30% uplift; $0.7B 2024 ([Bittopup](https://news.bittopup.com/news/genshin-impact-2025-15.2m-players-0.8b-revenue))
- **Marvel Snap**: monthly (first Monday); season pass + thematic cross-promo ([Marvel Snap Zone](https://marvelsnapzone.com/seasons/))
- **Brawl Stars**: ~6-8 week brawler drops + Brawl Pass; Brawl Talk cadence
- **Stumble Guys**: monthly IP collab (My Hero Academia, Avatar: TLA, PAC-MAN) ([Scopely](https://www.scopely.com/en/news/stumble-guys-invites-players-to-master-the-elements-with-new-avatar-the-last-airbender-collaboration))

**AppMagic 2025**: midcore games run 20-40 events/month; grew 73→89 events/mo overall; seasonal peak October. ([AppMagic LiveOps 2025](https://appmagic.rocks/research/liveops-report-2025))

## Anti-Patterns
| # | Anti-Pattern | Fix |
|---|-------------|-----|
| 1 | No mid-week event (DAU dips Wed–Thu) | Add Wed surprise: double drop, flash deal |
| 2 | Same cadence every week | Rotate event types; surprise every 3rd week |
| 3 | Event too short (<24h) | Minimum 48h; major events = 5-7 days |
| 4 | No marketing alignment | Share calendar 4 weeks early with UA team |
| 5 | Live-ops ignores seasonality | Map to CNY, summer, Halloween, year-end |
| 6 | Modeling best-in-class as base case | BP at 10% MAU conv, ad-free at 4% blended |

## Gotchas
- **D7 18-20% is a stretch, not a floor** — GameAnalytics 2025 median is 3.4-3.9%; top-quartile is 7-8%. Plan with eyes open.
- **Live-ops density ≠ quality** — adding events without tuning earn-rate math dilutes event currency value
- **Dead-week mitigation is mandatory** — Week 11 equivalent (low-prod mini-event) prevents churn between major events
- **No proven cozy-IAA-mobile-roguelite precedent at scale exists** — benchmarks from HKIA (premium) are invalid as IAA peers
- **Battle pass conversion industry-typical = 8-12% MAU** — not 28%; model accordingly before presenting to stakeholders

## Cross-References
- `game-events` — Detailed LTE design (event currency, shop, monetization)
- `game-economy-design` — Event economy sinks/faucets, event currency design
- `game-mobile-design` — Session design aligned to live-ops cadence
- `game-quest-design` — Daily/weekly quest structure and reward hooks

## Reference Files
| File | Coverage |
|------|----------|
| `references/calendar-templates.md` | Annual calendar blueprint, event scheduling templates |
| `references/kpi-framework.md` | Metric definitions, ARPDAU benchmarks, D7 dual-threshold, intervention playbook |
| `references/marketing-perspective.md` | UA alignment, real-world examples, influencer playbook, store checklist |
