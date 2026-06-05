---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Workflow: Sync (Rebase)

Use this when: user wants to rebase one or all worktrees onto the latest base branch.

```bash
# Sync all worktrees
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs sync --json
# Sync specific worktree
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs sync --worktree "<NAME>" --json
```

Reports per worktree: status (success/conflict/skipped), ahead/behind, conflicts.
Skips dirty worktrees to prevent data loss. Auto-aborts failed rebases.
