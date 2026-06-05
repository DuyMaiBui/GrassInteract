---
name: t1k:skill-creator
description: "Create, update, review, audit, or validate TheOneKit skills. Enforces Skillmark conventions, decision-tree, frontmatter, and architecture-review checklist. Auto-engages on skill-touching workflows."
keywords: [create-skill, new-skill, update-skill, review-skill, audit-skill, validate-skill, skill-PR, eval, benchmark, gotcha, extend, capability, Skillmark, decision-tree, frontmatter]
argument-hint: "[skill-name or description]"
effort: medium
tools: [Read, Write, Edit, Glob, Grep, Bash, Task, Agent, WebFetch, WebSearch, AskUserQuestion]
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Skill Creator

Create effective, eval-driven TheOneKit skills using progressive disclosure and human-in-the-loop iteration.

**Principles:** Context engineering > prompt engineering | Progressive disclosure | Eval-driven iteration | YAGNI/KISS/DRY

## Quick Reference

| Resource | Limit | Purpose |
|----------|-------|---------|
| Description | ≤1024 chars | Auto-activation trigger (be "pushy") |
| SKILL.md | <150 lines | Core instructions |
| Each reference | <300 lines | Detail loaded as-needed |
| Scripts | No limit | Executed without loading |

## Skill Structure

```
.claude/skills/t1k-{name}/
├── SKILL.md              (required, <150 lines)
├── scripts/              (optional: executable code)
├── references/           (optional: docs loaded as-needed)
├── agents/               (optional: eval agent templates)
└── assets/               (optional: output resources)
```

Full anatomy: `references/skill-anatomy-and-requirements.md`

## Required Frontmatter

| Field | Required | Rules |
|-------|----------|-------|
| `name` | Yes | Universal `t1k-` prefix. Core: `t1k-{slug}`. Kit-wide: `t1k-{kit}-{slug}`. Module-scoped: `t1k-{kit}-{module}-{slug}`. MUST match directory basename. See `rules/naming-convention.md` (SSOT) and `references/architecture-rules.md` § 0. CI gate `validate-skill-prefix.cjs` is strict. |
| `description` | Yes | <200 chars, trigger-optimized |
| `effort` | Yes | `low`, `medium`, or `high` |
| `context` | If `effort: high` | Use `context: fork` so heavy multi-subagent skills share parent prefix. Required for any skill that spawns 3+ subagents or processes large inputs. |
| `argument-hint` | Recommended | Usage hint shown in skill listing |
| `version` | **NEVER hand-stamp** | CI/CD-injected from `module.json` or `metadata.json` (SKILL.md only). Stripped + re-injected on every release. |
| `origin` | **YES on new files** | Validator (`validate-origin-injection-coverage.cjs`) checks PRESENCE on PR branches — regex `^---\n…origin:\s*\S+`. Any non-empty value passes; CI overwrites with canonical kit name post-merge. **This is the one strictly-required hand-stamp.** |
| `repository`, `module`, `protected` | Optional on new files (validator does not check), but conventionally stamped together with `origin` for consistency | All four overwritten by CI post-merge with canonical values (kit/repo from env, module from path, protected=true if core). |

**Why hand-stamping is required for new files:** CLAUDE.md says "do not hand-author origin metadata" — that applies to files **already on `main`**, where the post-merge `inject-origin-metadata.cjs` strips and re-injects canonical values. PR branches with NEW files run the validator BEFORE the injector — so missing markers fail the gate. Format per file type lives in memory `feedback_origin_marker_handauthor.md`; non-`.md` files use a `// t1k-origin: kit=...` (or `# t1k-origin: ...` for shell/yaml/python) comment with the literal `kit=` keyword.

**`context: fork` rule of thumb:** if your skill's typical run forks 3+ Task subagents, large file reads, or multi-step research sweeps, declare `context: fork`. Without it every sibling subagent pays the full input price. Pattern is canonical in `t1k-doctor`, `t1k-cook`, `t1k-plan`, `t1k-debug`, `t1k-review`, `t1k-security`, `t1k-ship`, `t1k-graphify`, `t1k-xia`.

