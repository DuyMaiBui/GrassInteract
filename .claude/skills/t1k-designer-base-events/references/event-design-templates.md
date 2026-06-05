---
title: Event Design Templates
skill: game-events
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Event Design Templates

## Standard Event Brief Template

```
EVENT BRIEF
-----------
Name:           [Evocative name — also your marketing copy]
Theme:          [Visual theme + narrative hook in 1 sentence]
Tier:           [Minor | Standard | Major | Season]
Duration:       [X days] — [Start date] to [End date]
Game mode:      [New mode | Existing mode variant | Passive collect]

REWARDS
-------
Free tier:      [Item 1 (milestone 3)] [Item 2 (milestone 5)] [Item 3 (milestone 7)]
Event exclusive: [Name] — retired after event, shown in collection forever
Paid tier:      [Optional premium track or bundle]

ECONOMY
-------
Event currency: [Name] — expires [X] days after event ends
Earn sources:   Daily quest: [X/day] | Event mode: [Y/game] | Login: [Z/day]
Free earn total: [X] over full event duration
Shop cost total (all items): [Y] — free player can afford [Z]%

MARKETING
---------
Art direction:  [Key visual — hero character, palette, visual hook]
Store screenshots: Update [screenshot 1, 2] on launch day
Press pitch:    [Tier 1 = yes | Tier 2+ = no]
Influencer:     [Count and tier] — early access starts [date]
Push notif:     Subject: [copy] | Body: [copy] | CTA: [copy]
UA creative:    [Key visual for creatives — same hero element]
```

---

## Milestone Track Structures

### 7-Milestone Standard Track (5-7 day event)
```
Milestone 1 (day 1):  Minor consumable (accessible — drives day 1 login)
Milestone 2 (day 2):  Soft currency pack
Milestone 3 (day 3):  Event shop voucher / bonus event currency
Milestone 4 (day 4):  Rare item from permanent pool
Milestone 5 (day 5):  Cosmetic (frame, avatar, emote)
Milestone 6 (day 6):  Premium cosmetic (event variant, limited palette)
Milestone 7 (day 7):  Event Exclusive (most aspirational; only earnable in this event)
```
Rule: milestone 7 visible from day 1 — FOMO anchor. 60-70% of players should reach it.

### 5-Milestone Minor Track (2-3 day event)
```
Milestone 1: Currency pack
Milestone 2: Consumable bundle
Milestone 3: Soft currency
Milestone 4: Cosmetic
Milestone 5: Limited exclusive
```

---

## Event Flow Patterns

### Pattern A — Daily Quest Loop (most common)
```
Login → Complete 3 daily event quests → Earn event currency
     → Spend in event shop → Track milestone progress
     → Repeat next day
```

### Pattern B — Event Mode Run
```
Enter event-specific game mode → Complete run → Earn event currency (variable by score)
     → Spend in shop → Milestone tracks run completions
```

### Pattern C — Passive Collection (mobile idle)
```
Login → Event collector ticks passively → Collect on return
     → Currency auto-earned over time → Spend at shop milestone
     → Creates return visit habit
```

---

## §Benchmarks — Recent Comparable Games (Event Design)

> All URLs verified live 2026-05-03. Source rule: `~/.claude/rules/url-verification.md`.

