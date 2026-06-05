---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Telemetry Rules

## Auto-Lesson, Skill-Bug & MCP-Gap Pipeline

Emit a marker in your message text → `lesson-collector.cjs` Stop hook captures it → enqueued to `pending-skill-updates.jsonl` → end-of-session detached flush (skill-bug / mcp-gap → `gh issue create`; lesson → next UserPromptSubmit reminder + background `/t1k:sync-back`). Full pipeline, privacy, overrides, and marker formats: `rules/workflow-failure-auto-issue.md` + `docs/workflow-failure-auto-issue.md`.

End-of-session detached flush via `lib/lesson-flush-runner.cjs` handles skill-bug/mcp-gap markers when no further UserPromptSubmit fires (#334). Detail: `docs/workflow-failure-auto-issue.md`.

### When to emit markers — decision checklist (AI guidance)

Emit `[t1k:skill-bug ...]` when skill content is wrong/misleading, you corrected a mistake caused by following a skill, or you are about to call `/t1k:issue` manually (emit FIRST to populate dedup cache). Emit `[t1k:lesson ...]` for non-obvious gotchas. Emit `[t1k:mcp-gap ...]` for MCP tool gaps. Do NOT emit for trivia, transient errors, or already-documented issues.

### Dual-action on marker (#341)

Mid-session marker emission MUST also spawn `/t1k:issue` (skill-bug / mcp-gap) or `/t1k:sync-back` (lesson) in the same turn — the Stop-hook queue is a safety net for end-of-session only. Detail: `docs/auto-issue-pipeline.md` § Dual-action.

**Manual-vs-auto dedup:** After any manual `/t1k:issue` filing, call `markSubmitted(fingerprint, issueUrl)` — see `skills/t1k-issue/references/file-manual.md` § "After filing". Full guide: `docs/auto-issue-pipeline.md`.
