---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Pre-Triage Review — MANDATORY before opening any sync-back PR

The goal: triage should **verify** your review, not **redo** it. Every sync-back PR MUST contain a `## Pre-Triage Review` block in the body with the fields below. This shifts review cost from triage (where it's repeated per run) to filing (where it happens once).

Anti-pattern (what we used to do): open a PR with `## Versions / ## Plan link / ## Rationale / ## Changed files`, then triage spends 5+ minutes per PR running `t1k-skill-creator validate`, checking naming-prefix conformance, looking for secrets, and assessing risk. Pre-triage review kills that round-trip.

## Review steps (run BEFORE opening the PR)

### Step 1 — Risk classification

Pick exactly ONE:

| Risk | When |
|---|---|
| `low` | Doc-only edit, single SKILL.md addition, no executor changes, no new gates, no behavior change |
| `medium` | Multi-file edits, new gate/hook, changes to existing logic, new reference doc + SKILL.md wiring |
| `high` | Cross-module impact, security-sensitive (secret handling, auth), behavior change to existing API, registry schema change |

If unsure between two levels, pick the higher one. Triage's `t1k-code-reviewer` runs the same risk classifier — agreement means triage can fast-path; disagreement means triage investigates.

### Step 2 — Gate pre-checks (run LOCALLY before push)

Run each applicable gate from `theonekit-release-action/scripts/` against the local workspace BEFORE pushing the branch. Record pass/fail/n-a per gate:

| Gate | When applicable | Command |
|---|---|---|
| `t1k-skill-creator validate {skill}` | Diff touches `.claude/skills/*/SKILL.md` or `.claude/skills/*/references/*.md` | `/t1k:skill-creator validate <skill-name>` |
| Naming-prefix conformance | Diff CREATES or RENAMES a skill dir or agent file | `node theonekit-release-action/scripts/lib-prefix.cjs expectedName ...` |
| `validate-modules-registry-sync` | Diff touches `.claude/modules/*/module.json` | regenerate `t1k-modules.json` and diff-check |
| Cross-ref validator | Diff renames a skill OR touches activation fragments | `node theonekit-release-action/scripts/check-skill-cross-refs.cjs .` |
| Rules ctx-budget headroom | Diff CREATES or MODIFIES any `.claude/rules/*.md` in receiver kit | `node .claude/scripts/sync-back-ctx-budget.cjs --rules-dir <receiver-rules-dir> --change <relpath>=<contentfile>` — see `references/ctx-budget-headroom.md` |
| Secret-scan | Always | `git diff --cached \| grep -iE "(api[_-]?key|password|token|bearer|secret)"` |
| Absolute-path scan | Always | `git diff --cached \| grep -E "/(home|Users)/[a-z]+"` |
| Empty-file scan | Always | `git diff --cached --stat \| awk '$3==0'` |

If ANY gate FAILS: do NOT open the PR. Surface the failure to parent so the fix can be applied first.

If a gate WARNS: include the warning in the PR body's "Pre-Triage Review" block so triage sees it.

### Step 3 — Generic-rationale check

Sync-back PRs MUST argue the change is generic to ALL consumers of the kit/module, not specific to the originating project. Run this check:

| Question | If YES → generic | If NO → project-specific (REFUSE) |
|---|---|---|
| Does the change reference a project-specific path, name, or constant? | (none referenced) | `path/to/their/project` |
| Does the change embed a project-specific assumption (e.g., "for our shop offering")? | (no embedded assumptions) | `for our shop offering` |
| Does the change improve a pattern that applies to all uses of the skill? | yes — name the broader applicability | (only applies to one project) |

If the check fails, REFUSE to open the PR and respond to parent: `submitted: false, error: "project-specific-change"`.

### Step 4 — Adversarial self-review

For each non-trivial change, try to find a counter-example or edge case:

| Change type | Counter-example to try |
|---|---|
| New rule / pattern in skill body | Is there a known case where this pattern would be wrong? |
| New gate (CI script) | Is there a legitimate-looking diff that this gate would reject as a false positive? |
| Renamed skill / agent | Are there activation fragments, routing JSON, or docs that reference the old name? |
| New keyword in activation fragment | Is there an existing skill that would now over-trigger on this keyword? |
| Cache-stability — change to skill body | Does it interpolate variable content (timestamps, counts, SHAs) that would bust prompt cache? |

Record the result: `(yes — found {N} edge cases, addressed by ...)` OR `(no — pattern is general)`.

### Step 5 — Recommended triage disposition

Pick exactly ONE — this is what triage adopts as the default action:

| Disposition | When to pick |
|---|---|
| `auto-merge-eligible` | Risk = low, all gates pass, generic-rationale clear, no warnings, no behavior change |
| `merit-pipeline-eligible` | Risk ≤ medium, all gates pass, but needs t1k-code-reviewer's confirmation (e.g., new pattern not previously seen) |
| `needs-human-review-for-{reason}` | Risk = high OR a gate warned OR cache-stability impact OR new schema field |

If you pick `auto-merge-eligible`, triage's Step 5b strict gate is expected to pass without further investigation. If you're wrong (gate fails downstream), the lesson goes into your next PR's pre-triage review as a stricter Step 4 check.

## Output — append this block to every PR body

Add as a new top-level section AFTER `## Changed files`:

```markdown
## Pre-Triage Review

**Risk classification:** `{low | medium | high}`

**Gate pre-checks:**
- [{x | warn | n-a}] `t1k-skill-creator validate {skill}` — `{pass | <warning summary>}`
- [{x | n-a}] Naming-prefix conformance — `{expected-name → actual-name}` — `{pass | <mismatch>}`
- [{x | n-a}] `validate-modules-registry-sync` — `{pass | <diff lines>}`
- [{x | n-a}] Cross-ref validator — `{N refs OK | <broken refs>}`
- [x] Secret-scan — `{clean | <redacted hit>}`
- [x] Absolute-path scan — `{clean | <relative path or comment>}`

**Generic-rationale check:**
- Applies to: `{all-consumers-of-kit | all-consumers-of-module | only-this-module}`
- Reasoning: {1 sentence}
- Anti-pattern verification: this is NOT a project-specific fix masquerading as generic — {evidence: ...}

**Adversarial self-review:**
- Counter-example tried: {description, or "n/a — trivial diff"}
- Cache-stability impact: {none | new variable content — mitigated by ...}

**Recommended triage disposition:** `{auto-merge-eligible | merit-pipeline-eligible | needs-human-review-for-<reason>}`

**Rationale:** {1-2 sentences — why this disposition fits, cite gate results}
```

## Failure modes (when sync-back MUST refuse to open the PR)

| Failure | Action |
|---|---|
| Any gate in Step 2 fails (not "warn") | Refuse — respond `submitted: false, error: "gate-failed: <name>"` to parent |
| Step 3 generic-rationale check fails | Refuse — `submitted: false, error: "project-specific-change"` |
| Risk = high but pre-triage review block would be incomplete | Refuse — `submitted: false, error: "high-risk-needs-full-review"` |
| Cache-stability check identifies un-mitigated variable content in a skill body | Refuse — fix the cache-busting content first, OR isolate the variable part to a non-body location |
| Rules ctx-budget headroom gate returns `overflow` AND user picks Abort | Refuse — `submitted: false, error: "ctx-budget-overflow"` (see `references/ctx-budget-headroom.md` for restructure-vs-abort-vs-override decision) |

## Budget

Pre-triage review should take **5–10 minutes** for a low-risk doc edit, **15–25 minutes** for a medium-risk multi-file change. Higher than 30 minutes → the change is probably high-risk; refile as multiple smaller PRs.

## Why this saves time net-net

- Triage on 5 PRs previously: ~25 minutes of review (filing was ~10 min/PR).
- Triage on 5 well-pre-reviewed PRs: ~8 minutes of verification (filing was ~20 min/PR).
- Filing cost: +10 min/PR × 5 = +50 min.
- Triage cost: −17 min × N triage runs.
- Break-even at **3 triage runs per PR** (typical PR is touched 3–7 times before merge).

The math also tilts further toward filing-time investment because **a wrongly-merged PR has long-tail cleanup cost** — broken main, follow-up PRs, retroactive rules. Pre-triage review surfaces those risks before merge.

## Related

- `references/open-pr.md` — the body template must include the `## Pre-Triage Review` block
- `references/preflight-checks.md` — Step 2 gates partially overlap (this file is more thorough)
- `references/sub-agent-invocation.md` — sub-agent contract for the writeback
- `skills/t1k-triage/references/auto-merge-gate.md` — Step 5b strict gate that consumes the `auto-merge-eligible` disposition
- `skills/t1k-triage/references/yolo-merit-pipeline.md` — consumes the `merit-pipeline-eligible` disposition
