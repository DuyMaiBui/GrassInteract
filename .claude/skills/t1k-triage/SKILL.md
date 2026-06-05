---
name: t1k:triage
description: "Triage GitHub issues and PRs across all kit repos. Fetches, classifies, and auto-implements actionable items. Use for 'review open issues', 'what needs fixing', 'process PR backlog'."
keywords: [triage, issues, backlog, classify, prioritize, github, process]
argument-hint: "[--dry-run|--ask|--ecosystem|--yolo]"
effort: high
tools: [Read, Glob, Grep, Bash, Write, Edit, Task, Agent, WebFetch, AskUserQuestion, Skill]
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit Triage — Issue and PR Review

Structured triage workflow across all repos registered in kit configs.

## Usage (compressed)

**Default behavior is AUTO.** Bare `/t1k:triage` runs the action phase end-to-end without prompting per item.

| Invocation | Behavior |
|---|---|
| `/t1k:triage` | AUTO (default) — report, act, verify per item |
| `/t1k:triage --ask` | Interactive — `AskUserQuestion` per partition |
| `/t1k:triage --dry-run` | Report only, no action |
| `/t1k:triage --ecosystem` | Maintainer mode — scan ALL T1K repos |
| `/t1k:triage --yolo` | Maximum autonomy — investigate deeply, decide, MERGE in-session |

Full mode matrix, safety contracts, and `--ecosystem` discovery algorithm: `references/usage-modes.md`.

**Default merge command:** `gh pr merge {n} --repo {REPO} --auto --squash --delete-branch`. Admin-bypass (`--admin`) is escalation-only — see Step 5a.5 (auto path) vs Step 5b (admin fallback) for the full rule.

## Routing

1. Read ALL `t1k-config-*.json` → collect all `repoUrl` values
2. Deduplicate repo URLs
3. Fetch issues/PRs from ALL repos in parallel
4. Label each item with source repo

## Scaling — when to hand off

Single-agent triage works for small backlogs. Past these triggers, delegate to `/t1k:team`:

- **> 20 items total**, OR
- **> 15 items in any single repo**, OR
- Items split into clearly-orthogonal categories (dependabot batch, sync-back PRs, bug fixes, tracker issues), OR
- Same skill/file appears in 3+ open PRs (likely-duplicate cluster)

Full hand-off pattern + partition rules: `references/scaling-handoff.md`.

## Per-item triage patterns

Apply to EVERY item, regardless of single-agent or team-fanout mode. Full pattern detail: `references/per-item-patterns.md`.

**5-verdict vocabulary:** `merge` / `close-superseded` / `close-obsolete` / `needs-fix` / `defer`

**Adversarial evidence requirement:** every verdict line MUST cite evidence in one of: `file:line` / commit SHA / 1-line excerpt / workflow run-job ID. **No verdict without evidence — if you can't cite, the verdict is `defer`.**

Other patterns covered in the reference: dup-cluster handling (3+ PRs on same skill), CI failure classification (infra-vs-real), tracker-issue verification (grep for the named artifact), and reasoning rules.

## Workflow

```
[Fetch] → [Classify] → [Analyze] → [Review PRs] → [Report] → [Cook] → [Verify]
```

### Step 1 — Fetch (parallel per repo)

```bash
gh issue list --repo {REPO} --state open --json number,title,labels,createdAt,body --limit 50
gh pr list --repo {REPO} --state open --json number,title,labels,createdAt,body,files,author --limit 50
```

### Step 1b — Repo discovery (module-aware)

Read ALL `t1k-config-*.json` → collect repos. For modular kits, note which modules exist per kit.

### Step 1c-pre — High-impact candidates (optional)

If telemetry endpoint resolves and `gh auth token` works, fetch top-5 score-ranked candidates and render at the top of Step 5 report (heading: `## 🎯 High-Impact Candidates`). On any failure, skip silently — never fail triage.

```bash
T=$(gh auth token 2>/dev/null); U=$(gh api user --jq .login 2>/dev/null)
ENDPOINT="${T1K_TELEMETRY_ENDPOINT:-$(jq -r '.telemetry.cloud.endpoint // empty' "$HOME/.claude/t1k-config-core.json" 2>/dev/null | sed 's,/ingest$,,')}"
[ -n "$T" ] && [ -n "$U" ] && [ -n "$ENDPOINT" ] && curl -sf -H "Authorization: Bearer $T" \
  "${ENDPOINT}/api/contributors/find-issue?priority=high&limit=5&user=${U}"
```

