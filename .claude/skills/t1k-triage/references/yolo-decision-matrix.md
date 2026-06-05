---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Step 6b — `--yolo` mode decision matrix

When `--yolo` is set, every decision that default-auto would defer is replaced with a structured AI investigation step. Principle: **investigate before deciding, decide before deferring**.

## Decision matrix

| Default-auto behavior | Yolo behavior |
|---|---|
| `merge` blocked on missing APPROVED review | Run merit pipeline (Steps 4 → 4b → 4c → 4d): t1k-code-reviewer agent + risk classifier + auto-fix on PR head + self-approve via `gh pr review --approve`. After Step 4d, `reviewDecision` is genuinely APPROVED in GitHub state and the unmodified Step 5b strict gate passes. Merit-pass requires ALL of: t1k-code-reviewer = `approve`, risk = `low`, PR author ≠ self, auto-fix succeeded |
| `solve` skipped because `effort: large` | Auto-chain `/t1k:plan` → `/t1k:cook --auto` per phase. Sequential per-phase (later phases may depend on earlier output). Stop chain on first phase failure |
| Wholesale defer when ≥ 3 issues queued for plan→cook AND combined effort > 30 min wall-clock | **Run Step 6d Issue Disposition Sweep** *before* the plan→cook chain. Spawn parallel background subagents (chunks of 4–6, max 4 concurrent), classify each issue into the 10-state taxonomy, execute safe closures inline (stale / dup / cant-repro / tracker-redundant), post `comment-and-leave-open` for ambiguous cases, and surface a structured cook queue with per-item fix sketches. **Wholesale "deferred for user prioritization" is no longer a valid yolo outcome.** Full procedure: `references/yolo-issue-disposition-sweep.md` |
| Tracking issue skipped per memory rule (`feedback_tracking_issue_pattern.md`) | Investigate declared blockers (parse body + comments for blocker references). For each reference, run `gh issue view {n} --json state,closed` / `gh pr view {n} --json state,merged`. If ALL blockers are resolved → escalate to `solve` and run plan→cook chain. If ANY still open → comment with current blocker status, defer |
| `defer` because `infoStatus: insufficient` | UNCHANGED. Yolo does NOT manufacture missing repro info — the comment-and-defer behavior remains. Acting without repro data is a correctness violation, not a policy gate |
| `defer` because **issue requires a UX/policy decision among multiple documented candidate fixes** (e.g., 2–4 fix options listed in the body) | **Auto-escalate to `solve` and run plan→cook chain.** The "missing context" is a *decision*, not data — and decisions are exactly what yolo is allowed to make. The plan agent enumerates the options, applies the Conservative-Pick algorithm (below), documents rationale, and proceeds. NEVER defer for "policy decision needed" under `--yolo` — that violates the doctrine |
| `defer` because issue is calendar-gated (revisit-on-date) or blocked on an upstream prerequisite that does not exist yet (`gate-missing` framework dependency, awaiting consumer-side repro from a different repo) | UNCHANGED. These are time/external-dependency gates, not policy gates. Re-running the triage cannot move them; only the calendar / upstream can |
| `close` with reason | UNCHANGED. Same template, same action |

## Tracking-issue blocker-detection regex

```
/(blocked on|depends on|waiting on|prerequisite[d]?:?|prereq:?)\s*(?:#(\d+)|PR\s*#?(\d+)|([A-Za-z0-9_.-]+\/[A-Za-z0-9._-]+#\d+))/gi
```

Capture: local `#NN`, `PR #NN`, or cross-repo `org/repo#NN`. Resolve each via `gh` and aggregate state. If parsing yields zero blocker references in a tracking-style issue, treat the whole issue as still-tracking (conservative) and defer with comment listing the heuristic used.

## Plan→Cook chain (large issues + policy-decision issues)

1. Spawn `/t1k:plan` skill with full issue context (title, body, comments, labels, repo, module hint from Step 2b). The spawn prompt MUST include the **Yolo Plan Contract** below verbatim so the spawned plan agent does not stall on user input.
2. Plan agent writes phased plan to `plans/{YYMMDD}-{HHMM}-issue-{repo-slug}-{n}/plan.md` + per-phase files. For policy-decision items, the plan's research phase enumerates options and the Conservative-Pick algorithm selects one with documented rationale — no `AskUserQuestion` / `ExitPlanMode` waits
3. For each phase sequentially: spawn `/t1k:cook --auto --plan-dir {path}`
4. Surface aggregate result in triage report: phases shipped, phases failed, links to PRs created, *and* the chosen option + rationale for policy-decision items
5. On any phase failure: STOP chain, mark remaining phases `chain-blocked: phase-{N}-failed`, post comment on the source issue with a summary

