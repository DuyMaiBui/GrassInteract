---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Ctx-Budget Headroom Check — Pre-Emit Gate for Rule-File Sync-Backs

Use this when: any path in the sync-back diff matches `.claude/rules/*.md` in the receiver kit (create OR modify). This gate predicts whether the proposed PR would push the receiver's always-loaded rules corpus over the 15 000-token cap enforced by `validate-context-window-budget.cjs` (theonekit-release-action).

**Why this exists:** the rules corpus auto-loads into every consumer session. Hard-cap breaches block release. Three PRs (#264, #269, #274) were filed in May 2026 that each broke the cap on first push, costing one full CI round-trip + an unscheduled restructure each. Catching the overshoot at filing time costs ~5 seconds and avoids the round-trip.

## When to run

| Condition | Run gate? |
|---|---|
| Diff includes ANY `.claude/rules/*.md` create or modify | **YES — mandatory** |
| Diff includes only deletions of rule files | NO (deletions cannot overshoot) |
| Diff is purely skill/agent/script — no rule files | NO |
| Receiver kit has no `.claude/rules/` dir | NO (gate auto-skips) |

This is a **Step 2 gate row** under `references/pre-triage-review.md`.

## How to run

1. Ensure the receiver kit's `.claude/rules/` is materialised on disk (the GitHub-MCP path needs to download manifests to a temp dir first; the gh-CLI fork-flow path already has a clone).
2. Write each proposed new/modified rule body to a temp file.
3. Invoke the helper:

```
node .claude/scripts/sync-back-ctx-budget.cjs \
  --rules-dir <abs-path-to-receiver-rules-dir> \
  --change <target-relpath>=<new-content-file> \
  [--change <target-relpath2>=<new-content-file2> ...]
```

Defaults: `--cap-tokens 15000 --warn-ratio 0.9` (warn at 13 500). Both match the release-action gate's `DEFAULT_BUDGET` for SSOT consistency.

## What the helper returns

A single JSON object on stdout (per `rules/ai-driven-design.md` — helper emits facts, you reason). Schema:

| Field | Meaning |
|---|---|
| `currentTokens` | sum of `.md` files in receiver `.claude/rules/` today |
| `projectedTokens` | what the sum would be after this PR merges |
| `delta` | `projectedTokens − currentTokens` (can be negative for net-shrinking PRs) |
| `headroomTokens` | `capTokens − projectedTokens` (negative = over cap) |
| `status` | `pass` \| `warning` \| `overflow` |
| `recommendation` | `proceed` \| `warn-in-pr-body` \| `ask-user` |
| `perChange[]` | per-file breakdown: `op` (`create`/`modify`/`skip`), `currentTokens`, `newTokens`, `deltaTokens` |
| `topConsumers[]` | top 5 rule files in the projected state — useful when trimming |

## How to act on each status

### `pass` (recommendation: `proceed`)

Projected total is under 13 500. Continue with the PR. No PR-body annotation needed.

### `warning` (recommendation: `warn-in-pr-body`)

Projected total is 13 500–14 999 — under the hard cap but in the warn band. Continue with the PR but **append a note to the `## Pre-Triage Review` block's Adversarial self-review row:**

```
Cache-stability impact: projected rules corpus = ~14200 tok (warn band, cap 15000).
Headroom for next rule: ~800 tok. Future rules should use the terse-rule+docs/ pattern.
```

### `overflow` (recommendation: `ask-user`)

Projected total ≥ 15 000. **DO NOT silently push the PR — it will fail `validate-context-window-budget.cjs` on the release.** Invoke `AskUserQuestion` with these three options:

| Header | Option | Outcome |
|---|---|---|
| `Restructure` | **Restructure to terse-rule + docs/ pattern (Recommended)** | Move the bulk of the rule body to `docs/<rule-name>.md`; keep `rules/<rule-name>.md` as rule statement + 1-line why + "See: docs/<rule-name>.md for details." Then re-run the helper to confirm `status: pass`. |
| `Abort` | Abort the sync-back | Report `submitted: false, error: "ctx-budget-overflow"` back to parent. User can manually trim and re-invoke. |
| `Override` | Push anyway with `T1K_CTX_BUDGET_OVERRIDE=1` (risky) | Only viable if the receiver kit's README documents the override. CI gate will still fail unless the env var is set in the release workflow. |

If the user picks **Restructure**, follow the pattern below before re-running the helper.

## The terse-rule + docs/ pattern (SSOT)

Seven existing rules already use this pattern in `theonekit-core`. Read one as a template before drafting:

- `branch-discipline.md`
- `ci-cd-trigger-design.md`
- `commit-scope-policy.md`
- `module-registry-sync.md`
- `naming-convention.md`
- `kit-pr-workflow-boundary.md`
- `preview-first-batch.md`

**Shape of the terse rule file:**

```markdown
# <Rule Name>

<1-2 sentence statement of the rule>.

## Rule

<The actual constraint, in normative MUST/NEVER language. Keep under 10 lines.>

## Why

<1-2 sentences. The motivating incident + failure mode if violated.>

## How to apply

<3-5 numbered steps. Each step under 1 line. No examples.>

## See also

- `docs/<rule-name>.md` — full rationale, examples, edge cases, history
- Related rule files (links)
```

**Shape of the companion `docs/<rule-name>.md`:**

Everything that does NOT need to be in every consumer's auto-loaded context. Worked examples, incident postmortems, edge-case enumeration, comparison tables, alternative-approach analysis, version history. As long or short as needed — it loads only on demand.

After restructure, the rule file should be **< 1000 tokens** (preferably < 500). Re-run the helper and confirm `status: pass` before pushing.

## Failure-mode integration with `pre-triage-review.md`

Step 2 gate-pre-checks table adds this row:

```
| Rules ctx-budget headroom | Diff includes any `.claude/rules/*.md` create or modify | `node .claude/scripts/sync-back-ctx-budget.cjs ...` |
```

Failure modes table adds this row:

```
| Step 2 gate `Rules ctx-budget headroom` returns `overflow` and user picks Abort | Refuse — respond `submitted: false, error: "ctx-budget-overflow"` to parent |
```

## Cross-platform notes

- The script uses `node:fs` + `node:path` only — no shell, no `/dev/stdin`. Works on Linux, macOS, and Windows.
- Token estimation is the `Math.ceil(content.length / 4)` heuristic via the shared `hooks/lib/token-estimate.cjs`. Replace with a real tokenizer if the heuristic ever drifts ≥10% from cl100k_base counts; today the heuristic is within ±5%.
- The gate counts only **top-level** `.md` files in the rules dir (matches `readdirSync` non-recursive). Changes to subdirs of `rules/` (none exist today) are correctly marked `skip` with reason in `perChange[]`.

## Related

- `references/preflight-checks.md` — runs BEFORE this gate (MCP, repo access, staleness)
- `references/pre-triage-review.md` Step 2 — this gate is one row in that table
- `docs/sync-back-ctx-budget.md` — full pattern explanation + motivating incident
- `.claude/skills/t1k-doctor/scripts/check-context-budget.cjs` — session-time complement (warns the user if their installed rules corpus is over budget)
- `theonekit-release-action/scripts/validate-context-window-budget.cjs` — the release-time gate this check predicts
