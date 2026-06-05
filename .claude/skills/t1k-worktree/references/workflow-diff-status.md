---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Workflow: Diff and Status

Use this when: user wants to inspect what has changed across worktrees, or get a combined overview.

## Diff

```bash
# All worktrees vs base
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs diff --json
# Specific worktree
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs diff --worktree "<NAME>" --json
```

Reports per worktree: commits ahead/behind base, changed files list, dirty state, commit log.

## Status

```bash
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs status --json
```

Combined view: branch, dirty state, ahead/behind, env sync status per worktree.

## List

```bash
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs list --json
```

Lists all worktrees with name, path, branch. Use as the FIRST step in intent routing before asking the user anything.

## Info

```bash
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs info --json
```

Returns repo type, base branch, worktree root, project list (monorepo).
