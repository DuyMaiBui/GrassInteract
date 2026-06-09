---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Manual Correction Implies a Skill Gap — Unity / DOTS routing (engine-specific additions)

Extends core's universal principle — Unity-specific additions only. Core `rules/development-principles.md` § "Update Skills After Every Error" supplies the universal rule: every manual correction is a skill gap; fix the canonical artifact (skill / agent / rule), re-spawn the affected teammates, then `/t1k:sync-back`. This file supplies the **Unity/DOTS canonical-artifact routing** plus the DOTS-specific triggers that the generic rule does not enumerate.

## Unity-specific triggers (in addition to core's)

- You correct a teammate's DOTS code and the correction is a recurring pattern, not a one-off — e.g. "you forgot `[BurstCompile]` on this system", or "this should use `DOTSCombat.Skills.SkillSlot`, not `DOTSCombat.SkillSlot`" (a sub-namespace gap).
- You write the same DOTS API/namespace warning into 2+ teammate briefs in one session ("this enum lives in sub-namespace `Foo.Bar`", "the API is `X.Y.Z`").
- A teammate emits `[t1k:skill-bug …]` / `[t1k:lesson …]` noting a `t1k-unity-dots-*` skill body was wrong or incomplete.

## Unity canonical-artifact routing (the load-bearing delta)

When a trigger fires, route the fix to the right Unity artifact:

| Manual correction was about… | Edit this artifact |
|---|---|
| DOTS API drift / namespace / sub-namespace knowledge | the relevant `t1k-unity-dots-*` skill (use `.claude/rules/skill-domain-routing-unity.md` to pick) |
| Burst / job / ECS authoring gotcha | `t1k-unity-dots-core-jobs-burst` or the owning `t1k-unity-dots-*` skill |
| Unity workflow / coordination (compile-gate, domain-reload) | `rules/ai-velocity-batch-compile-unity.md` or the relevant skill |
| Library naming / extraction call | `rules/library-quality-mandate-unity.md` § "Naming charter" |
| Agent behavior / spawn template | the agent `.md` under `.claude/agents/` |

Edit project-local (`rules/prefer-local-over-global-edits.md`), then `/t1k:sync-back` (background) to propagate to this kit.

## Unity narrow exceptions

- **Plan-derived facts that change per project** (e.g. "this demo uses `ForgeLevel=13`") go in the plan file, not the skill.
- **The DOTS skill IS the active edit target** (you're updating `t1k-unity-dots-rpg` anyway this turn) — finish the in-flight edit; don't recurse.

## Why (Unity evidence)

2026-05-23 ChaosForge DOTS-AI cook: phase3-combat fixed `using DOTSCombat;` → `using DOTSCombat.Skills;` for `SkillSlot`, but the DOTS combat skill never documented that `Skills` is its own sub-namespace — so the next teammate writing combat code guesses wrong again. Team-lead also injected "sub-agent fan-out unavailable at fork depth 2" into a later brief AFTER an earlier phase hit the blocker, instead of fixing the `t1k-team` skill. Without routing these corrections back to the `t1k-unity-dots-*` skills, every cook session pays the same DOTS-namespace correction tax.

## Related

- `rules/development-principles.md` § "Update Skills After Every Error" (core) — the universal ancestor this extends.
- `rules/skill-domain-routing-unity.md` — picks the right `t1k-unity-dots-*` skill for a DOTS correction.
- `rules/prefer-local-over-global-edits.md` — edit project-local skills, not `$HOME/.claude/skills/`.
- `rules/update-kits-before-sync-back.md` — pre-flight before any sync-back PR.
- `rules/library-feature-discovery-protocol.md` — the meta-rule this implements for the library-discovery case.
