---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: base
protected: false
---
# Editor Availability Pre-Check — MANDATORY Before MCP Compile/Verify

**Applies to:** any sub-agent (delegated or parallel) whose task includes MCP
compile, `refresh_unity`, `read_console`, `run_tests`, or other Unity-bound
verification calls.

## The Rule

**Before issuing ANY MCP compile/verify/test call, a sub-agent MUST confirm the
Unity Editor process is alive.** If the Editor is not running, every MCP call
returns an error and the sub-agent burns its entire token budget on retries,
producing zero file writes.

Evidence: a delegated implementer ran ~130 k tokens of failed MCP calls
(`No Unity Editor instances`, repeated `refresh_unity` timeouts) because the
editor was closed before delegation. Zero files were written. The budget was
exhausted.

## Pre-Check Procedure (run before the first MCP call)

```bash
# Signal 1 — lock file (fastest, no pgrep)
ls <project-root>/Temp/UnityLockfile 2>/dev/null && echo "EDITOR UP" || echo "EDITOR DOWN"

# Signal 2 — process (more robust, catches lock-file staleness after a crash)
pgrep -af "Unity.*<ProjectName>" | grep -v UnityHub | grep -v AssetImportWorker
```

Both absent → Editor is NOT running. Do not issue MCP calls.

One present → Editor is likely alive. Proceed; the existing "When MCP doesn't
respond" decision tree (`SKILL.md` § "Recovery decision tree") handles transient
busy states.

## Decision after the check

| Result | Action |
|---|---|
| Editor alive (lockfile OR pgrep hit) | Proceed with MCP calls. If a call times out, follow the recovery decision tree — do NOT abort immediately. |
| Editor down (both absent) | Skip ALL MCP compile/verify/test steps. Do filesystem-only work (`Write`, `Edit`, `Bash`) for this phase. Report back: "Unity Editor not running — skipped MCP steps; files written but not compiled." |
| Editor alive but bridge unresponsive | Follow recovery decision tree (pgrep workers, DLL mtime, log tail). Wait for reload. Do NOT ask user to restart. |

## Spawn-Brief Requirement

When a team-lead spawns a sub-agent for MCP-bound Unity work, the brief MUST
include:

```
## Editor Status Pre-Check (REQUIRED FIRST STEP)
Run before any MCP call:
  ls <project-root>/Temp/UnityLockfile && echo UP || echo DOWN
  pgrep -af "Unity.*<ProjectName>" | grep -v UnityHub
If both return empty → skip all MCP steps; do filesystem work only; report
"Editor was down, MCP skipped."
```

Omitting this from the brief is a spawn-brief defect — the sub-agent will
discover the failure only after token budget is exhausted.

## Why This Differs From the Reactive Decision Tree

The `SKILL.md` recovery decision tree (§ "When MCP doesn't respond") is
**reactive** — it diagnoses after a call fails. This pre-check is **proactive**:
it costs two Bash calls and zero MCP credits, vs. the cost of repeated timeouts
during a domain reload or with a closed editor. The pre-check DOES NOT replace
the recovery tree; it runs before the first call so a closed editor is caught
immediately, not after a budget-burning retry loop.

## Related

- `SKILL.md` § "🔍 When MCP doesn't respond — diagnose Unity status WITHOUT MCP" — reactive recovery (runs after a failure)
- `rules/unity-forbidden-operations.md` — never kill/restart Unity; never Reimport All
- `rules/ai-velocity-batch-compile-unity.md` — delegate compile-gate polling to Bash DLL mtime, not idle MCP waits
