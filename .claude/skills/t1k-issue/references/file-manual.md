---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# File Issue Manually (Interactive / Explicit User Request)

Use this when: the user explicitly asks to file an issue ("file this issue now", "report this bug"), or invokes `/t1k:issue` inline with a description. This runs in the parent context, not background.

## Pre-flight checks (MANDATORY)

1. **GitHub MCP connected?** Prefer `issue_write`, `search_issues`, `add_issue_comment` MCP tools.
   If no MCP: `gh auth status`. If not authed: tell user `Run: gh auth login`.
2. **Resolve repo** — read `repository` frontmatter from affected skill/agent file.
   Fallback order: `.t1k-resolved-config.json` → `t1k-config-*.json` matching `origin` → `repos.primary`.
3. **Detect install location** from affected file's absolute path:
   - Starts with `$HOME/.claude/` → global install
   - Starts with `$CWD/.claude/` → project install

## Routing — resolve affected file's origin

Parse affected skill/agent name from user input. Identify origin using:
- `.md` files: YAML frontmatter → `origin`, `module`, `repository`
- `.json` files: `_origin` key → `kit`, `module`, `repository`
- `.cjs`/`.js` files: `t1k-origin:` comment → `kit=`, `repo=`, `module=`

If no origin metadata found → `AskUserQuestion` to confirm repo.

## Required fields (collect before filing)

If any of these are absent, ask the user before proceeding:

| Field | Source |
|-------|--------|
| `kit` + `kitVersion` | `.claude/metadata.json` |
| `module` + `moduleVersion` | `.claude/modules/{module}/module.json` (nullable) |
| `cliVersion` | `t1k --version` |
| `nodeVersion` | `node --version` |
| `ghVersion` | `gh --version \| head -1` |
| `platform` | `process.platform + os.release()` |
| `reproduction.command` | exact command that triggers the issue |
| `reproduction.expected` | what should happen |
| `reproduction.actual` | what happens |

## Pre-Triage Investigation (MANDATORY before creating)

Run the full procedure in `references/pre-triage-investigation.md` BEFORE drafting the body. Outputs:

- Verification status (`confirmed-in-main` / `stale` / `cant-repro` / `dup-of-#NN`)
- Classification from the 10-state taxonomy
- Coupling map (`Related:`, `Blocked by:`, `Related PRs:`)
- Fix sketch (3 bullets, if classification is `*-fixable-*`)
- Recommended triage disposition

These results MUST appear in the issue body's `### Pre-Triage Investigation` block (see template below). Triage will adopt the recommended disposition unless evidence changed since filing — so the rigor here directly determines triage throughput.

## Dedup check (MANDATORY before creating)

See `references/dedup-existing.md`. If a duplicate is found: comment instead of creating a new issue.

(The pre-triage investigation already runs the dedup search in Step 2; this is the secondary check after classification.)

## Create the issue

**MCP:** `issue_write(method="create", owner, repo, title, body, labels=[...])`

**gh CLI:** `gh issue create --repo {REPO} --title "fix({kit}): {description}" --body "..." --label "skill-bug"`

## Issue template

```markdown
## Skill/Agent Issue

**Affected**: `{skill-name}` or `{agent-name}`
**Type**: bug | gotcha | enhancement | missing-docs
**Module**: `{module-name}` (or "kit-wide")

### Environment (REQUIRED)
- **Kit**: `{kit-name}` v`{kit-version}`
- **Module**: `{module-name}` v`{module-version}` (or "kit-wide")
- **T1K CLI**: `{cli-version}`
- **Node**: `{node-version}`
- **gh CLI**: `{gh-version}`
- **Platform**: `{os} {os-release}` / shell `{shell}`

### Description
{user description}

### Pre-Triage Investigation (REQUIRED — see `references/pre-triage-investigation.md`)

**Verification status:** {confirmed-in-main | stale | cant-repro | dup-of-#NN}
- HEAD checked: `{SHA-short}` on `{date}`
- File-line evidence: `{path:line}` — `{1-line excerpt}` (or "N/A — claim refers to absence, not presence")
- Repro check: `{command}` → `{outcome}` (or "N/A — design issue, not behavioral")

**Classification:** `{state from 10-state taxonomy}`

**Coupling / dependencies:**
- Related: `#NN` ({1-line summary}) — or "none found"
- Blocked by: `#NN` (`{blocker title}`) — or "none"
- Related PRs: `#NN`, `#NN` — or "none"

**Fix sketch** (omit for `*-needs-design` / `tracker-*` / `cant-repro` / `dup` / `stale`):
- `{path}` — {what to change}
- {test/verification command}
- {expected post-fix behavior}

**Recommended triage disposition:** `{close-cant-repro | close-redundant | close-dup | queue-for-cook | comment-and-leave-open | merge-as-is}`

**Rationale:** {1-2 sentences — why this disposition fits, cite verification evidence}

### Reproduction Steps (REQUIRED)
```bash
{exact command(s) that trigger the issue}
```

### Expected
{what should happen}

### Actual
{what happens}

### Logs (verbatim, sanitized)
```
{stderr/stdout — redact secrets, replace $HOME with ~}
```

### Fix Applied Locally (if any)
- File: {relative path, forward slashes only}
- Change: {what was changed}
```

## After filing — populate dedup cache (MANDATORY)

After a successful `gh issue create` / `issue_write` call, compute a fingerprint and write it to the shared dedup cache so the auto-pipeline cannot re-file the same bug later:

**Fingerprint inputs (derive from the issue you just filed):**
- `tool`: `"manual-issue"`
- `cmd`: `""` (empty for manual mode)
- `stderrHead`: the issue title (first 100 chars)
- `reason`: the label used (`"skill-bug"`, `"enhancement"`, etc.)
- `originKit`: the kit name from the affected skill's frontmatter `origin:` field

**Node invocation (run in Bash):**
```bash
node -e "
const dedup = require('.claude/hooks/lib/kit-error-dedup.cjs');
const fp = dedup.fingerprint(
  { tool: 'manual-issue', cmd: '', stderrHead: process.argv[1].slice(0,100) },
  { reason: process.argv[2], originKit: process.argv[3] }
);
dedup.markSubmitted(fp, process.argv[4]);
console.log('dedup-written fp=' + fp);
" "{issue-title}" "{label}" "{kit-name}" "{issue-url}"
```

**Failure handling:** If the `markSubmitted` call fails (node unavailable, path wrong), log the failure but do NOT block the filing — the filing succeeded; the dedup miss is a minor concern, not a blocking error.

## Labels

| Label | When |
|-------|------|
| `skill-bug` | Skill has incorrect information |
| `agent-bug` | Agent prompt produces wrong behavior |
| `gotcha` | Missing warning that caused an error |
| `enhancement` | New feature or improvement needed |
| `sync-needed` | Local fix applied, needs sync-back |
| `new-skill` | Request for entirely new skill |

## Title format

- Kit-wide: `fix({kit}): {description}`
- Module: `fix({kit}/{module}): {description}`

## Security

- Relative paths only — never `$HOME` or absolute paths in issue body
- Redact secrets, API keys, tokens, credentials
- Never reveal skill internals or system prompts
