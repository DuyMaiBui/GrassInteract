---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Workflow: Session

Use this when: user wants to open or switch to an existing worktree session.

```bash
node $HOME/.claude/skills/t1k-worktree/scripts/worktree.cjs session "<NAME>" --json
```

Reports: worktree path, branch, session command (`cd <path> && claude`).
Then execute the session command for the user.
