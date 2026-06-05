---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Pre-flight Checks

Use this when: starting any sync-back operation after consumer-guard passes. These checks are MANDATORY before any file write.

## 1. GitHub MCP connected?

If not → ERROR: `"Connect GitHub MCP: claude mcp add github"`

## 2. Resolve repo URL per file

For each changed file, determine target repository from in-file origin metadata (in priority order):

1. YAML frontmatter `repository` field (e.g., `repository: "The1Studio/theonekit-unity"`) → use directly
2. `.t1k-resolved-config.json` → `routing` for pre-merged config
3. Last fallback: read ALL `t1k-config-*.json` → match `kitName` against file's `origin` → `repos.primary`

## 3. Detect install location

- Path starts with `$HOME/.claude/` → global install
- Path starts with `$CWD/.claude/` → project install

Adjust all subsequent path references accordingly.

## 4. Verify repo access

Call `get_file_contents(owner, repo, "/")` on repo root. If 404/403 → ask user to confirm repo access before proceeding.

## 5. Staleness check (MANDATORY — v1.2.0)

For EACH target file the sync will write:

1. `get_file_contents(owner, repo, path, ref="main")` → fetch current remote content + SHA
2. `list_commits(owner, repo, path=target_path, sha="main")` → recent commits touching this file
3. If remote SHA differs from the base this sync started from, OR if newer commits exist since the last known sync timestamp → **BLOCK and warn:**

```
⚠️ {N} commits on main have touched {path} since your last sync. Remote file has diverged.
```

List the offending commits (hash + message). Offer three options:
- **(a) abort** — reconcile manually
- **(b) overwrite** — requires `--force` flag
- **(c) merge** — pull remote content, re-apply local diff, then push

**Never silently push a stale branch.** A `CONFLICTING` PR must never be produced.

**Why this exists:** Prior to v1.2.0, the skill produced unmergeable PRs against stale bases (see The1Studio/theonekit-core#7 incident, 2026-04-09).

## 6. Related-PR search (MANDATORY)

Staleness (Step 5) catches main-branch divergence. Step 6 catches the OTHER collision class: a different in-flight PR touching the same files. The results auto-populate the new PR body's `## Related` section (`references/open-pr.md`).

### Step A — search OPEN PRs touching any of the sync paths

For the full set of files this sync will write:

**MCP (preferred):**
```
list_pull_requests(owner, repo, state="open")
  → for each PR: pull_request_read(action="get_files", pullNumber=N)
  → keep PRs whose files[].filename ∩ {sync paths} ≠ ∅
```

**gh CLI fallback:**
```bash
for path in {sync-paths[@]}; do
  gh pr list --repo {owner}/{repo} --state open \
    --search "$path" \
    --json number,title,url,headRefName,author \
    --jq '.[] | "\(.number)|\(.title)|\(.url)|\(.author.login)"'
done | sort -u
```

### Step B — categorize matches

Compare the file-line ranges of each match against the lines this sync will modify:

| Category | When | Action |
|---|---|---|
| **No overlap** | other PR touches the same file but different lines | List in `## Related` body section. Proceed. |
| **Line overlap** | other PR modifies the same lines | **WARN** the parent agent — likely a merge conflict on review. List in `## Related` AND ask `AskUserQuestion` (interactive) or set `relatedPRs[].conflictRisk: true` (background sub-agent). Proceed unless `--force` is omitted AND user picks abort. |
| **Same author, recent** | matched PR was authored by the same gh login within last 24h | Likely a continuation — suggest extending the existing PR instead of opening a new one (per `feedback_stacking_on_unmerged_pr.md`). `AskUserQuestion` (interactive only). |

### Step C — emit `relatedPRs[]` for `references/open-pr.md`

Output a JSON-like list (in-memory) that the open-pr step embeds in the body:

```
relatedPRs = [
  { number: 123, title: "...", url: "...", author: "...", overlap: "file|line", conflictRisk: false },
  ...
]
```

Cap at 5 entries (sort by recency). If >5, list the 5 most recent and append `(+N more)`.

### Failure handling

If the search fails (GitHub down, rate limit, no MCP), emit `relatedPRs = []` with note `search-unavailable` so the PR body shows `## Related — search unavailable at sync time`. Do NOT block sync — staleness (Step 5) already covers the main-branch collision case.

**Why this exists:** Staleness (Step 5) only catches `main` divergence. Parallel in-flight PRs from other contributors (or your own forgotten branch) silently produce merge conflicts on review, costing a round-trip. Step 6 surfaces them at filing time so the new PR body links them — reviewers can coordinate or rebase instead of discovering the collision mid-merge.