### Yolo Plan Contract (spawn prompt must include verbatim)

```
You are spawned by /t1k:triage --yolo. Operate under these invariants:

1. NO user input — do NOT call AskUserQuestion, do NOT call ExitPlanMode.
   You are operating in non-interactive mode; the triage agent will not relay answers.
2. When the issue documents multiple candidate fixes (e.g., 2–4 numbered options),
   you MUST select ONE via the Conservative-Pick algorithm and document the
   rationale in the plan's "Decision" section. Do NOT list options without picking.
3. Write the plan directly to disk. The triage agent reads it back.
4. If you genuinely lack information that no amount of code-reading can supply
   (e.g., business-rule intent unknown), write a minimal plan that posts a
   targeted comment on the source issue requesting that ONE piece of info, and
   mark the plan `chain-blocked: needs-product-input` — do not stall the run.
```

## Conservative-Pick Algorithm (policy-decision auto-resolution)

When the source issue documents 2–4 candidate fixes, the plan agent picks ONE using this priority order (first match wins):

1. **Opt-in over opt-out** — if one option preserves current behavior by default and adds a new flag/setting to enable the change, that option wins. Users opt into new behavior; legacy users see no surprise.
2. **Narrowest blast radius** — fewest files touched, fewest call sites changed, fewest moving parts. For data-loss bugs, this means: prefer "warn + require explicit override" over "silently change algorithm" over "rewrite subsystem".
3. **Backwards-compatible** — option that does NOT break any documented contract / API signature / file format wins over one that does.
4. **Reversible** — option that can be reverted with a single commit revert wins over one that requires a migration script to undo.
5. **Lowest cook-effort estimate** — Step 2b S/M/L: prefer S over M over L (smaller fixes ship sooner and validate the diagnosis cheaper).
6. **Tie-breaker** — option listed *first* in the issue body wins (authors typically lead with their preferred fix).

The rationale MUST be documented in the plan under a `## Decision` section with:
- The N options enumerated (matching the issue body)
- Which one was picked
- Which priority rule fired (1–6 above)
- One-sentence justification

For data-loss / data-corruption / security issues specifically: **rule 1 (opt-in over opt-out) is mandatory** — never auto-pick an option that changes data-handling behavior by default, even if other rules would prefer it. The fix may be "add a `--prune-snapshot` flag, off by default" rather than "always-snapshot before prune". This converts a user-input gate into an opt-in gate without losing the fix.

## Yolo invariants — NEVER bypassed

- `mergeable: MERGEABLE` (no merging conflicting branches)
- `statusCheckRollup` all green (no merging red CI)
- `mergeStateStatus ∈ {CLEAN, BEHIND}` (no merging DIRTY/BLOCKED/UNSTABLE)
- Skill-file gate (Step 4) — Skillmark validation runs unchanged
- `infoStatus: insufficient` items still defer
- Privacy + credential sanitization (Step 1c) unchanged

## Yolo failure handling (fail-conservative)

- t1k-code-reviewer agent timeout/crash → mark `merge-blocked: review-agent-failed`, defer, surface to human
- plan agent failure → mark issue `chain-blocked: plan-failed`, comment with error excerpt, defer
- cook agent failure on phase N → STOP chain, mark `chain-blocked: phase-{N}-failed`
- Blocker-resolution `gh` call failure → treat as "still blocked" (conservative), defer

## Step 6c — `--yolo` Active Merge

After Step 4d self-approve and Step 5b gate pass, `--yolo` merges the PR **itself** via `gh pr merge {n} --squash --delete-branch`. On PENDING CI: poll `statusCheckRollup` every 60s for up to 10 min, then merge if green or mark `merge-deferred: ci-timeout`. On `BEHIND`: fall back to `/t1k:babysit-pr`. On gh-merge failure: mark `merge-blocked: gh-merge-failed` and surface to human (no retry).

Triage merges in-session — does NOT delegate to babysit-pr — because babysit may never run if the user moves on / session ends. Direct merge is the only way to satisfy the Completion Contract.

Full per-PR completion loop, parallelism rules, cache-stability constraints: `references/completion-verification.md` § "Step 6c".