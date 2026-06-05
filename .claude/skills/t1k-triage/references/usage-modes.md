---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Triage usage modes — full reference

This file documents the full mode/flag matrix. SKILL.md keeps a compressed version; this file keeps the safety contracts in long form.

## Modes

```
/t1k:triage              # AUTO (default) — report then act, then verify terminal state per item:
                         #   decision=solve  → /t1k:cook --auto --parallel; record cook PR URL
                         #   decision=merge  → /t1k:babysit-pr {n} per PR
                         #                     (only when triage's strict gate passes — Step 5b)
                         #   decision=close  → comment with reason + gh issue/pr close
                         #   decision=defer  → comment listing missing fields (no merge / no cook)
                         # Triage is "done" only after Step 7 verifies every item reached a terminal state.
/t1k:triage --ask        # Old interactive mode — report + AskUserQuestion per partition before acting
/t1k:triage --dry-run    # Report only, no action (overrides default auto, skips Step 7)
/t1k:triage --ecosystem  # Maintainer mode — scan ALL T1K repos. Composable with --ask / --dry-run / --yolo
/t1k:triage --yolo       # Maximum autonomy — investigate deeply, decide, MERGE in-session.
                         #   merge: gh pr merge after self-approve + Step 5b gate (Step 6c)
                         #   solve: cook + record PR URL; defer follow-up triage to next run
                         #   See Step 6b (decision matrix), Step 6c (active merge), Step 7 (DoD)
/t1k:triage --auto       # Deprecated alias for default. For full autonomy use --yolo
```

## Default-auto safety contract

The strict Step 5b auto-merge gate is what makes default-auto safe. Triage NEVER merges a PR that fails the gate, and the gate is intentionally stricter than `t1k:babysit-pr`'s standalone policy (no auto conflict resolution at the triage level — even simple frontmatter conflicts are deferred to a human). If you need a softer threshold, run `--ask` and act manually.

## `--yolo` mode safety contract

Yolo intentionally lowers the human-checkpoint gates so triage acts on items default-auto would defer. To compensate, every gate-bypass is replaced by a structured AI investigation step — t1k-code-reviewer agent verdict for merges, blocker-resolution check for tracking issues, t1k-planner+cook chain for large issues. Yolo NEVER bypasses correctness invariants (`mergeable: MERGEABLE`, green checks, `infoStatus != insufficient`, credential redaction, skill-file validation). See `references/yolo-merit-pipeline.md` and `references/yolo-decision-matrix.md` (`Step 6b`).

## Completion contract

Triage finishes only when every classified item is in a terminal state. Default-auto and `--yolo` both enforce Step 7 (Definition of Done): no item may be left "in flight" when triage reports completion.

| Decision | Terminal state |
|---|---|
| `merge` | PR `MERGED` in GitHub OR `merge-blocked: <reason>` recorded |
| `solve` | Cook PR URL recorded OR `solve-failed: <reason>` recorded |
| `close` | Issue/PR `CLOSED` in GitHub |
| `defer` | Missing-fields comment posted (Step 2d) |

In `--yolo`, triage actively performs merges itself via `gh pr merge` (does NOT delegate to `/t1k:babysit-pr`) and polls pending CI with a 10-minute bounded timeout. See `references/completion-verification.md` for the full protocol.

## `--ecosystem` mode (maintainer only)

Scans ALL TheOneKit repos regardless of which project you're in. Discovers repos by scanning the T1K parent directory for cloned kit repos, then reads each repo's `t1k-config-*.json` for the `repos.primary` value.

**Discovery algorithm:**
1. Find T1K parent dir: walk up from CWD looking for sibling `theonekit-*` directories. Fallback: `/mnt/Work/1M/8. OneAI/` (documented T1K root)
2. List all `theonekit-*` directories + `t1k-*` directories in parent
3. For each directory: read `$HOME/.claude/t1k-config-*.json` → extract `repos.primary`
4. Also include hardcoded known repos not yet cloned:
   ```
   The1Studio/theonekit-core
   The1Studio/theonekit-cli
   The1Studio/theonekit-unity
   The1Studio/theonekit-designer
   The1Studio/theonekit-cocos
   The1Studio/theonekit-rn
   The1Studio/theonekit-web
   The1Studio/theonekit-nakama
   The1Studio/theonekit-release-action
   ```
5. Deduplicate, fetch issues/PRs from all in parallel
6. Report grouped by repo, then by priority

**Note:** This mode fetches from GitHub directly — repos don't need to be cloned locally. The local scan is just for discovering additional repos beyond the hardcoded list.

## `--ecosystem` post-merge branch cleanup (Step 9)

When `--ecosystem` is set AND the run produced at least one merged PR (Step 6 / 6c), triage runs an ecosystem-wide branch-cleanup sweep AFTER Step 8 completes. Scope: only repos triage actually touched THIS run (merged a PR or pushed a branch). Pre-existing unrelated branches are left alone.

**Algorithm:**

1. Build `touched_repos` from the run's recorded merges + pushes (set, not list — deduped).
2. For each `repo` in `touched_repos`:
   1. `cd $T1K_ROOT/$repo`
   2. `git fetch --prune origin --quiet`
   3. `git checkout main` (if not already)
   4. `git pull --ff-only` — if this fails, STOP for this repo, log `cleanup-skipped: <repo> — pull failed (local divergence)` and continue to next repo. Do NOT `reset --hard`.
   5. For each local branch in `git branch | grep -v '^\*' | grep -v 'main$'`:
      - Resolve associated PR via `gh pr list --head <branch> --state all --json number,state,mergedAt`.
      - **Delete only if:** PR is MERGED in last 24h, OR PR is CLOSED and the user explicitly said `--yolo`, OR no PR exists AND branch has no commits not on main (rare — likely abandoned scaffolding).
      - **Never delete if:** branch is checked out in a worktree (`+` prefix in `git branch` output), branch has unmerged local commits, branch is referenced in active `git stash` entries.
      - Use `git branch -D <branch>` (force; squash-merge breaks ancestry check per `feedback_squash_merge_cleanup.md`).
   6. Verify final state: on `main`, `git status --short` empty (allow pre-existing untracked the run did not create).
3. Emit `[t1k:triage-cleanup]` summary line: `repos=N deleted=M skipped=K errors=E`.

**Safety contract:**

- No `--ecosystem` cleanup happens in `--dry-run` (report what WOULD be deleted instead).
- No cleanup happens for repos where the run had NO action (a repo with only deferred items is not touched).
- Cleanup respects `rules/branch-discipline.md` exceptions: worktree-checked-out branches, unmerged local commits, sync-back artifacts from other sessions.
- All cleanup output goes to the final triage report under a `## Ecosystem cleanup` section with per-repo before/after branch count.

**Anti-patterns explicitly forbidden:**

- `git reset --hard origin/main` to "force-sync" a divergent local main — refuse and skip the repo.
- `git branch -D <branch>` without resolving the PR state first.
- Touching repos NOT in `touched_repos` even if they have stale branches.
- Deleting `sync-back/*` branches authored by other sessions (preserve them).

## `--yolo` flag composition

- `--yolo --dry-run` → run full investigation (t1k-code-reviewer verdicts, blocker checks, plan generation) but take ZERO actions. Reports what yolo WOULD do. Use this to preview yolo decisions before running it live
- `--yolo --ask` → INVALID. Error out immediately: yolo means no human prompts; --ask is the opposite
- `--yolo --ecosystem` → yolo across all T1K repos. Maximum blast radius. Recommended only for trusted scheduled automation