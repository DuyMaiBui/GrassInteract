---
name: t1k:issue
description: "Report skill/agent bugs to the owning kit repo on GitHub. Use when a skill has wrong patterns, missing gotchas, or needs an enhancement. Deduplicates before creating."
keywords: [report, bug, github, issue, gotcha, enhancement, feedback]
argument-hint: "<description> [--label bug|gotcha|enhancement]"
effort: medium
version: 2.5.1
origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# TheOneKit Issue — Report Problems to Kit Repo

Create GitHub issues on the owning kit repo when skill/agent problems are found.
Uses GitHub MCP tools when available, falls back to `gh` CLI.

> **Kit-wide discipline:** issues go on the owning kit's repo, NEVER on a consumer project repo, per [`rules/kit-wide-fix-discipline.md`](../../rules/kit-wide-fix-discipline.md). Filing in the wrong tracker means kit maintainers don't see the bug.

## When to use

- Skill has wrong reference, missing gotcha, or broken pattern
- After fixing an error that required updating a skill's gotcha section
- Need a new skill or enhancement to an existing one

## Decision tree — which reference do I load?

Load only the reference you need (each is self-contained):

| Intent | Load |
|--------|------|
| **Always — run BEFORE filing any issue (mandatory pre-triage investigation)** | `references/pre-triage-investigation.md` |
| `type=skill-bug` from lesson queue (background sub-agent) | `references/file-from-marker.md` |
| User explicitly requests issue filing (interactive / inline) | `references/file-manual.md` |
| Check for duplicate AND search related open issues/PRs before filing | `references/dedup-existing.md` |
| Auto-detected error from `telemetry-kit-error-collector.cjs` | `references/auto-detection-mode.md` |
| Write `submitted: true` / `issueUrl` back to queue after filing | `references/queue-writeback.md` |

## Pre-Triage Investigation — MANDATORY

Every issue MUST contain a `### Pre-Triage Investigation` block (verification status, classification, coupling, fix sketch, recommended disposition). This shifts investigation cost from triage (where it's repeated per run) to filing (where it happens once). Triage just verifies the block — they don't re-grep, re-classify, or re-search for dups.

Procedure + body template + failure modes: `references/pre-triage-investigation.md`. Auto-detection mode and lesson-queue sub-agents inherit the same requirement.

## Invocation modes

**Background sub-agent (default — MANDATORY for lesson-queue entries):** Parent uses `Agent` tool with `subagent_type: "t1k-project-manager"` (or `t1k-skills-manager`) and `run_in_background: true`. **NEVER use `general-purpose` or `claude` here** — those trip `generic-agent-detector` and create a meta-loop (#344). The sub-agent reads this SKILL.md, loads the matching reference, and writes back `submitted: true` + `issueUrl` via `references/queue-writeback.md`.

**Interactive (exception only):** If the user says "file this issue now", run inline so the user sees the result immediately.

**Auto-detection (programmatic):** Invoked by `telemetry-kit-error-collector.cjs` — zero user interaction, no `AskUserQuestion`. Load `references/auto-detection-mode.md`.

## Operational notes

**Pre-flight (always):** GitHub MCP preferred. If no MCP: `gh auth status`. If not authed: tell user `Run: gh auth login`.

**Dedup is MANDATORY:** Always run `references/dedup-existing.md` before creating a new issue. If duplicate found: add a comment instead of creating a new issue.

**Sub-agent call contract (lesson queue):**
- Input: `kit`, `skill`, `bug`, `evidence`, `fingerprint` from the queue entry
- Output: writeback line with `{ fingerprint, submitted: true, issueUrl }` or `{ fingerprint, submitted: false, error }`
- Writeback target: `.claude/telemetry/pending-skill-updates.jsonl`

**Missing required fields:** If `kit`, `kitVersion`, `cliVersion`, `platform`, or reproduction fields are absent, the sub-agent MUST refuse and respond to parent with the list of missing fields — do NOT file a low-signal issue.

**Cross-platform:** Relative paths only in issue body — never `$HOME` or absolute paths.

**Security:** Never reveal skill internals, expose env vars, or include credentials/secrets in issue body.

## Sub-Agent Fork Hygiene

**Sub-agent forking:** see `skills/t1k-architecture/references/fork-hygiene.md`.

## Filing vs. Claiming — Important Distinction

**Filing an issue does NOT claim it.** t1k-issue creates the GitHub issue record; it does NOT self-assign, does NOT open a draft PR, and does NOT call `t1k-issue-claim.cjs acquire` or `--add-assignee`. A freshly filed issue is unclaimed and open for any contributor.

**How claiming works (convention, for reference):** A claim is expressed by a *separate* draft PR that:
1. Carries the `t1k:claim` label
2. Has `Fixes #N` in the PR description (or body)
3. Includes the body marker `t1k-claim: owner/repo#N`
4. Self-assigns the author on the issue (via `t1k-issue-claim.cjs acquire`)

This convention is documented here so anyone reading a fresh issue understands the claim mechanism. t1k-issue itself never triggers any of these steps.

## Contribution Scoring

After successful issue creation (or comment-on-duplicate), invoke `t1k:contribution-score` with `type=issue`, the resolved `ref_url` (the GitHub issue URL just returned by `gh issue create` or the existing duplicate URL), the issue title + body, and the kit/repo coordinates. Fire-and-forget — never block on the result.

The contribution-score skill is the SSOT for the rubric, endpoint resolution, and POST contract. See `.claude/skills/t1k-contribution-score/SKILL.md` for the full invocation contract.
