---
name: t1k:designer:base:events
description: Limited-time event design — LTE mechanics, event currency, event shop, seasonal/collab events, event monetization, event art briefs, press cycle, influencer activation, store featuring alignment
effort: medium
keywords: [event, LTE, limited-time, seasonal event, collab, event banner, event shop, event currency, themed event]
version: 1.13.2
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Game Events Design

## When This Skill Triggers
- Designing a limited-time event (LTE) from scratch
- Designing event currency, event shop, or event milestones
- Planning a seasonal event (Halloween, CNY, Holiday)
- Designing a collaboration event (IP collab, crossover)
- Monetizing an event (event banner, bundle, premium pass)
- Writing event names or copy that doubles as marketing
- Planning event art brief aligned to store screenshots

## Decision Tree

| Intent | Path |
|---|---|
| Design LTE structure end-to-end | [LTE Architecture](#lte-architecture) |
| Design event economy (currency, shop) | [Event Economy](#event-economy) |
| Plan event monetization | [Event Monetization](#event-monetization) |
| Plan event marketing | [Marketing Perspective](#marketing-perspective) |
| IP collab event | `references/event-design-templates.md` → Collab section |
| BPC case study reference | [Case Study: BackpackCrawler](#case-study-backpackcrawler) |
| Anti-patterns reference | [Anti-Patterns](#anti-patterns) |

## LTE Architecture

### Event Tiers
| Tier | Duration | Systems |
|------|----------|---------|
| Minor | 2-3 days | Daily quest variant |
| Standard | 5-7 days | Event currency + shop |
| Major | 14-21 days | Event pass + shop + banner |
| Season | 60-90 days | Battle pass + ranking + LTE |

### Event Flow Checklist
- [ ] Event name reads as marketing copy (evocative, memeable, searchable)
- [ ] Entry hook playable within 2 min of login
- [ ] Daily event quest yields visible event currency progress
- [ ] Milestone track shows final exclusive reward upfront (FOMO anchor)
- [ ] Shop refreshes mid-event for re-engagement
- [ ] Countdown timer visible in final 24h
- [ ] Post-event: exclusive rewards "retired" — visible but uncollectable

→ `references/event-design-templates.md` — event brief template, milestone track, collab checklist

## Event Economy

- **Single event currency** per event (multiple = drop in engagement)
- **Earn split**: daily quest 40% / event mode drop 40% / login 20%
- **Earn rate**: free player earns ~70% of shop over full event duration
- **Shop size**: 6-10 items; 1 exclusive + 3-4 re-run items + 2-3 consumables
- **Mid-event refresh**: swap 2-3 items at midpoint — announce on social
- **Currency expiry**: 7-day grace period after event ends (urgency without punishment)

→ `references/event-economy.md` — earn rate tables, pricing formulas, currency flow model

## Event Monetization

```
Revenue tier order:
  1. Event banner / limited-time gacha (highest)
  2. Event premium pass (paid track)
  3. Event bundle ($4.99-$9.99: exclusive cosmetic + 2× currency + booster)
  4. Premium bundle ($19.99-$29.99: same + extra cosmetic + banner pull)
  5. Currency top-up / accelerator
```
- Banner pity: per-banner counter only — does NOT carry across banners
- Reveal banner contents 48h before launch (community hype cycle)

→ `references/event-monetization.md` — bundle formulas, banner rates, pity tables

## Marketing Perspective

Events are marketing. Every decision should be evaluated for shareability, pressability, UA leverage.

### Event Name as Marketing Copy
- Name must be evocative and searchable (players search for guides/leaks)
- Test: "Would a TikTok thumbnail with just this name get clicks?"
- Good: "Phantom Eclipse Festival", "Crimson Harvest", "Neon Carnival"
- Avoid: "Spring Event 2026", "Monthly Challenge", "New Content"

### D1/D7/D30 Impact
- Major LTE: event-week D7 cohort typically 5-10% higher than non-event cohort
- ARPDAU during major events: +25-50% vs baseline
- Post-event LTV: event completers show 30-40% lower churn in following 30 days
- Event creative outperforms evergreen UA ads by 1.5-3× CTR

### Store Screenshots (Mandatory for Major Events)
- Update screenshots 2 days before event launch (Apple review buffer)
- Screenshot 1: event key art + name; Screenshot 2: exclusive cosmetic
- Submit for Apple In-App Events slot (free editorial-adjacent placement)

→ `references/event-marketing-playbook.md` — press cycle, influencer brief, store checklist

## Case Study: BackpackCrawler / Backpack Inu

> Full detail: `references/event-monetization.md` §BPC + `references/event-design-templates.md` §BPC. Sources: Story.md §12, Systems.md §2.3-2.4 §3.3 §20, `revenue-mix.json` (locked 2026-04-29, Round 3 corrections 2026-05-02).

### 3 Monthly Major Events (12-week season)
- **Wk 4 — "The Annual Picnic"**: cozy seasonal, exclusive Picnic Blanket cosmetic + 2× Seeds drop weekend
- **Wk 8 — "Black Friday Returns"**: re-run vault, shop-bundle anchor, premium pass refresh tier
- **Wk 12 — "Quarterly Earnings"**: 8-Act gauntlet (whole campaign back-to-back) + Mythic-tier reward drop

### 5 Rotating Weeklies
Take Your Pet to Work Day · Casual Friday · All-Hands Meeting · Pizza Party · Office Olympics — each ~15-25h prod, theme-tied to the workplace satire pillar; Wk 11 gets "Mochi's Photo Album" mini-event (lore re-collection, dead-week mitigation).

### Event Monetization (Tier 3 Heavy IAA stack)
- **Banner pity (per-banner, no carry):** 1.6% featured rate displayed; soft pity at 70 pulls; hard pity at 90 pulls
- **Bundle attach to event:** Standard $4.99 (4-5× value) → Premium $9.99 (6-8× value) → Whale $49.99 (10× banner pulls + cosmetics)
- **Event ARPDAU lift:** target +25-50% vs $0.17-0.19 base case (realistic_base_corrected scenario)
- **D7 dual-threshold:** target 20% / floor 18% — auto-revert kill-switch to Tier 2 if D7 < 18% any week

### Cozy-IAA Compromise (Acknowledged)
Tier 3 ad density (interstitial 1/run + 3-banner placements + push pressure) erodes cozy perception 5-10% per soft-launch survey. **Mitigations baked into every event:** Frank-voice sponsor framing on every ad surface, dual-tier ad-free pass escape valve ($4.99 / $9.99), opt-in push only with anti-FOMO copy, hard exclusions on emotional moments (post-death, FTUE, Day-1).

### Why This Matters for Event Design
Every event LTE in BPC inherits the cozy-pillar contract: **event design must front-load Frank-voice sponsor framing and back-load the ad-free pass cross-sell.** No event ships without a 1-question Pollfish cozy-perception survey at Wk 4 + Wk 8 (target ≥3.8/5; floor 3.4/5).

## Recent Comparable Games — Event Design

→ Full per-game breakdown in `references/event-design-templates.md` §Benchmarks. Summary:

- **Royal Match — Propeller Rush**: weekend tournament, +14.6% revenue lift, single-mechanic tournament structure ([PocketGamer](https://www.pocketgamer.biz/royal-match-earned-51-of-all-match-3-revenue-in-2024/))
- **Survivor.io**: bi-weekly events sustain $5-6M/mo at year 3; TikTok-driven UA + event creative outperforms evergreen 2-3× CTR ([Gamesforum](https://www.globalgamesforum.com/news/how-survivor.io-continues-to-pull-in-5-million-a-month-three-years-later))
- **Genshin Impact — double-banner events**: ~+30% revenue uplift on 6-week patch cycle; banner reveal 48h before launch is the press-cycle gold standard ([Bittopup](https://news.bittopup.com/news/genshin-impact-2025-15.2m-players-0.8b-revenue))
- **Marvel Snap — monthly seasons**: first-Monday cadence + thematic cross-promo + season-pass anchor; predictable cadence = press-cycle-friendly ([Marvel Snap Zone](https://marvelsnapzone.com/seasons/))
- **Brawl Stars — Brawl Pass + brawler drops**: 6-8 week cadence + Brawl Talk reveal cycle (community hype 72h before launch)
- **Stumble Guys — IP collabs**: monthly collab cadence (Avatar: TLA, My Hero Academia, PAC-MAN, NERF, Hot Wheels) — collab events outperform original IP events by 1.5-2× peak DAU ([Scopely](https://www.scopely.com/en/news/stumble-guys-invites-players-to-master-the-elements-with-new-avatar-the-last-airbender-collaboration))

**AppMagic 2025**: midcore games run 20-40 events/month; industry grew 73→89 events/mo per game; seasonal peak is October (Halloween portfolio = ~9 concurrent events at top games). ([AppMagic LiveOps 2025](https://appmagic.rocks/research/liveops-report-2025))

## Anti-Patterns
| # | Anti-Pattern | Problem | Fix |
|---|-------------|---------|-----|
| 1 | Generic event name | Not searchable, not shareable | Evocative name = marketing copy |
| 2 | Exclusive only via gacha | Regulatory risk; player hostility | Always provide a f2p grind path |
| 3 | No store screenshot update | Missed featuring opportunity | Update screenshots for every major event |
| 4 | Collab without legal clearance | IP dispute forces removal | Legal review before art production |
| 5 | Banner pity carries globally | Devalues future banners | Pity resets per-banner |
| 6 | Event shop static all event | No return visit hook | Mid-event item refresh |

## Cross-References
- `game-liveops` — Cadence: how events fit into the monthly live-ops calendar
- `game-economy-design` — Event currency earn rates, shop pricing
- `game-shop-offering` — Event shop offering algorithms and pity mechanics
- `game-quest-design` — Daily event quest structure, milestone reward hooks

## Reference Files
| File | Coverage |
|------|----------|
| `references/event-design-templates.md` | Event brief template, milestone tracks, collab checklist |
| `references/event-economy.md` | Event currency earn rates, shop pricing, currency flow model |
| `references/event-monetization.md` | Banner design, bundle formulas, pity tables, revenue tiers |
| `references/event-marketing-playbook.md` | Press cycle, influencer brief, store screenshot checklist |
