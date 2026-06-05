---
name: t1k:babysit-pr
description: "Monitor a PR to green+merged. Use for: 'babysit pr', 'watch pr', 'monitor pr', 'flaky ci', 'auto merge'. Retries flaky CI, resolves simple conflicts, auto-merges when approved+green."
keywords: [monitor, pr, watch, merge, flaky, ci, pull-request]
argument-hint: "<pr-number-or-url> [--dry-run]"
effort: medium
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit Babysit PR

Monitor a pull request until it is merged. Handles flaky CI, simple merge conflicts,
and auto-merges when conditions are met.

## Trigger Phrases
"babysit pr", "babysit-pr", "watch pr", "monitor pr", "auto merge", "flaky ci"

## Workflow

```
Start
  └─ Fetch PR state (status, reviews, conflicts)
       ├─ APPROVED + GREEN + no conflicts → merge
       ├─ CI FAILING → classify failure
       │     ├─ Known flaky test → retry CI
       │     └─ Real failure → report to user, stop
       ├─ MERGE CONFLICT → attempt auto-resolve
       │     ├─ Simple (non-overlapping) → resolve + push
       │     └─ Complex (overlapping logic) → report to user, stop
       └─ PENDING → wait (poll interval: 60s) → loop
```

## Operations

| Flag | Default | Description |
|---|---|---|
| `--dry-run` | off | Print actions without executing them |
| `--max-retries` | 3 | Max CI retry attempts before escalating |
| `--poll-interval` | 60s | Seconds between status checks |
| `--no-auto-merge` | off | Skip merge step (monitor only) |

## Commands Used

```bash
gh pr view <number> --json state,reviews,statusCheckRollup,mergeable
gh pr checks <number>
gh run rerun <run-id> --failed
gh pr merge <number> --squash --auto
gh pr diff <number>
```

## Merge Conditions (all must be true)

- [ ] At least 1 approved review
- [ ] No changes-requested reviews
- [ ] All required status checks green
- [ ] No merge conflicts
- [ ] PR is not in draft state

## Conflict Resolution Policy

**Auto-resolve (safe):**
- Documentation-only files (`.md`, `CHANGELOG`, `docs/`)
- Generated files with clear regeneration command
- Package lock files (re-generate from manifest)

**Escalate to user (unsafe — stop):**
- Overlapping logic in source files
- Schema or migration conflicts
- Any conflict in security-sensitive files

## Claim Lifecycle Awareness

A draft PR carrying the `t1k:claim` label and body marker `t1k-claim: owner/repo#N` represents a **live claim** on the referenced issue. babysit-pr observes the following lifecycle states:

| PR state | Claim state |
|----------|-------------|
| Draft (`isDraft: true`) + `t1k:claim` label | Live claim — issue is booked |
| Ready-for-review (`isDraft: false`) | Released-to-review — claim transitioning to merge |
| Merged or closed | Claim ended — GitHub state is the truth; no extra mutation needed |

**Auto-release on merge/close:** when babysit observes the PR merge or close, the claim is automatically released by the merge/close event itself. No additional `gh issue edit` or claim script call is needed.

### Competing-PR detection

During each poll, check for competing draft PRs on the same issue:

```bash
node .claude/scripts/t1k-issue-claim.cjs check <owner/repo#N>
```

If `competingPRs` is present in the response (≥2 PRs claim the same issue), surface a warning:

```
WARNING: Competing PRs on issue #N — PR #<lower> and PR #<higher> both claim this issue.
Recommend: close PR #<higher> (lower PR# wins the tie-break per claim convention).
```

This is a monitoring signal only; babysit-pr does NOT auto-close the competing PR. Surface for human action. This duplicates the backstop in t1k-triage (two detection points for the no-CAS residual risk).

## Admin-Merge Mode (data-driven, default OFF)

Admin-merge is an opt-in mode gated by `issueClaim.adminMerge.enabled` in `.claude/t1k-config-core.json`. It is OFF by default.

**When it fires:** ONLY when ALL of the following are true:
1. `adminMerge.enabled == true` in config
2. PR is **not draft** (`isDraft: false`)
3. `statusCheckRollup == SUCCESS` (all required CI checks green)

**Command:**
```bash
gh pr merge <N> --admin --squash --delete-branch
```

**What `--admin` bypasses:** the required-human-review branch protection rule ONLY — so an unattended PR (e.g., a sync-back PR with no available reviewer) can land. It does **NOT** bypass red or pending CI.

**Fallback when CI is red or pending:** do NOT admin-merge. Fall back to standard auto-merge:
```bash
gh pr merge <N> --auto --squash --delete-branch
```
Report the fallback reason. Never silently override CI status.

**Config reference:** `issueClaim.adminMerge` block in `t1k-config-core.json`:
- `enabled` (bool, default `false`) — master gate
- `requireCiGreen` (bool, default `true`) — enforced; admin-merge never fires on red/pending CI
- `bypassReview` (bool, default `true`) — documents that `--admin` bypasses required-review ONLY

This is the SSOT for admin-merge logic. Phase 4's ship pipeline invokes this mode; it does not duplicate the merge prose here.

## Gotchas

- **Branch protection rules:** If the repo requires a specific merge method (squash/rebase/merge),
  detect it from `gh repo view --json mergeCommitAllowed,squashMergeAllowed,rebaseMergeAllowed`
  before merging. Default: `--squash`.
- **CI retry abuse:** GitHub rate-limits workflow reruns. After `--max-retries` attempts on the
  same check, stop retrying and report the persistent failure. Never retry more than 3 times.
- **Stale approval:** If new commits are pushed after approval and "dismiss stale reviews" is
  enabled, the PR needs a fresh approval. Detect via `review.state == DISMISSED`, escalate.
- **Auto-merge already set:** If `gh pr view` shows `autoMergeRequest` is set, skip the merge
  step — GitHub will handle it. Avoid double-setting.
- **Draft PRs:** Never attempt to merge a draft PR. Check `isDraft` field first.
- **Missing `gh` CLI:** Verify `gh auth status` before starting. Fail fast if unauthenticated.
- **Admin-merge CI gate:** `--admin` NEVER bypasses red or pending CI. If `statusCheckRollup != SUCCESS`, fall back to `--auto` and report. Do not silently land a bad PR.

## Gotchas

- Never force-push or rewrite history on shared branches.

## Contribution Scoring

On first poll only, invoke `t1k:contribution-score` with `type=sync-back-pr` + PR URL/title/body (catches PRs opened outside any T1K skill). Fire-and-forget; SSOT gates non-T1K repos. See `.claude/skills/t1k-contribution-score/SKILL.md`.