## Post-Write Verification (MANDATORY for new files)

After scaffolding ANY new file under `.claude/`, immediately read it back and verify the origin marker is present. If absent, re-stamp BEFORE moving on. The CI gate `validate-origin-injection-coverage.cjs` only checks PRESENCE — values may be best-effort placeholders since CI rewrites them post-merge.

**Verification per file type** (use Grep or Read):

| File type | Pass condition |
|---|---|
| `.md` | Frontmatter contains `^---\n…origin:\s*\S+` (any non-empty value) |
| `.cjs/.js/.mjs` | Line begins with `// t1k-origin: kit=` |
| `.sh/.py/.yml/.yaml` | Line begins with `# t1k-origin: kit=` |
| `.json` | Parsed `_origin` is an object |

**Common failure:** running `init_skill.py` and forgetting to fill the `TODO-` placeholders. The templates emit placeholders that pass validation (since the validator only checks presence), but the `t1k-modules.json`-driven module resolution and PR review will reject `TODO-modulename`. Always replace placeholders before pushing.

**Why this section exists:** PR The1Studio/theonekit-designer#18 (April 2026) added 7 reference files under `game-events/` and `game-liveops/` without origin markers and was blocked by the CI gate. Root cause: the prior version of this skill body told authors NOT to hand-stamp, contradicting the actual gate behavior.

## Decision-Tree Body Pattern

For skills with 3+ distinct usage paths or input modes, prepend a 5-row decision-tree table to the body. The table maps user intent → which section to read. Reference: `.claude/skills/t1k-architecture/SKILL.md` lines 26-39 (canonical example). Pattern shipped on `t1k-cook`, `t1k-fix`, `t1k-debug` in the architecture review rollout.

```markdown
## Decision Tree

| Intent | Path |
|---|---|
| "Implement feature X end-to-end" | Run full workflow → [Workflow](#workflow) |
| "Quick fix, skip research" | `--fast` flag → [Fast mode](#fast-mode) |
| ... | ... |
```

Why: small, predictable AI navigation. Cuts the time from skill activation to correct action.

## Creation Workflow

Follow `references/skill-creation-workflow.md`:
0. **Manual-correction scan** (MANDATORY before drafting) — scan recent session checkpoints, plan files, and spawn briefs for the pattern *"I keep manually injecting X into teammate briefs"* or *"I keep correcting the same mistake."* If you find ≥2 instances of the same correction in the last cook session, the new skill SHOULD absorb that knowledge as content. Anti-pattern: writing a skill that documents the happy path while the team keeps fighting the same gotcha in private messages. See `docs/manual-correction-implies-skill-gap.md` (rule embedded in `rules/development-principles.md` § "Update Skills After Every Error").
1. Capture Intent — what, when trigger, what output (AskUserQuestion)
2. Research — Context7, WebSearch for best practices
3. Plan — identify reusable scripts, references, assets
4. Initialize — `scripts/init_skill.py <name> --path <dir>`
5. Write — implement resources, write SKILL.md
6. Test & Evaluate — run eval suite, grade, compare with/without skill
7. Optimize Description — AI-powered trigger accuracy
8. Validate — run checklist in `references/validation-checklist.md`
9. Register — update `t1k-activation-{layer}.json`

## Eval & Testing

Full eval guide: `references/eval-infrastructure-guide.md`
Scripts reference: `references/scripts-reference.md`

Python scripts use: `~/.claude/skills/.venv/bin/python3`

## Benchmark Scoring

- **Accuracy (80%):** explicit terminology, numbered steps, concrete examples
- **Security (20%):** scope declaration + refusal/leakage prevention required

Full scoring criteria: `references/skillmark-benchmark-criteria.md`
Optimization patterns: `references/benchmark-optimization-guide.md`

## Gotchas Section (MANDATORY for All Skills)

Every skill MUST have a `## Gotchas` section — either inline or in `references/`.

