---
name: t1k:ship
description: "One-command release pipeline: merge main, test, review, commit, push, PR. Single command from feature branch to PR URL. Use for 'ship', 'release branch', 'ready to merge', 'create PR with tests'."
keywords: [ship, release, deploy, publish, pipeline, merge, pr]
argument-hint: "[official|beta] [--skip-tests] [--skip-review] [--skip-docs] [--dry-run]"
effort: high
tools: [Read, Glob, Grep, Bash, Write, Edit, Task, Agent, AskUserQuestion, Skill]
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit Ship — Release Pipeline

Single command to ship a feature branch. Fully automated — only stops for test failures, critical review issues, or major version bumps.

## Agent Routing

Follow protocol: `skills/t1k-cook/references/routing-protocol.md`
This command uses roles: `t1k-tester`, `reviewer`, `t1k-docs-manager`, `t1k-git-manager`

## Arguments

| Flag | Effect |
|------|--------|
| `official` | Ship to default branch (main/master). Full pipeline with docs |
| `beta` | Ship to dev/beta branch. Lighter pipeline, skip docs update |
| (none) | Auto-detect from current branch naming |
| `--skip-tests` | Skip test step |
| `--skip-review` | Skip pre-landing review step |
| `--skip-docs` | Skip docs update step |
| `--dry-run` | Show what would happen without executing |

Auto-detection logic: `references/auto-detect.md`

## Pipeline

```
Step 1:  Pre-flight      -> Branch check, mode detection, status, diff analysis
Step 2:  Link Issues      -> Find/create related GitHub issues
Step 3:  Merge target     -> Fetch + merge origin/<target-branch>
Step 4:  Run tests        -> /t1k:test (abort on failure)
Step 5:  Review           -> /t1k:review (two-pass checklist, abort on critical)
Step 6:  Version bump     -> Auto-detect version file, bump patch/minor
Step 7:  Changelog        -> Auto-generate from commits + diff
Step 8:  Docs update      -> /t1k:docs update (official only, background)
Step 9:  Commit           -> /t1k:git cm with conventional commit
Step 10: Push             -> git push -u origin <branch>
Step 11: Create PR        -> /t1k:git pr with structured body + linked issues
```

Detailed steps: `references/ship-workflow.md` | PR template: `references/pr-template.md`

## Safety Gates

| Gate | Trigger | Action |
|------|---------|--------|
| On main/master | Feature branch expected | ABORT |
| Merge conflict | `git merge` fails | ABORT — resolve manually |
| Test failure | Any test fails | ABORT — fix tests first |
| Critical review finding | Severity = critical | ABORT — address findings |
| Dirty working tree | Uncommitted changes | Include them (do not abort) |
| PR creation | Always | CONFIRM — user must approve |

## Output Format

```
Pre-flight: branch feature/foo, 5 commits, +200/-50 lines (mode: official)
Issues: linked #42, created #43
Merged: origin/main (up to date)
Tests: 42 passed, 0 failed
Review: 0 critical, 2 informational
Version: 1.2.3 -> 1.2.4
Changelog: updated
Docs: updated (background)
Committed: feat(auth): add OAuth2 login flow
Pushed: origin/feature/foo
PR: https://github.com/org/repo/pull/123 (linked: #42, #43)
```

## Subagent Delegation (MANDATORY)

Steps 4, 5, 8: delegate to registry-routed subagents — do NOT inline.
Follow protocol: `skills/t1k-cook/references/subagent-injection-protocol.md` if installedModules present.

## Multi-Repo Rollout Sequence

For coordinated rollouts across the TheOneKit ecosystem (CLI-first, kit-second, adoption-gated), see `plans/reports/260422-1248-self-assembling-kit-architecture.md` §11. Critical invariants: (a) `minCliVersion` floor enforced by each kit's manifest; (b) ordered SessionStart auto-update — CLI before kit; (c) schema migrators live in CLI, not in kits; (d) atomic rename rollback on migration failure. Phase F of the safety-addendum plan operationalizes this for 50-user internal audience.

## Contribution Scoring

After Step 11 (Create PR), invoke `t1k:contribution-score` with `type=sync-back-pr` + PR URL/title/body. Fire-and-forget; SSOT gates non-T1K repos. See `.claude/skills/t1k-contribution-score/SKILL.md`.

## Sub-Agent Fork Hygiene

**Sub-agent forking:** see `skills/t1k-architecture/references/fork-hygiene.md`.
