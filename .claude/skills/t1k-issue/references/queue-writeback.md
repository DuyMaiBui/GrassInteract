---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Queue Writeback Protocol

Use this when: after any issue submission (success or failure), the sub-agent MUST write the result back to the queue so `lesson-queue-processor.cjs` can drop the row on the next tick.

## Contract (MANDATORY)

The processor filters entries where `submitted === true` — these are dropped from the queue. Entries with `submitted: false` increment the `failures` counter. After 5 failures the circuit breaker marks the entry `permanently_failed: true` and drops it.

## Success writeback

Append a new JSONL line to `.claude/telemetry/pending-skill-updates.jsonl`:

```json
{
  "fingerprint": "<same fingerprint as the original entry>",
  "submitted": true,
  "issueUrl": "https://github.com/Owner/repo/issues/{number}"
}
```

## Failure writeback

```json
{
  "fingerprint": "<same fingerprint>",
  "submitted": false,
  "error": "<short message — e.g. 'no-gh-auth', 'duplicate-filed', 'missing-fields'>"
}
```

Do NOT retry on failure — leave the entry in the queue. The next session's `lesson-queue-processor.cjs` will re-attempt. After `FAILURE_THRESHOLD` (5) failures the circuit breaker permanently drops the entry.

## File location

The canonical queue path is embedded in the lesson-queue reminder text — look for the line:

> `After each completes, append a writeback line to \`/absolute/path/to/.claude/telemetry/pending-skill-updates.jsonl\``

**Subagents MUST use the exact absolute path from the reminder** — do not compute it from templates or environment variables. The path is resolved once by `lesson-queue-processor.cjs` at reminder-generation time and injected verbatim so all subagents agree on the same file.

The processor defensively sweeps the following known orphan locations and migrates entries into the canonical path on each tick, but writing to the canonical path remains the contract:
- `~/.claude/telemetry/pending-skill-updates.jsonl` (global-telemetry)
- `~/.claude/pending-skill-updates.jsonl` (global-root orphan — missing `telemetry/` subdir)
- `<project-root>/.claude/pending-skill-updates.jsonl` (project-root orphan — missing `telemetry/` subdir)

## Auto-detection mode — additional step

After writing the success line, also call the local dedup cache:
```js
// .claude/hooks/lib/kit-error-dedup.cjs
markSubmitted(fingerprint, issueUrl)
```

This prevents re-filing within `autoIssueSubmission.dedupeTTLDays` days.

## Manual-mode — same cache writeback required

Manual invocations of `/t1k:issue` (via `references/file-manual.md`) MUST ALSO call `markSubmitted(fingerprint, issueUrl)` after filing. The fingerprint computation for manual mode differs from auto-mode (uses `tool: 'manual-issue'` and the issue title as `stderrHead`) — see `references/file-manual.md` § "After filing — populate dedup cache". This ensures a consistent shared dedup state between the two filing paths.

## Lesson-queue (type=skill-bug) entries

The processor `lesson-queue-processor.cjs` handles both `type=lesson` and `type=skill-bug` entries in the same JSONL file (`pending-skill-updates.jsonl`). The writeback schema is identical — only the `fingerprint` differs per entry. Sub-agents MUST preserve the fingerprint from the original entry verbatim.
