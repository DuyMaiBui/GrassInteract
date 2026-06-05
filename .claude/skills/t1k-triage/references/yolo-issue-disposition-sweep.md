---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Step 6d — `--yolo` Issue Disposition Sweep

The lightweight middle path between "do nothing" and a full `/t1k:plan → /t1k:cook` chain. Runs **before** Step 6b plan-cook escalation when the issue queue is too large to chain-cook safely under `preview-first-batch.md`.

## When this step fires

Trigger conditions (ALL must be true):

1. `--yolo` mode is active
2. After Steps 6a–6c run, **≥ 3 issues remain queued for `decision=solve` plan→cook chain**
3. Estimated combined plan→cook wall-clock > 30 min (use Step 2b effort heuristics: 1× `effort: large` OR 3× `effort: medium` ⇒ > 30 min)

When triggered, the sweep replaces wholesale deferral. Items that survive the sweep with `disposition: queue-for-cook` are then handed to Step 6b plan→cook chain (or surfaced for user prioritization if the chain is still too large).

## Anti-pattern this fixes

Pre-this-step yolo would emit reports like:

> "16 issues require multi-phase work. Preview-first mandate fires → deferred wholesale for user prioritization."

That violates yolo doctrine: **investigate before deciding, decide before deferring.** The sweep does the investigation phase even when the implementation phase has to wait.

## Procedure

### 1. Partition

Split the remaining issue set into chunks of **4–6 issues each**. Cap concurrency at **4 chunks** (= 4 parallel background subagents) per `parallelize-batch-work.md`. Group by category if obvious (CLI bugs, skill bugs, docs gaps, trackers) so each subagent has a coherent context.

### 2. Dispatch parallel background subagents

**Pre-flight (fork-context gate).** Before dispatching, verify `Agent` is callable: `ToolSearch(query="select:Agent", max_results=1)`. If `Agent` is absent from BOTH active scope AND the deferred-tools listing, this skill is running in a forked sub-context — STOP and report: `yolo-disposition-sweep cannot dispatch subagents from this fork-context. Re-invoke /t1k:triage --yolo from the main session.` Do NOT silently fall back to serial in-context investigation — silent fallback hides the contract violation (parallelism + isolation) the same way #259 hid orphan team creation in `t1k-team`.

Use `Agent` tool with `subagent_type: "t1k-researcher"` (the registered specialist for evidence-based multi-source classification — perfect fit for the 8-state taxonomy + repo-state verification per issue) and `run_in_background: true`. Fallback to `general-purpose` ONLY if `t1k-researcher` is not registered in this project's `.claude/agents/`. One subagent per chunk. Each subagent's prompt MUST include:

- The full issue list (numbers + repo)
- The 8-state classification taxonomy (below)
- The strict output format (per-issue markdown block)
- Repo paths for local file reads
- "Be evidence-based — if you can't tell, say so. Don't guess." (anti-hallucination guardrail)

Subagent procedure per issue:

1. `gh issue view <num> --repo <repo> --json title,body,createdAt,updatedAt,labels,comments,state`
2. Stale check: `updatedAt` > 90 days ago AND code referenced in body has changed/been removed
3. Dup check: scan labels + comments for `duplicate of #N` markers; cross-link via `gh search issues`
4. Repro check: if issue claims a bug with concrete repro, open the referenced files and verify the bug still holds in current `main`
5. Classify (see taxonomy)
6. Emit per-issue block

### 3. Classification taxonomy (8 states)

| State | Meaning | Default disposition |
|---|---|---|
| `bug-fixable-quick` | Root cause clear, fix < 50 LOC, no design unknowns | `queue-for-cook` (single-issue cook, low priority) |
| `bug-fixable-cook` | Real bug, needs proper plan→impl→test | `queue-for-cook` (high priority — surface in report) |
| `bug-needs-design` | Root cause requires cross-component decision | `comment-and-leave-open` (post findings) |
| `docs-fixable` | Docs/cross-ref gap, scope is clear | `queue-for-cook` (low effort) |
| `docs-needs-design` | Gap requires design decision (e.g., "ship 8 agents or remove the mandate?") | `comment-and-leave-open` |
| `stale` | > 90d untouched AND obsolete (code changed underneath) | `close-stale` |
| `dup` | Duplicate of another OPEN issue with named survivor | `close-superseded` |
| `cant-repro` | Claim doesn't hold on current `main` (verified by reading code) | `close-cant-repro` |
| `tracker-redundant` | Tracker for a gate/feature already implemented elsewhere | `close-redundant` |
| `tracker-survivable` | Tracker still relevant, no existing replacement | `comment-and-leave-open` (acknowledge tracking) |

