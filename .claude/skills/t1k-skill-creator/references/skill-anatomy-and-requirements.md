---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Skill Anatomy & Requirements

## Directory Structure

```
$HOME/.claude/skills/
└── skill-name/
    ├── SKILL.md          (required, <300 lines)
    │   ├── YAML frontmatter (name, description required)
    │   └── Markdown instructions
    └── Bundled Resources (optional)
        ├── scripts/      Executable code (Python/Node.js)
        ├── references/   Docs loaded into context as needed
        ├── agents/       Eval agent templates (grader, comparator, analyzer)
        └── assets/       Files used in output (templates, etc.)
```

## Core Requirements

- **SKILL.md:** <300 lines. Concise quick-reference guide.
- **References:** <300 lines each. Split by logical boundaries.
- **Scripts:** No length limit. Must have tests. Must work cross-platform.
- **Description:** <200 chars. Specific triggers, not generic.
- **Consolidation:** Related topics combined (e.g., cloudflare+docker → devops)
- **No duplication:** Info lives in ONE place (SKILL.md OR references, not both)

## Kit-Shipped SKILL.md Tightness Rule

Every line of a kit-shipped `SKILL.md` is loaded into context for every consumer × every session. **Bloat is multiplicative across ~50 consumers**. Strict line budgets apply:

- **SKILL.md body lines per step / section:** ≤ 5 lines per step. If a step needs more, the overflow belongs in a `references/*.md` file.
- **Forbidden inline in SKILL.md:** verbatim bail-message quote blocks (>2 lines), multi-line `Bash` probes, real-world-miss postmortems (`Real-world miss (date, session ID): …`), historical anecdotes longer than one clause, multi-paragraph rationale.
- **Where each piece belongs:**
  - SKILL.md → decision points, named gates, one-line pointers, short policy statements.
  - `references/*.md` → procedures, verbatim message templates, command examples, history tables, why-rationale.
- **Issue/PR cross-references:** acceptable inline as bare numbers (`#259`) when ≤5 fit on one line. Detailed `<issue> — <one-line>` tables go in references.
- **Test before commit:** `wc -l SKILL.md` after edits. If the SKILL.md grew >20 lines for a single fix, the diff is almost certainly bloated — move detail to a reference and re-trim.

**Why this rule exists:** kit-shipped skill bodies cost every consumer's context budget every session. A 200-line bloat ships to ~50 consumers × every session per consumer — that's tens of thousands of context-tokens spent per release for content most users will never read. References load on-demand only.

**Real-world miss:** during the #259 fix, the initial SKILL.md edit added ~40 lines of verbatim bail message + orphan probe Bash + historical anecdotes inline in Pre-flight Step 1. User flagged: *"the skill is too specific with example and things now, that noises the context."* Fix: extracted to `references/fork-context-bail.md` (ships with the kit, accessible to consumers without GitHub-issue permissions). Final SKILL.md net diff: +5 lines.

## Hook-Author Deployment Caveat

When a skill ships a companion hook entry to the kit's `.claude/settings.json` template, the hook FILE reaches consumers via `t1k update` but the REGISTRATION entry reaches them only via `t1k init --sync`. The skill body MUST be self-sufficient as the primary enforcement; the hook is defense-in-depth that may not be active until the consumer re-syncs.

Full deployment matrix, release-note discipline, and the manual-edit anti-pattern: `docs/hook-deployment-caveat.md` (lesson from #259).

## SKILL.md Frontmatter

```yaml
---
name: t1k:kebab-case-name      # required — `t1k:` prefix for core, `{kit}:` for kit skills
description: <200 chars, trigger-optimized
effort: low | medium | high    # required
context: fork                   # required when effort: high (heavy multi-subagent skills share parent prefix)
keywords: [optional, list]
argument-hint: "[hint]"         # recommended
# DO NOT hand-author: version, origin, repository, module, protected — CI/CD-injected only
---
```

**`context: fork` rule:** required for `effort: high` skills, recommended for skills that fork 3+ subagents or process large inputs. Without it, every sibling subagent pays the full input price (cache fragmentation). Canonical examples: `t1k-doctor`, `t1k-cook`, `t1k-plan`, `t1k-debug`, `t1k-review`, `t1k-security`, `t1k-ship`, `t1k-graphify`, `t1k-xia`.

**Metadata quality** determines auto-activation. See `references/metadata-quality-criteria.md`.

## Scripts (`scripts/`)

- Deterministic code for repeated tasks
- **Prefer:** Python or Node.js (Windows-compatible)
- **Avoid:** Bash scripts
- **Required:** Tests that pass, `.env.example`, `requirements.txt`/`package.json`
- **Env hierarchy:** `process.env` > skill `.env` > shared `.env` > global `.env`
- Token-efficient: executed without loading into context

See `references/script-quality-criteria.md` for full criteria.

## References (`references/`)

- Documentation loaded as-needed into context
- Use cases: schemas, APIs, workflows, cheatsheets, domain knowledge
- **Best practice:** Split >300 lines into multiple files
- Include grep patterns in SKILL.md for discoverability
- Practical instructions, not educational documentation

## Assets (`assets/`)

- Files used in output, NOT loaded into context
- Use cases: templates, images, icons, boilerplate, fonts
- Separates output resources from documentation

## Progressive Disclosure

Three-level loading for context efficiency:
1. **Metadata** (~200 chars) — always in context
2. **SKILL.md body** (<300 lines) — when skill triggers
3. **Bundled resources** — as needed (scripts: unlimited, execute without loading)

## Writing Style

- **Imperative form:** "To accomplish X, do Y"
- **Third-person metadata:** "This skill should be used when..."
- **Concise:** Sacrifice grammar for brevity in references
- **Practical:** Teach *how* to do tasks, not *what* tools are
