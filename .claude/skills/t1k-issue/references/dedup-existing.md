---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Dedup — Check for Existing Issues Before Filing

Use this when: before any `gh issue create` call, both manual and auto-mode. Dedup is MANDATORY — never skip it.

## Why

Duplicate issues fragment discussion, inflate counts, and waste maintainer time. A single issue with a "new occurrence" comment is far more useful.

## Search strategy

**MCP (preferred):**
```
search_issues(query="in:title {skill-name}", owner="{owner}", repo="{repo}")
```

**gh CLI fallback:**
```bash
gh issue list \
  --repo {owner}/{repo} \
  --search "in:title {skill-name}" \
  --state open \
  --json number,title,url
```

## Match criteria

An issue is a duplicate if:
- Title matches pattern `fix({kit}):` or `fix({kit}/{module}):` AND
- Title contains the affected skill name

Case-insensitive match is acceptable.

## If a duplicate is found — add a comment instead

**MCP:**
```
add_issue_comment(owner, repo, issue_number, body)
```

**gh CLI:**
```bash
gh issue comment {number} --repo {owner}/{repo} --body "..."
```

**Comment body (new occurrence):**
```markdown
**New occurrence** — {ISO timestamp}

**Fingerprint:** `{fingerprint}` (if from auto-mode)
**Session context:** {short description of what triggered this recurrence}
**Evidence:**
```
{sanitized logs}
```
```

## If no duplicate found

Proceed to create a new issue via `references/file-from-marker.md` or `references/file-manual.md`.

## Related-search (MANDATORY — always run, after dedup, before filing)

Even when no exact duplicate exists, search for OPEN issues + PRs that touch the SAME files as the affected skill/agent. The results auto-populate the issue body's `Coupling / dependencies` → `Related:` / `Related PRs:` lines (no more manual `none found`).

### Step A — resolve affected paths

From the affected skill/agent name, compute the file paths the bug is about:

| Component type | Path glob(s) |
|---|---|
| Skill | `.claude/skills/{skill-dir}/**` |
| Agent | `.claude/agents/{agent-name}.md` |
| Hook | `.claude/hooks/{hook-name}.cjs` |
| Routing/config fragment | `.claude/{fragment-name}.json` |

If the affected component is unclear, fall back to the kit/module name as a body-text search term only.

### Step B — search OPEN PRs touching those paths

**gh CLI:**
```bash
for path in {paths[@]}; do
  gh pr list --repo {owner}/{repo} --state open \
    --search "$path" \
    --json number,title,url,headRefName \
    --jq '.[] | "\(.number) \(.title) \(.url)"'
done | sort -u
```

**MCP (preferred):** `list_pull_requests(owner, repo, state="open")` then client-side filter PRs whose `files[].filename` matches any of the paths. (`pull_request_read` returns file lists.)

### Step C — search OPEN issues mentioning the skill/agent name in title OR body

**gh CLI:**
```bash
gh issue list --repo {owner}/{repo} --state open \
  --search "{skill-name OR agent-name}" \
  --json number,title,url \
  --jq '.[] | "\(.number) \(.title) \(.url)"'
```

Exclude any number already returned by the exact-dedup search above.

### Step D — populate the issue body's Coupling block

The body templates in `file-from-marker.md` and `file-manual.md` already have:

```
**Coupling / dependencies:**
- Related: `#NN` ({1-line summary}) — or "none found"
- Related PRs: `#NN`, `#NN` — or "none"
```

Replace `none found` / `none` with the actual search results:

```
- Related: #NN ({issue title, first 60 chars}), #MM (...) — or "none found"
- Related PRs: #NN ({PR title, first 60 chars}), #MM (...) — or "none"
```

Cap at 5 entries per line (sort by recency). If >5, list the 5 most recent and append `(+N more, see {search-url})`.

### Step E — if duplicate WAS found (commented instead of creating)

Add the related list to the dedup comment body too, so the maintainer sees parallel work even on a recurrence:

```markdown
**New occurrence** — {ISO timestamp}
...
**Related work in flight:**
- PRs touching same files: #NN, #MM (or "none")
- Open issues mentioning {skill-name}: #NN, #MM (or "none")
```

### Failure handling

If both Step B and Step C fail (GitHub down, rate limit), fill the body with `Related: search-unavailable` and `Related PRs: search-unavailable` — do NOT block the filing. Filing succeeds; missing related-links is a minor concern.

## Local dedup cache (manual AND auto-mode)

After filing ANY issue (manual or auto), write to the shared dedup cache:
```js
// from .claude/hooks/lib/kit-error-dedup.cjs
markSubmitted(fingerprint, issueUrl)
```
This prevents re-filing within `autoIssueSubmission.dedupeTTLDays` days even if the GitHub search is slow.

For **auto-mode**: use the fingerprint from the queue entry (already computed by `lesson-collector.cjs`).

For **manual mode**: compute a fresh fingerprint using `fingerprint({ tool: 'manual-issue', cmd: '', stderrHead: issueTitle.slice(0,100) }, { reason: label, originKit: kit })`. See `references/file-manual.md` § "After filing — populate dedup cache" for the full invocation.

**Why this matters:** Manual and auto filings share the same cache file (`~/.claude/.lesson-fingerprints.json`). A manual filing that populates the cache prevents the auto-pipeline from re-filing the same bug in a future session. This integration closes the manual-vs-auto dedup gap (issue #164).