### 4. Conservative close criteria (DEFAULT — `--yolo` does not override)

The sweep MAY close issues only when ONE of these three conditions is met:

- **`stale`** — `updatedAt` ≥ 90 days ago AND a grep proves the code-under-discussion no longer exists at the path the issue cites
- **`dup`** — explicit "duplicate of #N" comment OR identical title + repro + body excerpt matching another OPEN issue; survivor MUST be named
- **`cant-repro`** — current `main` HEAD has been verified to NOT exhibit the claimed behavior; verification command/excerpt MUST be cited
- **`tracker-redundant`** — an existing gate/feature/script provably covers the tracker's intent; replacement MUST be named with path

**Never close:**
- `bug-needs-design` / `docs-needs-design` — these need human design input, not closure
- `tracker-survivable` — still tracking real future work
- `bug-fixable-*` / `docs-fixable` — fixable means we owe the user a fix, not a close

If a sweep wants to close outside these four states, it MUST escalate to `AskUserQuestion` (via the main agent) before acting. Default disposition is conservative: **when in doubt, comment-and-leave-open.**

### 5. Aggregate + execute

After all subagents return, the main agent aggregates reports into a single disposition table:

```
| Repo | # | Classification | Disposition | Evidence |
|---|---|---|---|---|
| ... | ... | ... | ... | ... |
```

Execute in order:

1. **Closures** (`close-stale` / `close-superseded` / `close-cant-repro` / `close-redundant`) — sequential `gh issue close --comment "..."` calls. Comment template:
   ```
   Closing per yolo triage on {date}: {classification}.

   Evidence: {citation}

   Re-open if context changed.
   ```
2. **Comments** (`comment-and-leave-open`) — sequential `gh issue comment` calls with the subagent's findings paragraph.
3. **Queue-for-cook** — record in the Step 5 report under `## Cook Queue (post-sweep)` with per-item:
   - issue ref
   - effort estimate
   - fix sketch (3-bullet)
   - priority hint

### 6. Step 5 report integration

The sweep adds two new sections to the triage report:

- **`## Disposition Sweep (Step 6d)`** — full classification table for every swept issue
- **`## Cook Queue (post-sweep)`** — issues to user-prioritize for follow-up cook sessions

When the sweep runs, the legacy "Deferred Issues" section is REPLACED by these two. Wholesale deferral is no longer a valid outcome in yolo mode.

### 7. Failure handling (fail-conservative)

- Subagent timeout/crash → mark its chunk `sweep-failed: subagent-crashed`, defer the chunk's issues to user (do NOT close)
- `gh issue close` API failure → mark `close-failed: gh-error`, retain in defer pool
- Subagent emits a classification outside the 10-state taxonomy → reject (treat as `comment-and-leave-open`)
- Evidence missing for a close-disposition → downgrade to `comment-and-leave-open` (per "no verdict without evidence" rule from `per-item-patterns.md`)

## Performance + cost

Sweep on 15 issues with 4 parallel subagents typically completes in 8–12 min wall time vs. 60+ min for sequential investigation. Per `parallelize-batch-work.md`, parallel is the right default at this scale.

## Cache stability

Per `agent-security-boilerplate.md`: subagent prompts MUST have constant shape (no timestamp/SHA interpolation). The per-chunk issue list is variable but enumerated; that's fine. Disposition table output is variable-row but constant-column.

## Related

- `references/yolo-decision-matrix.md` § "Decision matrix" — Step 6d slots between rows "`solve` skipped because `effort: large`" and the plan→cook chain
- `rules/parallelize-batch-work.md` — concurrency = 4, chunk size = 4–6
- `rules/preview-first-batch.md` — sweep IS the preview-first response (preview = classification reports surfaced before bulk actions)
- `rules/always-ask-on-unresolved.md` — closures outside the four canonical states require `AskUserQuestion`
