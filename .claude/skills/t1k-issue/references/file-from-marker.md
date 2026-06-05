---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# File Issue from Skill-Bug Marker

Use this when: a `type=skill-bug` entry arrives from `lesson-queue-processor.cjs` via a system-reminder. The parent spawns this sub-agent via the `Agent` tool with `run_in_background: true`.

## Input (from queue entry)

The queue entry provides: `kit`, `skill`, `bug` (description), `evidence` (logs/context), `fingerprint`.

```
kit=<kit-name>  skill=<skill-name>  bug="<description>"  evidence="<logs>"
```

## Workflow

1. **Pre-flight** — confirm GitHub MCP or `gh auth status`. If neither: respond to parent with `submitted: false, error: "no-gh-auth"`.
2. **Resolve repo** — read `repository` from target skill's YAML frontmatter. Fallback: `.t1k-resolved-config.json` → `t1k-config-*.json` → `repos.primary`.
3. **Pre-triage investigation (MANDATORY)** — run the full procedure in `references/pre-triage-investigation.md`: verify on current `main`, dedup search, coupling check, classify, fix sketch, recommended disposition. Produces the `### Pre-Triage Investigation` body block.
4. **Dedup** — search existing open issues (Step 3 above already did this, but re-check post-investigation). See `references/dedup-existing.md`. If duplicate found: comment instead of creating.
5. **Create issue** (if no duplicate):
   - MCP: `issue_write(method="create", owner, repo, title, body, labels)`
   - gh CLI: `gh issue create --repo {REPO} --title "..." --body "..." --label "skill-bug"`
6. **Writeback** — see `references/queue-writeback.md`.

## Title format

```
fix({kit}): {bug description, first 60 chars}
fix({kit}/{module}): {bug description}   ← when module is set
```

## Issue body template

```markdown
## Skill/Agent Issue

**Affected**: `{skill-name}`
**Type**: bug
**Module**: `{module-name}` (or "kit-wide")

### Environment
- **Kit**: `{kit-name}` v`{kit-version}` (from `.claude/metadata.json`)
- **Module**: `{module-name}` v`{module-version}` (or "kit-wide")
- **T1K CLI**: `{cli-version}`

### Description
{bug}

### Pre-Triage Investigation

(REQUIRED — produced by `references/pre-triage-investigation.md`. Triage adopts the recommended disposition unless evidence has changed since filing.)

**Verification status:** {confirmed-in-main | stale | cant-repro | dup-of-#NN}
- HEAD checked: `{SHA-short}` on `{date}`
- File-line evidence: `{path:line}` — `{1-line excerpt}` (or "N/A — claim refers to absence, not presence")
- Repro check: `{command}` → `{outcome}` (or "N/A — design issue, not behavioral")

**Classification:** `{state from 10-state taxonomy}`

**Coupling / dependencies:**
- Related: `#NN` ({1-line summary}) — or "none found"
- Blocked by: `#NN` (`{blocker title}`) — or "none"
- Related PRs: `#NN`, `#NN` — or "none"

**Fix sketch** (omit if `*-needs-design` / `tracker-*` / `cant-repro` / `dup` / `stale`):
- `{path}` — {what to change}
- {test/verification command}
- {expected post-fix behavior}

**Recommended triage disposition:** `{close-cant-repro | close-redundant | close-dup | queue-for-cook | comment-and-leave-open | merge-as-is}`

**Rationale:** {1-2 sentences — why this disposition fits, cite verification evidence}

### Evidence
```
{evidence — sanitized, relative paths, no $HOME}
```

### Fingerprint
`{fingerprint}`
```

## Labels

Apply `skill-bug`. If local fix was applied, also add `sync-needed`.

## Security

- Relative paths only in body — never `$HOME` or absolute paths
- Redact secrets, API keys, tokens before including any log evidence
