---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Parallel Teammate — Race-Free Git Commit Primitive

## Rule

Parallel teammates share a single git working tree and a single index (staging area). The two-step `git add` + `git commit` pattern is **BANNED** for parallel teammates because any teammate's `git add` affects the shared index; a concurrent `git commit` will sweep in files staged by the other teammate.

**Use the pathspec form instead:**

```bash
# CORRECT — atomic, index-independent
git commit -m "<message>" -- path/to/file1 path/to/file2

# BANNED for parallel teammates
git add path/to/file1 path/to/file2
git commit -m "<message>"
```

The pathspec form bypasses the index entirely — only the named paths are snapshotted into the commit, regardless of what any other teammate has staged.

## Why

Two teammates running `git add <files>` concurrently: the second `git commit` absorbs the first teammate's staged-but-uncommitted files into the wrong commit, and the first teammate then commits an empty diff, losing work.

## Banned patterns

```bash
git add .               # BANNED — sweeps entire tree
git add -A              # BANNED — sweeps entire tree
git commit -a           # BANNED — implicit add of all tracked changes
git add <files>
git commit -m "..."     # BANNED — two-step race window
```

## Required pattern

```bash
# Single atomic form — always safe under parallel execution
git commit -m "<message>" -- <file1> [<file2> ...]
```

Pre-commit verification (run before every commit):

```bash
git status --short       # confirm your files are the only modified paths
git diff HEAD -- <file1> # verify content before committing
```

## Narrow exceptions

When `git add -p` (interactive hunk staging) is required, give that teammate its own `git worktree` — index races are impossible across worktrees. Unstaged deletions: `git rm --cached <file>` then `git commit -- <file>`. A merge commit across branches (lead only) may use the two-step form because the lead is sequenced after all teammates finish.

## Related

- `rules/parallelize-batch-work.md` — when and how to parallelize
- `rules/agent-completion-discipline.md` — 150K checkpoint + commit-before-summary
- `skills/t1k-team/SKILL.md` § "Spawn Brief — Mandatory Inclusions"
