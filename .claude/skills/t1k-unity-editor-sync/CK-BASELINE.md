---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: editor
protected: false
---
# CK Baseline

- **CK Version**: v2.13.0
- **Baseline Date**: 2026-03-21
- **GameKit Version**: 1.0.0

## Forked Skills (reimplemented as t1k-*)

| CK Skill | GK Skill | CK Skill Version |
|---|---|---|
| `cook` | `t1k-cook` | 2.1.1 |
| `plan` | `t1k-plan` | — |
| `test` | `t1k-test` | 1.0.0 |
| `debug` | `t1k-debug` | 4.0.0 |
| `fix` | `t1k-fix` | 1.2.0 |
| `code-review` | `t1k-review` | — |
| `docs` | `t1k-docs` | — |
| `git` | `t1k-commit` + `t1k-push` | 1.0.0 |
| `brainstorm` | `t1k-brainstorm` | 2.0.0 |

## Direct-Use Skills (no fork, use CK as-is)

These CK skills are universal and work for game projects without modification:
- `simplify`

## GK-Only Universal Commands (no CK equivalent needed)

- `t1k-scout` (wraps Explore agent with game context)
- `t1k-ask` (wraps AskUserQuestion with game skill activation)
- `t1k-watzup` (wraps git log + MCP console + TaskList)
- `journal`, `kanban`, `preview`, `sequential-thinking`
- `find-skills`, `ck-help`, `coding-level`, `worktree`

## GK-Only Skills (no CK equivalent)

- `t1k-scene`, `t1k-playtest`, `t1k-balance`, `t1k-profile`
- `t1k-milestone`, `t1k-wiki`, `t1k-sync`, `t1k-help`

## Sync Instructions

Run `/t1k:unity:editor:sync` to compare current CK skills against this baseline.
Update this file after incorporating CK changes.
