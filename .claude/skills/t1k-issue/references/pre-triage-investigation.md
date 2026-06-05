---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Pre-Triage Investigation — MANDATORY before filing any issue

The goal: triage should **verify** your investigation, not **redo** it. Every issue you file MUST contain a `### Pre-Triage Investigation` block with the fields below. Without it, the issue is low-signal and will burn triage cycles.

Anti-pattern (what we used to do): file a one-paragraph "X is broken, here are some logs," then have triage spend 5 minutes per issue grepping the kit to check whether it's a real bug, a dup, or already-fixed-in-main. Pre-triage investigation kills that round-trip.

## Investigation steps (run BEFORE drafting the body)

### Step 1 — Verify the bug exists in current `main`

| Check | How |
|---|---|
| `git log --oneline main..HEAD -- <referenced file>` returns nothing | The bug claim references files still on main |
| Open the referenced file at current HEAD and read the relevant lines | Confirm the pattern/anti-pattern/missing-thing is actually present |
| Run the reproduction command (if any) against current `main` | Confirm the bad behavior still occurs |

If the bug does NOT reproduce on current `main`:
- Verification status = `cant-repro`
- Cite the verification command + output excerpt
- Recommend triage disposition = `close-cant-repro`
- **Still file the issue** — the writeback is what tells triage to close (don't silently swallow)

### Step 2 — Dedup against open issues

| Check | How |
|---|---|
| `gh search issues --repo <repo> --state open "<keyword from bug>"` | Direct keyword match |
| `gh search issues --repo <repo> --state open "in:title <skill-name>"` | Same skill, different wording |
| `gh search issues --repo <repo> --state open "label:skill-bug"` (last 20) | Recent skill-bug filings |

If a duplicate exists:
- Verification status = `dup-of-#NN`
- Add a comment to the survivor instead of creating a new issue (the existing dedup path; this just makes the reasoning visible)

### Step 3 — Coupling check

For each `#NN` mentioned in the body or related-files commit history:
- Run `gh issue view {n} --json state,title,labels` — is it open? labeled `blocked` / `wontfix`?
- If yes: record under `Blocked by:` or `Related:`

For each commit SHA on the referenced files in the last 90 days:
- `gh search prs --repo <repo> "<SHA>"` — was there a PR that touched this surface?
- If yes: link the PR under `Related PRs:`

### Step 4 — Classify (10-state taxonomy from `t1k-triage`)

Pick exactly ONE of:

| State | Meaning |
|---|---|
| `bug-fixable-quick` | Root cause clear, fix < 50 LOC, no design unknowns |
| `bug-fixable-cook` | Real bug, scope > 50 LOC, needs plan→impl→test |
| `bug-needs-design` | Root cause requires cross-component decision |
| `docs-fixable-quick` | Docs/cross-ref gap, scope < 50 LOC mechanical fix |
| `docs-fixable-cook` | Multi-file doc edits needing verification |
| `docs-needs-design` | Gap requires a design decision (e.g., "ship 8 agents or remove mandate?") |
| `tracker-survivable` | Tracker for a future feature/gate, no existing replacement |
| `tracker-redundant` | Tracker for something already implemented elsewhere — name the replacement |
| `enhancement` | Not a bug; new capability or improvement |
| `cant-repro` / `dup` / `stale` | Set by Step 1/2 above |

### Step 5 — Fix sketch (REQUIRED for fixable-* classifications)

3 bullets, each ≤ 1 sentence:
- What file(s) need to change
- What the change is (specific enough to estimate)
- What test/verification proves it worked

If you can't write a fix sketch in 3 bullets, the classification is wrong — it's probably `bug-needs-design` or `docs-needs-design`, not `*-fixable`.

### Step 6 — Recommended disposition

Pick exactly ONE — this is what triage adopts as the default action:

| Disposition | When to pick |
|---|---|
| `close-cant-repro` | Verification status = `cant-repro` |
| `close-redundant` | Classification = `tracker-redundant` |
| `close-dup` | Verification status = `dup-of-#NN` |
| `queue-for-cook` | Classification = `*-fixable-*` |
| `comment-and-leave-open` | Classification = `*-needs-design` OR `tracker-survivable` (with open design questions) |
| `merge-as-is` | Only valid for `enhancement` with a ready PR linked |

## Output — append this block to every issue body

Add immediately AFTER the existing `### Environment` and `### Description` sections, BEFORE `### Evidence`:

```markdown
### Pre-Triage Investigation

**Verification status:** {confirmed-in-main | stale | cant-repro | dup-of-#NN}
- HEAD checked: `{SHA-short}` on `{date}`
- File-line evidence: `{path:line}` — `{1-line excerpt}` (or "N/A — claim refers to absence, not presence")
- Repro check: `{command}` → `{outcome}` (or "N/A — design issue, not behavioral")

**Classification:** `{state from Step 4}`

**Coupling / dependencies:**
- Related: `#NN` ({1-line summary}), `#NN` ({1-line summary})  — or "none found"
- Blocked by: `#NN` (`{blocker title}`)  — or "none"
- Related PRs: `#NN`, `#NN`  — or "none"

**Fix sketch:**
- `{path}` — {what to change}
- {test/verification command}
- {expected post-fix behavior}

(omit Fix sketch block if classification is `*-needs-design` / `tracker-*` / `cant-repro` / `dup` / `stale`)

**Recommended triage disposition:** `{disposition from Step 6}`

**Rationale (1-2 sentences):** {why this disposition fits the classification — cite Step 1/2 evidence}
```

## Failure modes (when pre-triage investigation MUST refuse to file)

| Failure | Action |
|---|---|
| Cannot fetch current `main` (network, auth fail) | Refuse — respond `submitted: false, error: "cannot-verify-main"` to parent |
| Bug claim cannot be located in any file in the repo | Refuse — ask user for the affected file path |
| Classification = `*-fixable-*` but cannot produce a 3-bullet fix sketch | Downgrade classification to `*-needs-design`, document why |
| Step 1 shows `cant-repro` AND Step 2 shows no dup, BUT user explicitly asked to file | File with disposition = `close-cant-repro` so the resolution path is visible (let triage close it) |

## Budget

Pre-triage investigation should take **3–5 minutes** for a typical bug, **5–10 minutes** for a complex one. If you're > 15 minutes, the issue is probably `bug-needs-design` and you're trying to fully solve it inline — stop, file with the partial findings, mark `comment-and-leave-open`.

## Why this saves time net-net

- Triage on 17 issues previously: ~80 minutes of investigation (filing was ~5 min/issue).
- Triage on 17 well-pre-triaged issues: ~15 minutes of verification (filing was ~12 min/issue).
- Filing cost: +7 min/issue × 17 = +120 min.
- Triage cost: −65 min × N triage runs.
- Break-even at **2 triage runs per issue** (typical issue gets triaged 3–5 times before resolution).

## Related

- `references/file-from-marker.md` — sub-agent path, must include the pre-triage block
- `references/file-manual.md` — interactive path, must include the pre-triage block
- `references/dedup-existing.md` — Step 2 implementation detail
- `skills/t1k-triage/references/yolo-issue-disposition-sweep.md` — the consumer of these blocks (classification taxonomy matches)
- `skills/t1k-triage/references/per-item-patterns.md` § "Adversarial evidence requirement" — same evidence standard
