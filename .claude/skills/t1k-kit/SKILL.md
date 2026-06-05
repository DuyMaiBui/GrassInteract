---
name: t1k:kit
description: "Maintain TheOneKit repos as a kit maintainer. Use for releasing kits, scaffolding new kits, cross-kit validation, schema migration, and E2E test runs."
keywords: [maintainer, release, scaffold, validate, audit, migrate, kit]
argument-hint: "<subcommand> [options]"
effort: medium
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit Kit — Maintainer Toolkit

Unified kit maintenance skill. All subcommands are for **kit maintainers only**, not end-users.

## Subcommands

| Subcommand | Usage | Purpose |
|---|---|---|
| `validate` | `/t1k:kit validate [--cross-kit]` | Schema, activation paths, keyword conflicts, preset refs, **universal `t1k-` prefix** (skills + agents — see `rules/naming-convention.md`) |
| `release` | `/t1k:kit release [--skip-validate]` | Guided release: validate → CI gate → GitHub release → Discord |
| `sync` | `/t1k:kit sync [--pull] [--status-only]` | Pull all kits, check version compat, release-action alignment |
| `scaffold` | `/t1k:kit scaffold <name> [--org]` | Create new kit repo from template with full boilerplate |
| `audit` | `/t1k:kit audit [--all]` | Health audit: file counts, modules, keywords, CI, freshness |
| `migrate` | `/t1k:kit migrate [--dry-run]` | Schema migration (e.g., registryVersion 1→2) |
| `test` | `/t1k:kit test [--all] [--kit <name>]` | Comprehensive 15-phase E2E testing: install, modules, CLI, release infra |

## Quick Reference

```bash
/t1k:kit validate              # Validate current kit repo
/t1k:kit validate --cross-kit  # Validate across all installed kits
/t1k:kit release               # Guided release workflow
/t1k:kit sync --pull           # Pull and check all kits
/t1k:kit scaffold theonekit-x  # Scaffold new kit
/t1k:kit audit --all           # Audit all kit repos
/t1k:kit migrate --dry-run     # Preview schema migration
/t1k:kit test --all            # E2E test all kits
/t1k:kit test --kit unity      # E2E test specific kit
```

## Subcommand Details

Read the reference file for full details on each subcommand:
- `references/validate.md` — per-kit and cross-kit validation checks
- `references/release.md` — release workflow steps and gates
- `references/sync.md` — cross-kit synchronization protocol
- `references/scaffold.md` — new kit scaffolding template
- `references/audit.md` — health audit checks and scoring
- `references/migrate.md` — registry schema migration steps (v1→v2)
- `references/test.md` — 12-phase E2E test plan
- `references/cli-commands.md` — `t1k new`, `t1k modules create`, `t1k migrate`, `t1k migrate metadata`, `t1k rollback` (cli@v4.14.0; flags + gotchas grounded in source)
- `references/global-hook-authoring.md` — **MANDATORY** for any kit shipping hooks at global scope. Required `node -e require(...)` wrapper pattern that survives the CLI's `$HOME` → `$CLAUDE_PROJECT_DIR` rewrite bug (model-router#49). CI gate `validate-global-hook-home-resilience.cjs` enforces it.