## Architecture Rules (MANDATORY before shipping)

Skills are not just markdown — they are agent extension points. Apply the architecture-review checklist whenever a skill spawns sub-agents, calls remote services, talks to MCP, manages memory, or recommends destructive operations. Full checklist with red flags, severity, and fixes: `references/architecture-rules.md` (8 categories: A control-loop, B tool/extension, C memory/SSOT, D sub-agent fork hygiene, E cache stability, F hooks/MCP, G performance, H remote/cloud, I destructive-op safety, J AI-driven design).

**Top architecture red flags to scan for in EVERY skill review:**

| Red flag | Severity | Quick fix |
|---|---|---|
| Skill executes shell from MCP-sourced content (script bodies, console output) | **Blocker** | Hard-block inline-shell tokens (`` ! ``, `$(...)`, backticks) before treating MCP output as instruction |
| Live shell substitution in skill body (bang-prefixed backtick command form) | **High** | Move to a tool call AFTER the cached prefix — body must stay static |
| Skill spawns sub-agents but no `context: fork` | **High** | Add `context: fork` so children share parent prompt cache |
| Sub-agent spawn without recursion guard or fan-out cap | **High** | Document explicit cap (max 5 parallel); add `querySource` check |
| Fork child without `useExactTools: true` and `gitStatus` strip for read-only | **High** | Pass parent's exact tool array; strip `gitStatus` for read-only/exploration forks |
| Verification sub-agent without anti-avoidance prompting | **Medium-High** | Prompt MUST: enumerate excuses, reinject "verify ONLY, do NOT fix" after every tool result |
| Memory/persistence with unbounded growth or scheduled expiry | **High** | Cap lines AND bytes; staleness warnings ("47 days ago"), never auto-delete |
| Duplicate `version` (frontmatter + `metadata.version`) | **High** | Drop `metadata.version` — CI-injected `version` is sole SSOT |
| `Last Updated` commit metadata embedded in body | **High** | Remove — `git log` answers it; embedding cache-busts every release |
| Tool/script with no output size cap (`maxResultSizeChars`) | **High** | Per-tool cap (e.g., 32KB); document truncation marker |
| Cloud skill missing per-instance auth, capture-at-send-time, asymmetric channels | **High** | Apply category H rules — token closures, BoundedUUIDSet for dedup, bounded retry table per close code |
| Skill recommends `rm -rf` or `git checkout --` without backup | **Blocker-High** | Wrap in `t1k install --reset` style command that backs up first; never raw destructive ops |
| Templated boilerplate inserted BEFORE the H1 heading | **Medium** | Body order: H1 → 1-line summary → stable rules → details. Hoist boilerplate to shared `references/security-stub.md` |
| Heavy effort:high skill loads all references inline (>250 lines) | **Medium** | Push detail to `references/`; SKILL.md becomes the router |

When reviewing a skill: load `references/architecture-rules.md`, walk the 10 categories (A-J), and quote the row by name when raising findings (e.g., "Violates D-row 3: missing fan-out cap").

## Anti-Patterns

