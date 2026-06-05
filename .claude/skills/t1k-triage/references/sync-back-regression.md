---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Sync-back PR regression detection (Step 4a)

A sync-back PR is identified when ANY of the following match:
- Branch name matches `t1k-sync/*` (per `t1k-sync-back/references/open-pr.md`)
- PR body contains BOTH `## Versions (at sync time)` AND `## Rationale (why this change is generic)` headings
- PR title matches `^fix\((core|cli|unity|designer|cocos|rn|web|nakama|release-action|[a-z-]+)\): update [a-z0-9-]+`

For each sync-back PR, evaluate `statusCheckRollup` (already fetched in Step 5b). If ANY check has `conclusion ∈ {FAILURE, TIMED_OUT, ACTION_REQUIRED, STALE}`:

1. Fetch the failing run's first error excerpt:
   ```bash
   gh run view {run-id} --repo {REPO} --log-failed 2>/dev/null | head -200
   ```
2. Sanitize per Step 1c rules (redact credentials, strip absolute user paths).
3. Emit a single-line `[t1k:skill-bug ...]` marker in triage's session output:
   ```
   [t1k:skill-bug kit="theonekit-core" skill="t1k-sync-back" bug="<failing-check-name>: <one-line summary>" evidence="<repo>#<n>: <first-error-line> (run <url>)"]
   ```
   The Stop hook `lesson-collector.cjs` captures this and queues an issue against `theonekit-core`'s `t1k-sync-back` skill on next UserPromptSubmit (per `rules/telemetry.md` + `rules/error-recovery.md`).
4. Add the PR to the **"Sync-Back Regressions"** section of the Step 5 report with:
   - Repo, PR #, failing check name, run URL, first-line error
5. Mark `merge-blocked: sync-back-regression-<check-name>` (don't merge — sync-back has shipped a bad PR; closing the loop is more valuable than landing the diff).

## Why this matters

Sync-back PRs that fail CI are *prima facie* evidence that `t1k-sync-back` produced output the kit's quality gates reject. Without this detection step, the same regression keeps shipping every time sync-back fires. The marker→queue→issue pipeline turns each failure into an actionable update to the sync-back skill — closing the self-improving loop described in Core Requirement #8.

## Suppression

If the failing check is `validate-modules-registry-sync` AND the PR's diff already includes a regenerated `t1k-modules.json`, suppress the marker (CI is racing the post-merge regen). Otherwise emit unconditionally.