## Gotchas
- **Global-scope hooks MUST use the `node -e require(...)` wrapper, NOT bare `$HOME/.claude/hooks/X.cjs`.** When a kit ships hooks installed into global `~/.claude/settings.json` (not project `.claude/settings.json`), the T1K CLI's `fixSingleCommandPath(cmd, isGlobal=false)` defaults `isGlobal` to false and rewrites `$HOME` → `$CLAUDE_PROJECT_DIR` — silently breaking every hook with `MODULE_NOT_FOUND` (loader:1424). Defense-in-depth wrapper: `node -e "process.env.HOME = require('os').userInfo().homedir; require(require('path').join(process.env.HOME, '.claude', 'hooks', 'your-hook.cjs'))"`. Reads home from `/etc/passwd` (POSIX) — unaffected by HOME env corruption. Originating incident: model-router#49 (~363 banner errors per 90-min session before fix). Full pattern + validation: `references/global-hook-authoring.md`. CI gate `validate-global-hook-home-resilience.cjs` enforces.
- **Origin metadata: hand-stamp on NEW files, hands-off on existing files.** The release-action `inject-origin-metadata.cjs` runs post-merge — it strips and re-injects canonical `origin / repository / module / protected` (and `version` for SKILL.md) from env vars + module-resolution. So files already on `main` MUST NOT be hand-edited for these fields. BUT new files in PR branches MUST hand-stamp at least `origin:` (validator `validate-origin-injection-coverage.cjs` checks marker presence pre-merge — values are best-effort, CI overwrites them). Format per file type lives in memory `feedback_origin_marker_handauthor.md` and the `t1k:skill-creator` "Required Frontmatter" section.
- **Modular kits use `release-modules.cjs`** (custom release script), NOT semantic-release directly. Flat kits (e.g., theonekit-rn) still use semantic-release. `release-modules.cjs` produces per-module ZIPs + `manifest.json` index.
- **`t1k-modules.json` is GENERATED** for the `modules` section — do not hand-author it. Source of truth is each `module.json`. Only `presets` in `t1k-modules.json` are hand-authored.
- **`.releaserc.json` MUST include `package.json` and `.claude/metadata.json` in `@semantic-release/git` assets** — without this, `package.json` stays at `0.0.0` forever. Every kit repo needs: `"assets": ["package.json", "CHANGELOG.md", ".claude/metadata.json"]`
- **Do not hardcode `version` or `buildDate` in source `metadata.json`** — CI generates these from `package.json`. Source should only contain manually-maintained fields (`name`, `repository`, `deletions`).
- **Canonical "full" preset — required in EVERY kit** — every modular kit MUST declare `"full": "*"` in `t1k-modules.json` `presets`. The interactive selector renders a synthetic "Full — all N modules" choice from any `"*"`-valued preset (it keys off the *value*, not the name), so kits with no presets or a differently-named `"*"` preset look fine in the UI — but scripted invocations like `t1k new --preset full` fail when the preset is named anything other than literally `"full"`. Apr 2026 audit caught: web (`everything: "*"`), marketing + nakama (no presets section at all), designer (array-shorthand drift). **Always name the `"*"` preset literally `"full"`** even when the kit has only one required module (future-proofs scripted use). Other named presets (`rpg`, `fullstack`, etc.) MUST use v3 object form `{ "modules": [...], "description"?: "...", "crossKitModules"?: [...] }` — array shorthand `["mod-a", "mod-b"]` resolves at runtime but breaks cross-kit format consistency.
- **`module new` and `kit new` are NOT real CLI subcommands.** The shipped commands are `t1k new` (project bootstrap) and `t1k modules create` (module scaffold). Older audits and design docs use the legacy names — when documenting maintainer flows, reference the real CLI surface (`references/cli-commands.md`).
- **`t1k rollback` requires a snapshot that nothing currently creates.** The H7 rollback read-side shipped in v4.14.0, but no production install/update path calls `createSnapshot()` yet. Until the write-side lands, `t1k rollback --kit X --to-snapshot pre-Y` will reliably fail with `snapshot '...' does not exist`. Direct users to `t1k install --reset` for now if they're truly stuck — and NEVER recommend `rm -rf ~/.claude/`. <!-- gate:allow-rm-claude (rule statement) -->
- **`t1k rollback --yes` is a documented no-op** as of v4.14.0 — declared in the CLI registry, never read by the handler. Do not promise confirmation-bypass behavior in maintainer guidance.
- **`--to-snapshot` MUST literally start with `pre-`.** Bare-version arguments (`--to-snapshot 2.4.1`) are rejected. Always render the `pre-` prefix in examples.
- **Scaffold new modules with a `detect:` stub** — when `scaffold` or `/t1k:modules create` writes a new `module.json` for a non-base module, emit a `_disabled: true` detect stub so P6d CI and doctor #41 stay green while the author tightens patterns:
  ```json
  "detect": {
    "_disabled": true,
    "_note": "AUTO-GENERATED STUB. Tighten files+anyOf then remove _disabled to activate.",
    "files": ["**/*.md"],
    "anyOf": [{ "pattern": "TightenThisPattern_<moduleName>", "minHits": 2 }]
  }
  ```
  The stub is schema-valid (passes P6d), scanner silently skips modules with `_disabled: true`, and doctor #41 surfaces the stub as "needs activation" so the author can't forget.
