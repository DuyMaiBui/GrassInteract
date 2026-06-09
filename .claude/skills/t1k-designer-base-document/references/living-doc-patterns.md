---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Living Documentation Patterns

## Design-as-Code Philosophy

Code constants are the single source of truth. Docs reflect them — never lead them.

| Truth Source | Example | Doc Field |
|-------------|---------|-----------|
| SceneSetup.cs constant | `const int UnitCount = 50` | Unit Types table count |
| PrefabCreator.cs | `baseStats.HP = 100` | Stats table HP column |
| MenuItem attribute | `[MenuItem("Tools/Demo/Setup")]` | Editor Tools menu path |
| System class name | `class CrawlerEncounterSystem` | Systems section class name |

**Rule**: Before writing any number or name in a wiki, grep the source file.
```bash
grep -n "const\|readonly\|static" "Assets/Demos/{Name}/Editor/{Name}SceneSetup.cs"
```

## Wiki Page Structure (Demo-*.md)

All demo wiki pages follow the 14-section structure defined in the `game-designer` agent.
Keep each wiki page under 300 lines. Extract deep-dives to `docs/wiki/Domain-*.md` and link.

```
docs/wiki/
├── Demo-BattleDemo2D.md        # <300 lines, links to Domain pages
├── Demo-BackpackCrawler.md
├── Domain-CombatSystem.md      # Deep dive, no line limit
├── Domain-InventoryGrid.md
└── Architecture.md
```

## Sync Triggers

| Code Change | Sections to Update |
|-------------|------------------|
| SceneSetup constant changed | Scene Structure, How to Run |
| PrefabCreator stats changed | Unit Types / Content Matrix |
| New system added to Runtime/ | Systems section |
| MenuItem path renamed | Editor Tools table |
| New authoring field added | Demo-Specific Components |
| BDP tree modified | Systems section (AI behavior) |
| Library module added | Library Coverage table |
| UI panel added/removed | Game Flow section |

## Auto-Detection Pattern

To find all constants needing documentation review after a code change:
```bash
# Find all tunable constants in a demo
grep -rn "const\|readonly static\|SerializeField" \
  "Assets/Demos/{DemoName}/" --include="*.cs" \
  | grep -v "//\|\.meta" \
  | sort

# Find all MenuItem paths (Editor Tools section)
grep -rn 'MenuItem("' "Assets/Demos/{DemoName}/Editor/" --include="*.cs"

# Find all system class names (Systems section)
grep -rn "class.*System" \  # adjust pattern for your engine
  "Assets/Demos/{DemoName}/Runtime/" --include="*.cs"
```

## Cross-Reference Conventions

### Between Wiki Pages
Use relative markdown links:
```markdown
→ See [CombatSystem deep dive](Domain-CombatSystem.md)
→ See [Architecture overview](Architecture.md#simulation-systems)
```

### Wiki ↔ Skills
Reference skills for implementation details, not design details:
```markdown
> Implementation: see engine implementation skills → relevant config class
> Navigation setup: see `agents-navigation` skill
```

### Wiki ↔ CLAUDE.md
CLAUDE.md Quick Reference section links to wiki for context:
```markdown
- **DerivedStatsSystem**: requires 7 components — see [Domain-StatsSystem](docs/wiki/Domain-StatsSystem.md)
```

## Update Verification Checklist
After updating any wiki page:
- [ ] All unit counts match current SceneSetup constants
- [ ] All stat values match current PrefabCreator values
- [ ] All system class names match current Runtime/ files
- [ ] All MenuItem paths match current `[MenuItem("...")]` attributes
- [ ] Cross-reference links resolve (no broken anchors)
- [ ] Line count under 300 (extract to Domain-*.md if over)
- [ ] Related pages updated (search for pages linking to this one)
---

## Pattern — Decision-Point Tracker

When a long-running cook makes ≥3 autonomous design decisions (AFK mode), surface them all in ONE place so a returning user can audit and reverse before the decisions calcify into shipped systems.

**Where:** a `## Open Decision Points` section in the demo's wiki page (`docs/wiki/Demo-{Name}.md`) AND a long-form plan-file copy at `plans/{date}-{slug}/user-decision-points.md`.

**Per-decision schema:**

| Field | Purpose |
|---|---|
| **Title** | One-line "what was decided" — readable in a TOC |
| **Context** | The forcing function — what data or constraint required the call |
| **Decision** | The exact call made (with values, formula, threshold, file path) |
| **Alternatives considered** | 2-4 other paths and why they were rejected |
| **Reversal cost** | "5 min code change" / "1 day refactor + balance re-run" / "shipped to playtest — cannot reverse" |
| **Status** | RESOLVED (user confirmed), DEFERRED (pending input), OPEN (no input yet) |
| **Reference** | Commit SHA or PR # where the decision was implemented |

