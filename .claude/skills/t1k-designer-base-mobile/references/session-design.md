---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Session Design

## Session Length Targets

| Metric | Target | Rationale |
|--------|--------|-----------|
| Per-level duration | 3-5 min | Fits commute/break micro-sessions |
| Per-session duration | 15-20 min | Under thumb fatigue threshold |
| Sessions per day | 3-5 | Matches casual mobile habit loop |
| Weekly playtime | 45-90 min total | Sustainable without burnout |

## Session Structure (Bookends)

**Session Start** (first 30 seconds):
1. Daily reward popup — immediate positive reinforcement
2. "Welcome back!" message if >24h absence
3. Pending notifications resolved (idle rewards ready, guild gifts)
4. Entry into last active game state (no main menu friction)

**Session End** (last 30 seconds):
1. Progress saved indicator (explicit "Saved!" checkmark — never silent)
2. Daily quest progress summary ("2/3 completed")
3. Soft "come back" hook: "Next reward in 2 hours" or "Guild event ends in 4h"
4. Never force-show shop/IAP offer on session exit (dark pattern, triggers uninstall)

## Micro-Session Design (<2 min)

Ensure meaningful progress is possible in under 2 minutes:
- **Collect idle rewards**: one-tap collection, visible on app icon badge
- **Quick battle**: auto-battle mode, skip cutscenes, fast result screen
- **Daily quest check-in**: log in + collect = 30 seconds
- **Guild gift sending**: one tap per friend, max 5 gifts/day
- **Inventory management**: drag-and-drop sorting, bulk sell/discard

**Design rule**: if the minimum useful action takes >2 min, casual players disengage.

## Save-Anywhere Protocol

- **Auto-save trigger**: after every discrete action (battle end, item collected, room cleared)
- **Save granularity**: save mid-dungeon state, not just floor entry
- **Resume precision**: load into exact state — same room, same HP, same inventory
- **Save indicator**: brief "cloud" or "checkmark" icon flash after each save (reassures player)
- **Conflict resolution**: if save fails (no network), queue locally and sync on next launch

## Interruption Handling

- **Phone call received**: auto-pause immediately, no input needed
- **App backgrounded**: save state, pause all timers, release audio focus
- **App foregrounded**: resume from exact state, no loading screen if <5 min backgrounded
- **Notification tap**: deep-link to relevant screen (not main menu)
- **Battery warning (20%)**: show "Low battery — save and exit?" prompt

## Loading Times

| Context | Target | Maximum |
|---------|--------|---------|
| Cold start (first launch) | <3s | 5s |
| Hot resume (<5 min) | <1s | 2s |
| Level load | <2s | 4s |
| Asset streaming mid-session | Invisible | — |

**Loading screen tips**: show game tips, lore snippets, or progress bar — never blank screen.
**Perceived speed trick**: start audio and show partial UI immediately, load assets in background.

## Offline Progress (Idle Rewards)

- **Accumulation cap**: 8-12 hours — beyond this, player doesn't gain more
- **Display on return**: "You earned 450 gold while away!" animation
- **Soft currency only**: never award hard currency offline (devalues premium)
- **Notification at cap**: push notification when offline cap reached ("Your storage is full!")
- **Prestige mechanic**: late-game upgrade that extends offline cap to 24h (sink for late-game currency)

## Common Session Flow Patterns

**Idle/Casual loop** (3-5 min):
1. Open app → collect idle rewards (30s)
2. Send/receive guild gifts (30s)
3. Auto-battle one dungeon floor (2 min)
4. Check daily quests (30s)
5. Close app

**Engaged loop** (15-20 min):
1. Open → collect + gifts (1 min)
2. Manual 3-floor dungeon run (10 min)
3. Upgrade equipment with loot (2 min)
4. Complete daily quests (2 min)
5. Check event progress (1 min)

## Gotchas
- **Energy systems**: time-gated energy frustrates players who want to play more → use soft-energy (refills fast) or optional energy boost for power players
- **Timer anxiety**: if countdown timers are always visible, players feel pressured → show timers only when relevant (on the building/slot, not HUD)
- **Session too long by default**: mandatory tutorials stretching >10 min → cap tutorial to 5 min max, make rest skippable