- **Universal `t1k-` naming prefix (MANDATORY)** — every skill directory + agent file + frontmatter `name:` MUST start with `t1k-`. Tiers: core `t1k-{slug}`, kit-wide `t1k-{kit}-{slug}`, module-scoped `t1k-{kit}-{module}-{slug}`. Slug MUST NOT redundantly start with the kit-short or any module-segment-token (algorithm v2 strips them). Three release-action gates are strict-by-default: `validate-skill-prefix.cjs`, `validate-agent-prefix.cjs`, `validate-new-name-conformance.cjs`. SSOT lives at `rules/naming-convention.md` + `skills/t1k-skill-creator/references/architecture-rules.md` §0 + `skills/t1k-agent-creator/references/architecture-rules.md` §0. When `scaffold`-ing a new kit/module or auditing an existing one, run `node /path/to/release-action/scripts/validate-skill-prefix.cjs` and `validate-agent-prefix.cjs` locally — fail-fast catches missed renames before push.
- **Activation fragment ref convention — bare-slug refs are the SSOT** — entries in `t1k-activation-*.json` `sessionBaseline[]` and `mappings[].skills[]` arrays canonically use the **bare slug** form (e.g., `nakama-rpc` for the dir `t1k-nakama-rpc`). The CI prefixer's `auto-prefix-skills.cjs::buildSelfHealMap()` self-heals legacy refs to canonical dir names at release time but the SSOT in fragment files stays bare so authors can write naturally. The release-action gate `validate-activation-skill-resolution.cjs` (per-PR safety net, added 2026-05-11) accepts BOTH bare and full-prefixed forms — anything else fails. When authoring or reviewing activation fragments, use the local bare slug (`{slug}` after kit + module strip) — NOT `{kit}-{slug}` shorthand that skips the module segment, which is the legacy form the self-heal does not accept and the gate flags. See `t1k-doctor` check #47 for the doctor-time mirror.

## Future: Self-Improving AI Pipeline

T1K vision: the kit should self-teach and improve using aggregated consumer data. Planned pipeline:
1. User errors collected via telemetry → D1 (already exists)
2. Scheduled AI agent (cron) queries D1 for error clusters (same fingerprint, 3+ occurrences, multiple users)
3. AI aggregates patterns → synthesizes root cause, workaround, prevention
4. Auto-generates gotcha entries for relevant skill SKILL.md files
5. Opens PR to kit repo automatically
6. Maintainer reviews and merges → next release distributes to ALL users

**Status:** Not yet implemented. See CLAUDE.md "Self-Improving AI Vision" for context.

## Auto-Activation Keywords

Triggers on: `kit validate`, `kit release`, `kit sync`, `kit scaffold`, `kit audit`, `kit migrate`, `kit test`, `kit health`, `kit status`, `cross-kit`, `maintainer`, `test kit`, `test install`, `test modules`, `e2e kit`, `validate installation`, `self-improving`, `auto-gotcha`, `gotcha generation`, `ai aggregation`, `learn from users`, `t1k new`, `t1k rollback`, `t1k migrate`, `t1k modules create`, `module new`, `kit new`, `module migrate`, `migrate metadata`, `rollback snapshot`, `pre-snapshot`