**When to add an entry:**
- AFK-mode AI picks a magic number that affects gameplay tuning (boss HP, drop rate, cooldown)
- AI chooses between two plausible architectures (event-bus vs polling, singleton vs per-entity)
- AI fills a documented `??` / `TBD` / `decide later` slot in the plan
- AI defaults to "ship the simpler one" when the user wasn't there to weigh in

**Anti-pattern:** burying decisions in commit messages — they're invisible to the user who returns 8 hours later and reads only the wiki. The decision-point section is the "what changed under the hood" cheat sheet.

Reproduced 2026-05-23 in DOTS-AI ChaosForge cook (sleep-run) — 14 autonomous decisions surfaced in `plans/260523-1543-chaosforge-demo/user-decision-points.md` and `docs/wiki/Demo-ChaosForge.md` § "Open Decision Points". User audited and resolved 11 / 14 within the first morning session.

## Pattern — Pillar Audit (final convergence pass)

Before declaring a feature shipped, run a final pass that re-reads the **design pillars** (the 3-5 sentences in the GDD that define what makes the game *that game*) and audits every shipped system against them. Catches drift that incremental review misses.

**Procedure:**
1. Copy the design pillars verbatim into a scratch table.
2. For each shipped system / mechanic / UI element, ask: "Which pillar does this serve, and does the implementation fulfil that promise?"
3. Flag mismatches as `PILLAR-DRIFT` findings with severity (high if pillar IS the game's USP; low if pillar is a secondary feel goal).
4. Resolve high-severity drift before ship; defer low-severity to a follow-up if time pressure exists.

**Schema for the audit doc** (lives at `plans/{date}-{slug}/pillar-audit-{round}.md`):

```markdown
## Pillar 1 — "[Pillar text from GDD]"
| Shipped element | Serves pillar? | Notes |
|---|---|---|
| ForgeRollSystem | ✓ | Variance + risk match "uncertainty in every craft" |
| InventoryGrid | ⚠ partial | Slot count UI buries cap visibility — see PILLAR-DRIFT-3 |
| BossUnlockToast | ✗ | Too celebratory for "anxious progression" tone — see PILLAR-DRIFT-7 |
```

**When to run:**
- End of every milestone (alpha, beta, RC, gold)
- After any major balance pass that touched ≥3 systems
- When the team-lead notices "feels off" but can't pin a specific bug — pillar drift is often the cause
- Before a public playtest

**Output:** a numbered list of `PILLAR-DRIFT-N` findings with: severity, the pillar being missed, the shipped element, the proposed fix (or "accept drift — pillar was aspirational").

Reproduced 2026-05-23 in DOTS-AI ChaosForge cook (Phase 10 R5, `p10r5-pillar-fidelity` teammate, SHA `34799dc1`). Final pass surfaced 6 drift findings (4 high, 2 low) — 4 fixed in R5, 2 deferred to post-cook polish.

## Pattern — Shipped vs Canon Disclaimer Block

When a wiki page documents BOTH the full design canon (e.g., a 10-era universe) AND a compressed shipped slice (e.g., a 3-realm demo), include a callout block at the top of every affected section so future readers don't confuse aspirational lore with actual content.

**Macro template:**

```markdown
> 📦 **Shipped vs Canon:** This page documents the [canon X — full N-element universe]; the current demo ships [shipped Y — N' compressed elements]. See [link to mapping doc] for the shipped-slice → canon mapping.
```

**When to use:**
- Wiki page is the design canon (long horizon, lots of TBD / future / planned content)
- The demo ships a compressed slice of canon (3 of 10 realms, 1 of 5 factions, the tutorial only)
- A future reader could mistake canon content for "implemented and playable"

**Where to place:**
- Top of the wiki page (1 callout for the whole page)
- AND inside any section where canon and shipped diverge significantly (e.g., a `Realms` table where the demo only ships 3 of 10)

**Anti-pattern:** silent canon documentation. A reader reads "the player travels through 10 realms" and thinks "the demo ships 10 realms." When they boot it up and find 3, trust is damaged.

**Optional enforcement:** add a wiki linter check that flags pages with TBD / future / planned tokens AND no `Shipped vs Canon` callout in the top 30 lines.

Reproduced 2026-05-23 in DOTS-AI ChaosForge cook (Phase 9b, SHA `cf606b74`) — 4 of 6 sibling wiki pages (Realm-Progression, Combat, Forge-System, Equipment) needed retrofit callouts because the shipped 3-realm slice silently contradicted the 10-era canon. The `📦 Shipped vs Canon` macro became the standard fix.

