---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Library Feature Discovery Protocol — Research 3× Before Implementing New

**MANDATORY** for any DOTS-AI implementer, planner, or cook teammate before adding new combat / inventory / progression / puzzle / nav / spawn / UI-bridge functionality.

## Rule

When you need a feature AND the corresponding library skill (e.g. `t1k:unity:dots-combat:rpg`, `t1k:unity:dots-core:ecs-core`, `t1k:unity:dots-inventory:grid`) does NOT mention it:

1. **Research the whole library 3 times** before assuming the feature doesn't exist. Each pass uses a different angle — name, behavior, callers.
2. **If found** → update the library skill with what you discovered (gotcha, reference doc, pattern), THEN use the existing library code.
3. **If NOT found** (after all 3 passes) → implement it in the library (not the demo), THEN update the library skill so the next implementer inherits the knowledge.

Either way, the skill is updated. The skill is the durable memory; manual rediscovery is the bug.

## The 3 research passes

| # | Angle | Commands |
|---|---|---|
| 1 | **Name / token search** | `grep -rln "<FeatureName>" Packages/unity-dots-library/` — exact identifier match (component, system, method, namespace) |
| 2 | **Behavior / concept search** | `grep -rln "<behavior-keyword>" Packages/unity-dots-library/` — describe what it does, not what it's called (e.g. `"floating damage"`, `"cooldown reset"`, `"wave spawn"`) |
| 3 | **Caller / precedent search** | `grep -rln "<related-system-name>" Assets/Demos/RPG/ Packages/unity-dots-library/` — find demos that use related library code; inspect their imports for adjacent library systems you missed |

If a pass returns hits → STOP, you found it. Skip remaining passes. Update the skill with the finding.

If all 3 return empty → safe to implement new (in the library, per Gate 1 library-first).

## Skill update is mandatory

The skill update is NOT optional follow-up — it is part of the task's definition of done. Specifically:

- **Found-in-library case**: edit the skill's reference doc (or create `references/<topic>-reuse.md`) with: library path, system/component name, when-to-use one-liner, demo precedent. Add the keyword to SKILL.md frontmatter `triggers:` so future activation catches it.
- **Implemented-new-in-library case**: edit the skill the same way, but the "library path" is now your new one. Include rationale "researched 3× pre-implementation, no prior art" so reviewers know the decision was due-diligence-backed.

Then **`/t1k:sync-back`** (background sub-agent) so the kit repo gets the skill update.

## Why

Real session evidence (2026-05-24 ChaosForge combat planning):

- Orchestrator manually re-discovered `DOTSCombat.Survivor.DamageNumberSystem` for floating damage numbers — that knowledge should have been in the `t1k-unity-dots-combat-rpg` skill all along.
- Orchestrator manually re-discovered `DOTSCombat.AutoBattler` round/phase systems for wave flow — same gap.
- Orchestrator manually re-discovered `BattleDemoSideView` "thin demo wrapper" architectural template — same gap.

Each re-discovery costs ~5-10 minutes and produces correction overhead. Cumulative across sessions and teammates: hours per cook. With this protocol + the skill-update mandate, the discovery happens ONCE and every future session inherits.

## Anti-patterns

- **"It's not in the skill so it doesn't exist"** — false. Library content drifts ahead of skill content. Always research.
- **"I searched once and it's not there"** — one grep with one keyword isn't enough. Do all 3 passes.
- **"I'll just write it in the demo for now and extract later"** — Gate 1 violation. Write in the library OR research more.
- **"I found it, let me just use it without updating the skill"** — leaves the gap for the next implementer. Update the skill in the same task.
- **"Skill update can be a follow-up PR"** — no. Skill update lands with the implementation, not later. Sync-back is the canonical propagation path.

## Narrow exceptions

- **Trivial usage** of an already-documented library system — no new doc needed, the skill already covers it.
- **Demo-specific glue** that's genuinely not reusable (e.g. a Canvas controller wiring 4 buttons specific to ChaosForge UI layout) — no library entry, no skill entry.
- **Editor-only scaffolding** that mirrors an existing demo's editor pattern — reference the existing demo in the implementation comment; no new skill entry required.

## How to apply

1. Before writing a new system / component / utility: invoke the relevant lib skill (`t1k:unity:dots-*`).
2. If it mentions what you need → use it.
3. If it doesn't → run the 3 research passes.
4. Update the skill regardless of outcome.
5. Spawn background `/t1k:sync-back` after local commit.

## Related

- `rules/manual-correction-implies-skill-gap-unity.md` — the meta-rule this implements for the library-discovery case.
- `rules/development-principles.md` § "SSOT — No Duplicates" + "Update Skills After Every Error" — the SSOT + skill-update mandates this leans on.
- `rules/prefer-local-over-global-edits.md` — skill edits go to `./.claude/skills/`, then sync-back.
- `rules/update-kits-before-sync-back.md` — `t1k modules update` + GitHub HEAD check pre-flight before any sync-back.
- `CLAUDE.md` § "Completion Gates → Gate 1 Library-First" — the parent constraint this protocol services.

## History

Established 2026-05-24 during DOTS-AI ChaosForge combat planning session. User directive:

> "If the lib skill don't mention about the thing that we need, we should research the whole lib for 3 time to find the one we need. If there is, update the skill. If there is not, implement new then update the lib skill."

The session had multiple cases of orchestrator-side rediscovery of library systems (DamageNumberSystem, AutoBattler module, BattleDemoSideView template) that should have been pre-encoded in the dots-combat-rpg skill. Each rediscovery added correction overhead. This rule closes the loop.
