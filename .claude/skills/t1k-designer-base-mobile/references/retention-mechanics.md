---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Retention Mechanics

## Retention Targets

| Cohort | Casual F2P | Mid-core F2P | Hardcore F2P |
|--------|-----------|-------------|-------------|
| Day 1 (D1) | 35-45% | 45-55% | 50-60% |
| Day 7 (D7) | 12-20% | 18-28% | 25-35% |
| Day 30 (D30) | 4-8% | 8-14% | 12-20% |

**Formula**: D7 ≈ D1 × 0.40; D30 ≈ D7 × 0.35 — use these ratios to project forward from early data.

## Daily Login Rewards

**7-Day Cycle Design**:
- Day 1: small soft currency (100 gold) — entry reward
- Day 2: consumable item (1 health potion)
- Day 3: medium soft currency (300 gold)
- Day 4: rare crafting material
- Day 5: large soft currency (600 gold) + minor hard currency (5 gems)
- Day 6: uncommon item or skin piece
- Day 7: large hard currency (30 gems) + exclusive cosmetic

**Streak rules**:
- Bonus multiplier for consecutive streak (Day 7 reached = 1.5× next cycle)
- **Gentle reset**: missing 1 day resets streak multiplier but NOT daily position
- **Never punish**: missing a day never removes accumulated rewards
- Show "comeback bonus" message on return: "+50 gold for coming back!"

## Daily Quests

**Standard daily quest set (refresh at midnight local time)**:
- Easy × 3: "Win 2 battles", "Collect 100 gold", "Open 1 chest" (5 min each)
- Medium × 1: "Complete a dungeon floor", "Upgrade 1 item" (10-15 min)
- Hard × 1: "Win 3 battles in a row", "Reach floor 5" (20+ min)

**Completion bonus**: completing 3/5 grants daily quest reward (soft currency + event points).
**Full completion bonus**: all 5/5 grants premium reward (hard currency or exclusive item).
**Design rule**: casual players complete 3/5 daily through normal play — no grinding required.

## Weekly Events

| Event Type | Duration | Reward Type |
|-----------|----------|-------------|
| Score attack | 7 days | Exclusive cosmetic tier list |
| Cooperative guild event | 5 days | Guild reward pool (shared) |
| Limited-time dungeon | 3 days | Exclusive item only in this event |
| Boss rush challenge | 2 days | Premium currency + leaderboard title |

**Event cadence**: at least 1 active event at all times — no "dead weeks".
**Exclusive rewards**: event-exclusive items cannot be obtained anywhere else (FOMO driver).
**Catch-up**: events have catch-up mechanics after day 3 (double progress rate for latecomers).

## Social Hooks

- **Friend list**: see friends' progress, send/receive gifts (5/day)
- **Guild contributions**: daily donation increases guild level → unlock guild shop
- **Leaderboards**: friend-scope only (not global) — new players can compete meaningfully
- **Co-op events**: guild boss requiring coordinated attacks — shared contribution, shared reward
- **Gifting**: send free soft currency to friends (capped, not exploitable) — triggers reciprocity

**Gotcha**: global leaderboards destroy new player motivation — always filter to friends + nearby rank range.

## Push Notification Strategy

**Maximum 2 notifications per day.** More = uninstall trigger.

| Trigger | Message | Timing |
|---------|---------|--------|
| Offline cap reached | "Your storage is full! Collect your rewards." | When 8-12h cap hit |
| Daily reset | "Your daily quests are ready!" | Morning (9-10 AM local) |
| Event ending soon | "Event ends in 4 hours — final chance!" | 4h before deadline |
| Win-back (3 days absent) | "We miss you! Your comeback reward is waiting." | Day 3 absence |
| Guild event | "Your guild needs you — boss arrives in 1h!" | 1h before event |

**Rules**:
- Always opt-in (never opt-out) — request permission after first positive moment in game
- Respect quiet hours: never send between 10 PM and 8 AM local time
- Deep-link to relevant screen (not main menu)
- Include specific value in message ("500 gold", not "rewards")

## FTUE (First Time User Experience)

**Target duration**: 5-10 minutes from install to first win.

**Step sequence**:
1. Skip/minimal account creation (guest play first, register later)
2. Tutorial: 3-5 forced actions maximum (tap here → drag here → confirm)
3. **First battle**: guaranteed win (scripted outcome, not RNG) — critical for D1
4. First reward drop: visual excitement, animation, sound — make it feel amazing
5. Core loop demonstrated: earn → upgrade → fight (one full cycle in tutorial)
6. Skip option: available from step 2 onward (experienced players rage-quit forced tutorials)

**D1 retention correlation**: games with guaranteed first win have 8-12% higher D1 retention.

## Win-Back Mechanics

| Absence Duration | Trigger | Offer |
|-----------------|---------|-------|
| 1 day | Nothing | — |
| 3 days | Push notification | "Comeback reward: 200 gold" |
| 7 days | Email + push | "We miss you — here's a free item" |
| 30 days | Email | "So much has changed! New content + 500 gems" |
| 90+ days | Lapsed player campaign | "Fresh start bonus: everything you missed" |

**Win-back reward rule**: reward must be meaningful (enough to make progress) but not so large it invalidates current players' grind.
