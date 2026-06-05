---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Naming Convention — Universal `t1k-` Prefix

**MANDATORY** for every skill and agent shipped by any TheOneKit kit. No exemptions. Any kit-shipped skill or agent without the `t1k-` prefix is a **bug** and MUST be filed as an issue against the owning kit (see `## Violation handling` below).

## Rule

Every skill and agent shipped by a TheOneKit kit MUST start with `t1k`. This applies to filenames under `.claude/agents/*.md` and `.claude/skills/*/SKILL.md`, the `name:` field in frontmatter, and anything surfacing in Claude Code's UI or registry routing.

**Skills** carry two coexisting forms; **Agents** carry a single (dash) form.

### Skills — two-form rule

| Surface | Form | Separator | Example |
|---|---|---|---|
| Filesystem directory | dash form | `-` | `t1k-designer-base-balance-tools/` |
| SKILL.md frontmatter `name:` | colon form | `:` | `t1k:designer:base:balance-tools` |

Filesystem uses dash (`:` is filesystem-hostile); frontmatter uses colon (Claude Code slash UI). After `-`↔`:` conversion the two strings are byte-identical.

### Agents — single-form rule

| Surface | Form | Example |
|---|---|---|
| File basename (`*.md`) | dash form | `t1k-fullstack-developer.md` |
| Agent frontmatter `name:` | dash form | `t1k-fullstack-developer` |

`name:` and basename MUST be byte-identical. No colon form — agents are not slash-commands.

### Slug structure

| Tier | Dash form | Colon form |
|---|---|---|
| Core | `t1k-{slug}` | `t1k:{slug}` |
| Kit-wide | `t1k-{kit}-{slug}` | `t1k:{kit}:{slug}` |
| Module-scoped | `t1k-{kit}-{module}-{slug}` | `t1k:{kit}:{module}:{slug}` |

`{kit}` = repo slug minus `theonekit-`. `{module}` = directory under `.claude/modules/`. Slug MUST NOT redundantly start with `{kit-short}-` or any `{module}-segment-token` (algorithm v2 strips them).

## Enforcement (strict by default)

- `validate-skill-prefix.cjs`, `validate-agent-prefix.cjs`, `validate-new-name-conformance.cjs` — release-action gates, strict since 2026-05-08.
- SSOT for the algorithm: `theonekit-release-action/scripts/lib-prefix.cjs`.

## Violation handling

A non-prefixed kit-shipped skill/agent is a **bug**. File it via `/t1k:issue` — title `[naming-convention] <name>: missing t1k- prefix` — and do NOT silently rename in a consumer project (fix must land in the kit source).

**Consumer-side zombies:** old non-prefixed copies left by `t1k modules update`. Move them OUT of the auto-scanned folder or delete after verification.

⛔ **Never quarantine inside Claude Code's auto-scanned folders** (`.claude/agents/`, `.claude/skills/`, `.claude/rules/`, `.claude/hooks/`, `.claude/commands/`, or project-local equivalents). Even dot-prefixed subdirectories are still walked — files inside them keep appearing as live registrations. The doctor check `check-stale-backup-folders.cjs` (#50) detects this. Quarantine destinations must be OUTSIDE the auto-scanned folder or deleted outright.

## Full details

Authoring procedure + full rule + history: `skills/t1k-skill-creator/references/architecture-rules.md` § 0 (skills) and `skills/t1k-agent-creator/references/architecture-rules.md` § 0 (agents).
