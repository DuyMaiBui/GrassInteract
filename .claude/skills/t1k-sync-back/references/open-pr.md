---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Open PR

Use this when: creating the branch, pushing files, and opening the pull request in the target kit repo (after routing and paths have been resolved).

## Main flow (has push access)

```
1. create_branch(owner, repo, branch)
   → If branch exists: append YYMMDD suffix
     e.g., t1k-sync/core/t1k-cook-260408
2. push_files(owner, repo, branch, files=[{path, content}...], message)
   → All files in ONE atomic commit
3. create_pull_request(owner, repo, title, head=branch, base="main")
```

## Fork flow (no push access — 403 on create_branch)

```
1. fork_repository(owner, repo)
   → If fork exists: reuse it
2. create_branch on FORK (fork-owner, repo, branch)
3. push_files to FORK branch
4. create_pull_request(owner=original, repo, head="fork-owner:branch", base="main")
```

## Dry-run flow (--dry-run)

```
1. For each target file: get_file_contents(owner, repo, path) → current remote content
2. Diff local modified file vs remote current
3. Display diff to user
4. Stop — no branch or PR created
```

## PR title and body format

**Title:** `fix({module}): update {skill}` or `fix({kit}): update {name}` for kit-wide

**Body (REQUIRED sections — do NOT open a PR missing any of these):**

```markdown
## Versions (at sync time)

- **Kit**: `{kit-name}` v`{kit-version}` (from consumer `.claude/metadata.json`)
- **Module**: `{module-name}` v`{module-version}` (or "kit-wide")
- **T1K CLI**: `{cli-version}`
- **Platform**: `{os}` `{os-release}`

## Plan link

`{relative-path-to-plan}` — or `ad-hoc` if no plan doc.

## Rationale (why this change is generic)

{1-3 sentences — must argue the change applies to ALL consumers of this kit/module,
not just the originating project. If the change is project-specific, sync-back must refuse.}

## Changed files

- `{path 1}` — {one-line description of change}
- `{path 2}` — {one-line description of change}

## Related

(REQUIRED — populated from `references/preflight-checks.md` Step 6 `relatedPRs[]`. Lists open PRs that touch any of the same files. Empty list → render `- _none — no open PRs touch these files at sync time_`. Search failed → render `- _search-unavailable at sync time_`.)

- #{number} ({title, first 60 chars}) — author `@{login}` — overlap: `{file | line}`{, ⚠️ conflict-risk if line-overlap}
- ...

## Pre-Triage Review

(REQUIRED — produced by `references/pre-triage-review.md`. Triage's `t1k-code-reviewer`
adopts the recommended disposition unless gate state has changed since filing.)

**Risk classification:** `{low | medium | high}`

**Gate pre-checks** (run locally before push):
- [{x | warn | n-a}] `t1k-skill-creator validate {skill}` — `{pass | <warning summary>}`
- [{x | n-a}] Naming-prefix conformance — `{expected-name → actual-name}` — `{pass | <mismatch>}`
- [{x | n-a}] `validate-modules-registry-sync` — `{pass | <diff lines>}`
- [{x | n-a}] Cross-ref validator — `{N refs OK | <broken refs>}`
- [x] Secret-scan — `{clean | <redacted hit>}`
- [x] Absolute-path scan — `{clean | <relative path or comment>}`

**Generic-rationale check:**
- Applies to: `{all-consumers-of-kit | all-consumers-of-module | only-this-module}`
- Reasoning: {1 sentence}
- Anti-pattern verification: NOT a project-specific fix masquerading as generic — {evidence}

**Adversarial self-review:**
- Counter-example tried: {description, or "n/a — trivial diff"}
- Cache-stability impact: {none | new variable content — mitigated by ...}

**Recommended triage disposition:** `{auto-merge-eligible | merit-pipeline-eligible | needs-human-review-for-<reason>}`

**Rationale:** {1-2 sentences — why this disposition fits, cite gate results}
```

Missing any section → REFUSE to open the PR and ask the parent to fill it in. The `## Pre-Triage Review` block is mandatory; if any Step 2 gate failed in `references/pre-triage-review.md`, refuse to open the PR until the gate passes locally.

## Post-PR rules

- Sync-back ONLY creates the PR. Review, triage, and merge happen in the kit repo.
- Do NOT automerge from the consumer project.
- End every invocation by reporting the PR URL and noting "review + merge in the kit repo."
- Use `/t1k:triage` from inside the kit repo to process the sync-back PR backlog.