| Game | Event Cadence | Signature Pattern | Revenue/UA Note | Source |
|---|---|---|---|---|
| Royal Match | Daily jackpot + weekend tournament | Propeller Rush single-mechanic tournament = +14.6% revenue weekend | $2B+ 2024; 51% of all match-3 revenue | [PocketGamer](https://www.pocketgamer.biz/royal-match-earned-51-of-all-match-3-revenue-in-2024/) |
| Survivor.io | Bi-weekly + seasonal peaks | TikTok-driven UA + event creative outperforms evergreen 2-3× CTR | $5-6M/mo at year 3 | [Gamesforum](https://www.globalgamesforum.com/news/how-survivor.io-continues-to-pull-in-5-million-a-month-three-years-later) |
| Genshin Impact | 6-week patch cycle + double banners | Banner reveal 48h before launch = press cycle gold standard | $0.7B 2024; double-banner +30% uplift | [Bittopup](https://news.bittopup.com/news/genshin-impact-2025-15.2m-players-0.8b-revenue) |
| Marvel Snap | Monthly (first Monday) | Predictable cadence + thematic cross-promo = press-cycle-friendly | Season pass anchor revenue | [Marvel Snap Zone](https://marvelsnapzone.com/seasons/) |
| Brawl Stars | 6-8 week brawler drops + Brawl Pass | Brawl Talk reveal cycle = 72h hype runway | Sustained 5+ year live-ops | Supercell community |
| Stumble Guys | Monthly IP collab | Avatar TLA, MHA, PAC-MAN, NERF, Hot Wheels — 1.5-2× peak DAU vs original IP | Peak collab events drive UA spike | [Scopely](https://www.scopely.com/en/news/stumble-guys-invites-players-to-master-the-elements-with-new-avatar-the-last-airbender-collaboration) |

**AppMagic 2025**: midcore games run 20-40 events/month; industry grew 73→89 events/mo per game; seasonal peak October. ([AppMagic LiveOps 2025](https://appmagic.rocks/research/liveops-report-2025))

**Sensor Tower 2025**: top-grossing games structure live-ops as overlapping portfolios — Halloween example shows 9 concurrent events at top 10 grossing puzzle/midcore games. ([Sensor Tower](https://sensortower.com/live-ops-strategies-2025-report))

### Cadence Heuristics by Genre
- **Match-3 / casual puzzle**: daily mini-event + weekend tournament + monthly major (Royal Match pattern)
- **Roguelite / midcore action**: bi-weekly LTE + monthly major (Survivor.io pattern)
- **Gacha RPG**: 6-week patch cycle + 2-week banner rotation (Genshin pattern)
- **Card battler**: monthly season pass + thematic LTE every 2 weeks (Marvel Snap pattern)
- **Cozy / collection**: monthly major + weekly mini + opt-in social events (BPC pattern, first-of-kind)

---

## §BPC — BackpackCrawler Event Templates

> Sources: Story.md §12 (3 monthly events), Systems.md §2.3-2.4 §3.3, `revenue-mix.json` (2026-04-29 lock + 2026-05-02 corrections).

### 3 Monthly Major Events (12-week season)

#### Wk 4 — "The Annual Picnic"
- **Theme:** cozy seasonal — workplace-picnic satire ("mandatory fun")
- **Mechanic:** 2× Seeds drop weekend, exclusive Picnic Blanket cosmetic
- **Banner:** Mochi-themed pet accessory line (per-banner pity, 1.6%/70/90)
- **Bundle:** $4.99 picnic bundle (Frank-voice framing: "Frank's deal — bring the family")
- **Cozy guardrail:** zero push notifications during event; opt-in only

#### Wk 8 — "Black Friday Returns"
- **Theme:** retail satire — "limited-time" jokes about FOMO
- **Mechanic:** re-run vault (3 past LTEs accessible for 1 week) + 2× shop-bundle anchor
- **Banner:** premium-cosmetic-only banner (no power items — cozy pillar #1 preserved)
- **Bundle:** $9.99 premium pass refresh tier (10% MAU conversion target — realistic, not best-in-class)
- **Cozy guardrail:** anti-FOMO copy review on all push templates and shop framing

#### Wk 12 — "Quarterly Earnings"
- **Theme:** workplace-finale satire — full 8-Act gauntlet back-to-back
- **Mechanic:** Mythic-tier reward drop on completion (rotates per season)
- **Banner:** Mythic cosmetic banner with bundle pity bonus (+10 pulls per Standard bundle)
- **Bundle:** $49.99 whale tier with 10× banner pulls + 2 cosmetics + season pass refresh
- **Cozy guardrail:** Mochi pity-counter UI (visual, not nag); no FOMO countdown on whale tier

### 5 Rotating Weeklies (15-25h prod each)
- Take Your Pet to Work Day · Casual Friday · All-Hands Meeting · Pizza Party · Office Olympics
- All anchor to workplace satire pillar; cycle on 5-week rotation
- Each yields theme-tied cosmetic + 1 day of 1.5× run rewards

### Wk 11 Mini-Event — "Mochi's Photo Album"
- **Purpose:** dead-week mitigation between Wk 10 and Wk 12 finale
- **Prod cost:** ~15h (lore re-collection mini-event, no new mechanics)
- **Retention lift:** D7-equivalent uplift per soft-launch projection
- **Cozy alignment:** narrative-only, zero monetization

### Validation Gates per Event (Mandatory)
1. **Wk 4 + Wk 8** in-game cozy-perception survey (1-question, Pollfish, target ≥3.8/5; floor 3.4/5)
2. **D7 cohort tracking**: target ≥20%, floor ≥18% — auto-revert kill switch on floor breach
3. **Push opt-in rate**: target ≥40% post-FTUE; floor ≥25% — rewrite consent prompt below floor
4. **Banner pity reset audit**: never carry across events (legal + player-trust)