**Critical override:** issues labeled `critical`/`P0`/`security` always appear first regardless of score.

### Steps 1c, 1d, 2, 2b, 2c, 2d — comment enrichment and classification

These steps fetch comments + reviews per item, run effort estimation, and apply the InfoStatus completeness check that gates `solve`/`merge` decisions. **`infoStatus: insufficient` items MUST decision = `defer`** — never silently `solve` them.

Full procedural detail: `references/comment-enrichment-and-classification.md` (covers Step 1c throttling/privacy/sanitization, Step 1d backfill, Step 2 classification fields, Step 2b effort heuristics, Step 2c comment classifier signals, Step 2d completeness gate).

### Step 3 — Analyze issues

For each issue: read body + enriched comments, run Step 2d completeness check, check if skill/agent exists, check for duplicates, determine decision (`close` / `merge` / `solve` / `defer`). Do NOT mark an item `solve` without `infoStatus: complete` or `partial`.

#### Step 3-claim — Claim check on `needs-fix` decisions

When an issue is classified `needs-fix` (decision = `solve`), run a claim check via the SSOT script BEFORE scheduling any cook work:

```bash
node .claude/scripts/t1k-issue-claim.cjs check <owner/repo#N>
```

Interpret the JSON result:

| `state` | Action |
|---------|--------|
| `free` | `acquire` (self-assign): `node .claude/scripts/t1k-issue-claim.cjs acquire <owner/repo#N>` — then proceed to schedule the fix |
| `held` | **Skip scheduling.** Report holder + prNumber in the triage report: `"Issue #N already claimed by <holder> · PR #<prNumber> — skipped to avoid double-booking."` Do NOT call acquire or open a competing PR. |
| `stale` | Report as an abandoned claim (PR age > `stalenessDays` from `issueClaim` config). Surface in a dedicated **Stale Claims** subsection of the report for human follow-up. No auto-close; no auto-release. |
| `skip` | Issue is out of scope (not in tracked repos or already closed) — treat as `defer`. |

**Constraint:** never call `gh issue edit`, `gh pr create`, or any inline `gh` claim mutation here. All claim state changes go through `t1k-issue-claim.cjs`.

#### Step 3-dup — Duplicate-PR detection backstop