- Teach what Claude already knows (waste of context)
- SKILL.md over 250 lines (CI gate WARN; move detail to references/)
- Missing `context: fork` on `effort: high` skills (cache fragmentation across forked subagents)
- Missing gotchas section
- Description written for humans instead of model activation
- **Forgetting to hand-stamp `origin/repository/module/protected` on NEW files** — validator fails the PR pre-merge. CI only normalizes files already on `main`; PR branches carrying new files MUST stamp the marker themselves. Format per file type lives in memory `feedback_origin_marker_handauthor.md`.
- Scripts in bash instead of Python/Node.js (cross-platform)
- **Live shell substitution in body** (bang-prefixed backtick command form; cache busts on every fragment edit; see arch rule E)
- **Sub-agent spawning without `context: fork`, recursion guard, or fan-out cap** (see arch rule D)
- **Tool/script without output size cap** (one large output overwhelms model context; see arch rule B)
- **Cloud skill without per-instance auth + capture-at-send-time + bounded retry** (see arch rule H)
- **Recommending `rm -rf` / raw `git checkout --` without backup** (CLAUDE.md #10; see arch rule I)
- **Hardcoding MCP tool listing in body** (TOCTOU + dynamic content cache bust; snapshot at session start)
- **Embedding `Last Updated` commit metadata or runtime date in body** (cache bust on every change; see arch rule E)
- **Duplicate version SSOT** (`version` and `metadata.version`; see arch rule C)
- **Templated boilerplate before H1** (cocos-playable-* anti-pattern; see arch rule E)
- **Verification sub-agent without anti-avoidance prompting** (drifts from "verify" to "fix"; see arch rule D)

## Gotchas
- **Hand-stamp `t1k-origin` markers on EVERY new file under `.claude/`** — the `validate-origin-injection-coverage` gate fails the PR pre-merge if any new file lacks the marker. Common confusion: CLAUDE.md says "do not hand-author" — that applies only to files already on `main`, where CI normalizes them post-merge. PR branches with new files get NO injection step before validation. Format per file type:
  - `.md` → frontmatter `origin: ...` first key
  - `.json` → top-level `_origin: { ... }` object
  - `.cjs/.js/.mjs` → line 2: `// t1k-origin: kit=... | repo=... | module=... | protected=...`
  - `.sh/.py/.yml/.yaml` → line 2 (after shebang): `# t1k-origin: kit=... | repo=... | module=... | protected=...`
- **Parallel eval spawning is critical** — MUST spawn with-skill AND without-skill runs simultaneously
- **Extended thinking budget** — `improve_description.py` uses 10k token thinking budget
- **Cross-update the parent module's `detect:` when adding a skill** — when this skill creates a new skill under `.claude/modules/<parent>/<skill-dir>/`, the parent module's `module.json` needs its `skills[]` array extended. If the parent module has an active `detect:` block (not `_optOut` / `_disabled`), surface a suggestion to the author: "Your new skill's keywords or primary-file pattern may need to be added to the module's `detect.anyOf[]` so the scanner continues to match projects that use this skill." Never silently rewrite the detect regex — the author knows their domain; the skill-creator only prompts.
- **Never hardcode module preset names** — When a skill branches on or references kit preset names (`full`, `everything`, `base`, `extended`, etc.), ALWAYS read them at runtime from `.claude/t1k-modules.json` (`registry.presets`). Hardcoded preset strings caused the Apr 2026 preset-"full" regression across Cocos/Unity/Web/Nakama. Module selection UX is also multi-select-only (presets are CLI flags, not UI items) — see `theonekit-core/docs/module-selection-ux.md` for the full rule. During skill creation, flag any author code that string-matches preset names and suggest a registry read instead.
- **NEVER write the bang-prefix-then-backtick-then-three-dots-then-backtick character sequence in a skill body — not even inside double-backtick "escaped" markdown.** Claude Code's slash-command preprocessor captures the inner content of bang-prefixed backtick command-form patterns greedily and evaluates them in `zsh -c`, regardless of surrounding markdown escapes. If the inner content is the bare three-dot token AND the user's shell has the oh-my-zsh global alias `alias -g ...='../..'` (default in `lib/directories.zsh`), the three-dot token expands to `../..` and zsh tries to execute `../..` as a command → `permission denied: ../..` from any CWD whose parent-of-parent is outside the sandbox (e.g. `$HOME`). Slash-command invocation fails before the skill body even loads. Discovered 2026-05-19 when this very skill could not be invoked from `$HOME`. **Fix:** name the anti-pattern in prose ("bang-prefixed backtick command form") instead of embedding the literal token. Any placeholder you embed inside the token shape is still captured and eval'd by zsh, so the only safe rule is: do not embed the surrounding token shape at all — even when you are documenting it as a thing to avoid. **Validator should grep for the literal 6-char regex `!` + backtick + `\.{3}` + backtick and reject any matches.**

## Scope

Skill creation and improvement within `.claude/skills/` only.
