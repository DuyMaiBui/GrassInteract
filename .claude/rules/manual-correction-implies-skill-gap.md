---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# Manual Correction Implies a Skill Gap — Fix the Skill, Not the Brief

## Rule

When you (orchestrator, team-lead, or any agent) catch yourself **injecting knowledge into a teammate brief, spawn prompt, or correction message** that should already live in a skill, agent definition, or rule — STOP and **fix the underlying artifact** before the next teammate spawns. Patching the brief alone perpetuates the gap: every future spawn re-discovers the same blind spot, every future teammate makes the same mistake, every future session pays the same correction tax.

## Triggers (any of these = the rule fires)

- You write the same warning into 2+ teammate briefs in one session ("the API is `X.Y.Z`", "this enum lives in sub-namespace `Foo.Bar`", "watch out for `Z` because…").
- You re-explain a constraint to a teammate that you've already explained in a prior turn ("sub-agent fan-out is unavailable at fork depth 2", "git index is shared — never `git add .`").
- You correct a teammate's output ("you forgot `[BurstCompile]` on this system", "this should use `DOTSCombat.Skills.SkillSlot`, not `DOTSCombat.SkillSlot`") and the correction is a recurring pattern, not a one-off.
- A teammate emits a `[t1k:skill-bug …]` or `[t1k:lesson …]` marker noting that a skill body was wrong or incomplete.
- You read or write a checkpoint memory section that contains advice future sessions would benefit from (and the advice isn't already in the canonical skill).

## Required action (mandatory)

The moment any trigger fires:

1. **Identify the canonical artifact** that should have carried this knowledge:
   - DOTS API drift / namespace knowledge → `t1k-unity-dots-*` skill (use the routing table in `.claude/rules/skill-domain-routing-unity.md`)
   - Workflow / coordination patterns → `t1k-team`, `t1k-cook`, `t1k-plan` skill
   - Agent behavior / spawn templates → agent `.md` file under `.claude/agents/`
   - Cross-cutting discipline → a rule file under `.claude/rules/`
2. **Edit the canonical artifact** (project-local per `rules/prefer-local-over-global-edits.md`):
   - Add a gotcha to the skill's `Gotchas` section, OR
   - Add a fact to the skill's reference file, OR
   - Add a guard to the agent's brief template, OR
   - Add a new rule file
3. **Re-spawn or re-message the affected teammates** referencing the updated skill so the gap closes immediately for the current session.
4. **`/t1k:sync-back`** (background sub-agent per orchestration rules) to propagate the fix upstream to the owning kit repo.
5. **`/t1k:issue`** (background sub-agent) ONLY if (a) the gap is non-trivial and warrants discussion, OR (b) the fix needs upstream coordination (e.g., a t1k-team skill body bug that requires architectural review). Routine "add this fact to the skill" edits don't need an issue — the sync-back PR is the discussion.

## Anti-patterns

- **Patching only the brief** ("I'll just tell the next teammate") — guarantees the same correction next session.
- **Adding a checkpoint memory note in place of a skill edit** — memory is session-scoped; skills are repo-scoped. Use both: memory for the active session decision, skill for the durable knowledge.
- **Deferring the skill edit "until after the cook"** — by then the context is lost and the edit doesn't happen. Edit during the cook.
- **Filing an issue without also editing the skill** — issues are slow loops; the skill edit closes the loop now. Do both: edit + issue when warranted, edit alone when routine.
- **Editing only the global `$HOME/.claude/skills/` copy** — per `rules/prefer-local-over-global-edits.md`, project-local is canonical and survives `t1k self-update`. Edit local; sync-back propagates to the kit.

## Why

Real session evidence (2026-05-23 ChaosForge cook):

- Team-lead injected `"sub-agent fan-out unavailable at fork depth 2"` into phase4's brief AFTER phase3 hit the blocker — but the t1k-team SKILL.md still promises the fan-out. Next team in another project will hit the same blocker.
- phase3-combat fixed `using DOTSCombat;` → `using DOTSCombat.Skills;` for `SkillSlot` — but the DOTS combat skill never documented that `Skills` is its own sub-namespace. Next teammate writing combat code will guess wrong again.
- Team-lead manually appended "150K commit checkpoint + git discipline + tests per Gate 4" to every spawn brief — but the t1k-team skill's spawn-brief generator doesn't include any of those. The patches stayed in checkpoint memory, never made it back to the skill.
- Auto-pipeline (`lesson-collector.cjs`) is blind to teammate-emitted markers (Issue #272) — so the `[t1k:skill-bug …]` markers teammates emitted this session never auto-filed. The manual correction path is currently the ONLY working path, which makes this rule load-bearing.

Without this rule, every cook session pays the same recurring correction tax. With this rule, each correction is a one-time investment that future sessions inherit for free.

## How to apply

- **Mid-cook (between waves):** keep a running list of "things I had to manually inject" — at the end of each wave, scan the list and edit the canonical artifacts before spawning the next wave.
- **End-of-cook (always):** at session wrap-up, audit the checkpoint memory and your recent spawn briefs for any guidance that should be skill content. Edit those skills + sync-back as part of the close-out, not as an optional follow-up.
- **In-the-moment:** if you catch yourself typing the same warning twice in one turn, STOP — open the skill, add the gotcha, then continue.

## Narrow exceptions

- **Single-use, session-specific decision** (e.g., "use the Modern Era seed for this demo's mid-game vertical slice") — that's plan content, not skill content. Don't pollute skills with session-only specifics.
- **Plan-derived facts that change per project** (e.g., "this demo uses ForgeLevel=13") — those go in the plan file, not the skill.
- **The skill IS the active edit target** (e.g., you're updating dots-rpg skill anyway as part of this turn's work) — don't recurse; finish the in-flight edit.

## Related

- `rules/development-principles.md` § "Update Skills After Every Error" — the generic ancestor of this rule; this one is the strict version that catches manual-correction patterns the generic rule misses.
- `rules/prefer-local-over-global-edits.md` — edit project-local skills, not `$HOME/.claude/skills/`.
- `rules/update-kits-before-sync-back.md` — pre-flight before any sync-back PR.
- `rules/workflow-failure-auto-issue.md` — auto-marker pipeline for catching skill bugs (currently broken for teammate-emitted markers per Issue #272).
- `rules/orchestration-rules.md` — `/t1k:sync-back` and `/t1k:issue` are background-only.
- `rules/telemetry.md` — auto-lesson pipeline documentation.

## History

Established 2026-05-23 during DOTS-AI ChaosForge cook (session b3e1e6e8). User directive:

> "I see you're editing a lot — that means the DOTS skill hasn't been updated properly, or it wasn't loaded. Please check, then file issues or fix them, and sync-back for me. If that's it, please note in a rule or skill that if we face something like this, we should update the skills so we don't face the same issue again."

The session had 4+ instances of team-lead manually patching teammate briefs with knowledge that should have lived in skills (depth-2 sub-agent constraint, namespace gaps, 150K checkpoint discipline, git index race). None of those patches made it back to the skills until this rule was written.