When `check` returns a `competingPRs` array (≥2 draft PRs reference the same issue), recommend **close-later** for the higher PR number (tie-break backstop — lowest PR# wins per the acquire-time tie-break in the claim script). Surface in the triage report:

```
Competing PRs on #N: PR #<lower> (keep), PR #<higher> (recommend close-later — duplicate claim)
```

This catches the sub-second double-acquire window that the script's in-band tie-break may have missed.

#### Step 3-Y — `--yolo` defer-rationale guard (MANDATORY when `--yolo`)

When `--yolo` is set, before finalizing any `decision=defer`, you MUST audit the defer rationale against this forbidden-phrase list. If the rationale uses ANY of these phrases for an issue whose `infoStatus` is `complete` or `partial`, the classification is **invalid** and MUST be reclassified as `decision=solve` with plan→cook chain.

**Forbidden defer rationales under `--yolo`** (non-exhaustive — match by intent, not literal):

- "needs maintainer UX decision"
- "needs maintainer design decision"
- "decision-policy=human"
- "multi-option UX gate"
- "design decision required"
- "needs human policy decision"
- "needs maintainer input"
- "human decision needed"
- "complex bug; needs maintainer X decision"
- "N valid fix strategies" / "N candidate fixes" / "N design options" as the SOLE defer reason
- "route to `/t1k:plan`" *posted as a comment instead of actually spawning the plan*

If you find yourself writing any of the above for an item with documented options, STOP. The presence of documented options IS the context — there is no missing data. Reclassify and execute the plan→cook chain per `references/yolo-decision-matrix.md` § "Plan→Cook chain". The Conservative-Pick algorithm selects among options without human input.

**`defer` remains valid under `--yolo` ONLY for these causes** (allowlist — anything else is forbidden):

| Allowed defer cause | Evidence required |
|---|---|
| `infoStatus: insufficient` | Step 2d completeness check fails — specific missing fields listed |
| Calendar gate (revisit-on-date) | Issue body or hook cites a future date; cite `file:line` of the expiry constant |
| Upstream prerequisite missing | Named framework/module/file does not yet exist; cite `gh issue view` or `grep -r` evidence |
| External fork (no write access) | `headRepositoryOwner.login != base owner` + active author |
| Out-of-scope (not in T1K ecosystem) | Repo is not in any `t1k-config-*.json` `repoUrl` list (e.g., ClaudeAssistant, MCP forks) |

**Do not rubber-stamp prior triage defers.** If a previous run deferred for a now-forbidden rationale, the current run MUST re-evaluate against this rule. The new triage's decision supersedes the prior comment — the new rule is the SSOT. Cite the rule activation in the report.

### Step 3.5 — Stale-base auto-rebase (`--yolo` only)

**Trigger — ALL of the following must hold:**
- PR has one or more failing CI checks (excluding `Claude Code Review`, which is a non-required infra check that never gates merges)
- PR's base commit is behind current `main` by N≥1 commits (`gh pr view {n} --json mergeStateStatus` returns `BEHIND`)
- PR branch is NOT checked out in a local `git worktree` (no `+` prefix in `git branch` output)
- Running in `--yolo` mode

**Action (per qualifying PR):**

```bash
git fetch origin --quiet

# Verify branch not in worktree
git worktree list --porcelain | grep -q "branch refs/heads/{pr-head-ref}" && echo "WORKTREE-SKIP" && exit 0

git checkout {pr-head-ref}
git rebase origin/main
```

- **Conflict detected** (`git rebase` exits non-zero): `git rebase --abort`, skip this PR for the rebase step, classify as `defer` with reason `stale-base-rebase-conflict`. Do NOT force-push. Comment on PR: `triage: rebase onto main produced conflicts; leaving for human resolution.`
- **Clean rebase**: `git push --force-with-lease origin {pr-head-ref}`. Return to triage CWD after push.

After a clean push, **wait ~30s then re-fetch CI status** via `gh pr view {n} --repo {REPO} --json statusCheckRollup`. Re-classify based on new results before proceeding to Step 4.

**Always log:** `"Auto-rebased PR #N onto main (was N commits behind). New HEAD: <sha>."`

### Step 4 — Review PRs

Spawn `t1k-code-reviewer` agent per PR. If fixable issues found, push review comments via `gh pr review`.

**Skill file gate:** If a PR modifies `$HOME/.claude/skills/` files (SKILL.md, references/, scripts/), run `/t1k:skill-creator validate <skill-name>` before recommending merge. Skillmark conventions (frontmatter, progressive disclosure, effort tags, gotcha format, body tightness §K) MUST be verified.

**Body-tightness check (auto, when SKILL.md is in diff):** scan added lines for incident-marker patterns (dates, PR refs, `Originating incident:`, `Verified failure`, commit hashes, `Real-world miss`). If detected, mark `merge-blocked: tightness-violation` and recommend moving content to `references/`. Per `skills/t1k-skill-creator/references/architecture-rules.md` §K — body bloat ships to ~50 consumers × every session. CI gate `validate-skill-body-tightness.cjs` will surface gate output.

### Step 4a — Sync-back PR regression detection (all modes)

Sync-back PRs that fail CI are *prima facie* evidence that `t1k-sync-back` produced output the kit's quality gates reject. For each detected sync-back PR with red CI: emit `[t1k:skill-bug ...]` marker, add to "Sync-Back Regressions" section of report, mark `merge-blocked: sync-back-regression-<check>`.

Full identification rules, fetch commands, sanitization, and suppression conditions: `references/sync-back-regression.md`.

### Steps 4b / 4c / 4d — `--yolo` merit pipeline

When `--yolo` is set, run a 3-step pipeline that turns AI judgement into real GitHub state changes (instead of internal bypasses): **4b** Risk Classification (low/medium/high) → **4c** Auto-Fix On PR Head (origin markers, t1k-modules.json regen, markdownlint) → **4d** Self-Approve via `gh pr review --approve`. After 4d, `reviewDecision === APPROVED` is genuinely true; the unmodified Step 5b strict gate passes.

**Merit-pass requires ALL of:** t1k-code-reviewer = `approve`, risk = `low`, PR author ≠ self, Step 4c auto-fix clean, Step 4d API call OK. Any failure → defer to human, none silent.

Full pipeline detail (risk thresholds, fixable categories, push protocol, body template, rollback semantics): `references/yolo-merit-pipeline.md`.

### Step 5 — Report

Save to: `plans/reports/triage-{YYMMDD}-{HHMM}-triage.md`

Module-aware report format:

| # | Repo | Module | Type | Effort | S/M/L | Priority | Title |

**Required report sections (in order):**

1. Triage summary — counts per decision
2. 🎯 High-Impact Candidates (Step 1c-pre, optional)
3. Items by decision — full classification table
4. **Sync-Back Regressions** (Step 4a) — only if any sync-back PRs failed CI
5. Actions taken (in-session merges, closes, cook PR URLs)
6. Deferred items (with reasons)
7. **Contribution Score** (Step 8) — appended at the very end

### Step 5a.5 — Mark auto-merge after merit approval (`--yolo` + non-draft only)

**Trigger — ALL of the following must hold:**
- Triage classified the PR as `merge`
- Merit-pipeline approval succeeded (Step 4d `gh pr review --approve` returned OK)
- `isDraft: false` — calendar-gated or author-WIP PRs NEVER receive the auto-merge flag
- Running in `--yolo` mode

**Action — this REPLACES today's Step 5b admin-bypass for routine non-draft cases:**

```bash
gh pr merge {n} --repo {REPO} --auto --squash --delete-branch
```

This sets GitHub's **native auto-merge flag**. GitHub will squash-merge and delete the branch automatically once all REQUIRED checks pass. Non-required checks (e.g., `Claude Code Review`) do NOT block auto-merge.

**Prerequisite — repo-level setting:** the target repo MUST have `allow_auto_merge: true` (GitHub → Settings → Pull Requests → "Allow auto-merge"; or `gh api -X PATCH repos/{owner}/{repo} -F allow_auto_merge=true`). All 10 T1K kit repos were enabled on 2026-05-20. **New kits scaffolded via `t1k:kit scaffold` MUST set this** — `gh api -X PATCH repos/The1Studio/<new-kit> -F allow_auto_merge=true` after repo creation.

**Symptom when disabled:** `gh pr merge --auto` returns `GraphQL: Auto merge is not allowed for this repository (enablePullRequestAutoMerge)`. Fallback: fall through to Step 5b admin-bypass for this PR, AND file an issue against the target repo to enable `allow_auto_merge: true`. Do NOT perpetuate the admin-bypass workaround — fix the repo setting.

**Always log:** `"Marked PR #N for auto-merge (squash, delete branch). GitHub will merge when CI green."`

**Important:** if a check that was previously non-required becomes required AFTER the auto-merge flag is set, GitHub will silently wait for it indefinitely — the flag will never fire. If this is suspected, verify the current required-checks list with `gh api repos/{REPO}/branches/main/protection --jq '.required_status_checks.contexts'` and re-evaluate.

Step 5b admin-bypass (`gh pr merge --admin`) REMAINS available for emergencies (e.g., a required check is stuck in a known-broken state and the team needs to ship immediately). Admin-bypass is NOT the routine path — it bypasses GitHub's merge queue and is an escalation of last resort.

### Step 5b — Auto-merge gate

Before any merge action, every PR with `decision: merge` MUST pass the **strict** gate (isDraft=false, reviewDecision=APPROVED, all checks SUCCESS, mergeable=MERGEABLE, mergeStateStatus ∈ {CLEAN, BEHIND}, skill-file gate). Anything ambiguous defers to human; **never relax the gate to chase throughput**.

**Admin escalation (yolo only):** when the operating user holds admin role on the target repo AND Steps 4b–4d cannot land an approval (e.g., self-authored PR where GitHub forbids self-approve, or branch protection won't accept bot APPROVE alone), `gh pr merge --admin --squash --delete-branch` is permitted as a final escalation IFF all other gates pass (checks green, mergeable, no requested changes). The merge MUST still satisfy the strict gate except for `reviewDecision=APPROVED`. Cite "admin-override (yolo)" in any output that records the merge. **Step 5a.5 is preferred over this escalation for non-draft PRs where merit-pipeline approval succeeded.**

Full gate spec, `--yolo` flow into the gate, and Step 5c BEHIND-branch handling: `references/auto-merge-gate.md`.

### Step 6 — Action phase

The classifier output is partitioned by `decision`. **By default (no flag) triage runs the auto path** for every partition; `--ask` switches to interactive prompts; `--dry-run` reports only.

Run partitions in parallel where possible; merge actions in parallel batches of 5 to avoid GitHub secondary rate limits.

| Decision | Auto path (default) | `--ask` mode |
|---|---|---|
| `merge` | Default-auto delegates `/t1k:babysit-pr {n} --repo {REPO}`. `--yolo` merges directly via `gh pr merge --squash --delete-branch`. Items failing the Step 5b gate → `merge-blocked: <reason>` | List in "Ready to merge" group; `AskUserQuestion` per partition |
| `solve` | `/t1k:cook --auto --parallel` for items where `infoStatus` ∈ {`complete`, `partial`} | `AskUserQuestion` per partition |
| `close` | Comment with reason via template, then `gh issue close` / `gh pr close` | Show in "Recommended close" group; ask per item |
| `defer` (default-auto) | NO action — Step 2d already posted the missing-fields comment | Same as auto |
| `defer` (`--yolo`, cause = **policy/UX decision among documented candidate fixes**) | **Auto-escalate to `solve`** and run plan→cook chain with the Yolo Plan Contract + Conservative-Pick algorithm (`references/yolo-decision-matrix.md`). NEVER wholesale-defer for "needs human policy decision" under `--yolo` — that violates the doctrine | Same as auto |
| `defer` (`--yolo`, cause = `infoStatus: insufficient` / calendar gate / upstream prerequisite missing) | NO action — same as default-auto. These are data/time/external gates, not policy gates. Re-running cannot move them | Same as auto |

**NEVER auto-merge `defer` items.** They lack the information required to verify safety. **NEVER cook `defer` or `close` items** — except the explicit yolo policy-decision auto-escalation row above, which is a re-classification to `solve` BEFORE cook, not a cook-on-defer.

`close` template: `Closing per triage on {date}: {reason}. Re-open if context changed.`

`--ask` mode "do all" shortcut: when user replies "do all" / "yes to everything" to the partition prompt, run that partition's auto path immediately.

### Step 6b / 6c — `--yolo` decision matrix and active merge

When `--yolo` is set, every decision that default-auto would defer is replaced with a structured AI investigation step (t1k-code-reviewer for merges, blocker-resolution for tracking issues, plan→cook chain for large issues). After approval + gate pass, yolo merges in-session via `gh pr merge` (does NOT delegate to babysit-pr — direct merge is the only way to satisfy the Completion Contract).

Full decision matrix, blocker-detection regex, plan→cook chain, yolo invariants, failure handling, and active-merge polling protocol: `references/yolo-decision-matrix.md`.

### Step 6d — `--yolo` Issue Disposition Sweep (anti-wholesale-defer)

When ≥ 3 issues remain queued for plan→cook chain (Step 6b) and the combined wall-clock would exceed 30 min, the sweep fires **before** Step 6b. It runs parallel background investigation subagents (chunks of 4–6 issues, max 4 concurrent per `parallelize-batch-work.md`), classifies each into the 10-state taxonomy, and executes safe closures (`stale` / `dup` / `cant-repro` / `tracker-redundant`) inline. The rest become structured `comment-and-leave-open` actions or enter the cook queue with per-item fix sketches.

**Anti-pattern this kills:** reports that say "16 issues deferred for user prioritization" because plan→cook chains were too big. Yolo doctrine is "investigate before deciding" — wholesale deferral is no longer a valid outcome when the sweep can run.

**Conservative close criteria — never overridden by `--yolo`:** closes only require ONE of {stale + code-changed proof, explicit dup with named survivor, cant-repro verified on current `main`, tracker-redundant with named replacement script}. Any other state → `comment-and-leave-open`.

Full procedure (partition, dispatch, classification taxonomy, close criteria, aggregation, report integration, failure handling): `references/yolo-issue-disposition-sweep.md`.

### Step 7 — Definition of Done

After Step 6 dispatches all actions, triage runs a completion-verification loop. **Triage MUST NOT report completion until every classified item is in a terminal state.**

| Decision | Terminal state |
|---|---|
| `merge` | PR `MERGED` in GitHub OR `merge-blocked: <reason>` recorded |
| `solve` | Cook PR URL recorded OR `solve-failed: <reason>` recorded |
| `close` | Issue/PR `CLOSED` in GitHub |
| `defer` | Missing-fields comment posted (Step 2d) |

Polling: every 60s with a 30-minute total budget (configurable via `T1K_TRIAGE_TIMEOUT_MIN`) and 10-attempt per-item cap. Final report MUST include the terminal_marker for every item. `--dry-run` skips Step 7. `--ask` runs Step 7 only against items the user approved.

Full algorithm, verification commands, interaction rules: `references/completion-verification.md`.

### Step 8 — Contribution Score Report (this run only)

After Step 7 verifies all items reached terminal state, render a contribution-score summary scoped strictly to **items handled during this triage run** — NOT the user's lifetime aggregate. Throughout Steps 1d/6/6a/6c, triage POSTs each item to the contribution-score worker and accumulates `runScores[]` in memory. Step 8 just renders that table — does NOT call `/api/contributors/me`.

Full table format, failure handling, and boundary rules: `references/contribution-score-report.md`.

### Step 9 — `--ecosystem` branch cleanup (post-merge sweep)

When `--ecosystem` AND the run merged ≥1 PR, sweep local stale branches across the repos triage actually touched THIS run. Each repo: `fetch --prune`, `checkout main`, `pull --ff-only`, then `git branch -D <branch>` for each local branch whose remote PR is MERGED (or CLOSED + `--yolo`). Hard-skip on local divergence, worktree-checked-out branches, unmerged commits, sync-back artifacts from other sessions. Never `git reset --hard`. Emit `[t1k:triage-cleanup]` summary. Skipped in `--dry-run`.

Full algorithm, safety contract, anti-patterns: `references/usage-modes.md` § Ecosystem post-merge branch cleanup.

## Guardrails — Auto-Rebase and Auto-Merge

These invariants apply to Steps 3.5 and 5a.5. Any failure collapses to `defer`; nothing is silent.

| Invariant | Reason |
|---|---|
| NEVER auto-rebase a branch checked out in a worktree (`+` prefix in `git branch`) | Worktree checkout is "in active use elsewhere" — rebase would corrupt the worktree's HEAD |
| NEVER auto-rebase outside `--yolo` mode | Interactive mode users expect deterministic, unmodified branch state; a background rebase would be surprising and hard to undo |
| NEVER auto-merge a DRAFT PR | Draft = author-WIP. The `isDraft` field is an explicit signal to hold. Calendar-gated PRs (e.g., core#234 "revisit-on-date") must also stay manual even after CI turns green |
| NEVER use `--auto` flag on a PR that failed merit pipeline | Auto-merge waits for CI but never adds the missing approval; the PR will sit forever pending a human review |
| NEVER admin-bypass as a shortcut when Step 5a.5 applies | Admin override bypasses GitHub's merge queue and removes the audit trail that `gh pr merge --auto` preserves — reserve for genuine emergencies only |
| Watch for newly-required checks | The `--auto` flag silently waits for all REQUIRED checks. If a check transitions from optional to required AFTER the flag is set, auto-merge will never fire. Verify `required_status_checks.contexts` when a PR appears stalled |

## Agents

| Phase | Agent |
|---|---|
| PR review (default + yolo) | `t1k-code-reviewer` |
| Skill validation | `t1k-skills-manager` |
| Implementation | `/t1k:cook` (registry-routed) |
| Yolo: large-issue planning | `/t1k:plan` (registry-routed) |
| Sync-back regression filing (Step 4a) | `/t1k:issue` (auto-spawned by lesson pipeline) |

## Future: Self-Improving AI integration

See: `references/self-improving-ai.md`
