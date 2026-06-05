---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Workflow Failure → Auto-File Issue

`workflow-failure-detector.cjs` (SubagentStop hook) auto-detects failures. AI-emitted markers are the fallback. Full reference: `docs/workflow-failure-auto-issue.md`.

## Rule

If you observe any failure mode below AND the hook didn't auto-detect it (no `[t1k:workflow-failure-detected]` stdout), emit the matching marker BEFORE moving on:

| Failure mode | Marker type |
|---|---|
| Agent stops mid-task without writing declared deliverable | `[t1k:skill-bug ...]` |
| Skill body cannot fulfill its stated purpose in context | `[t1k:skill-bug ...]` |
| Agent modifies files outside its declared scope | `[t1k:skill-bug ...]` |
| Plan-file claim found factually wrong during execution | `[t1k:lesson ...]` |
| MCP tool returns "unknown action" / missing-parameter gap | `[t1k:mcp-gap ...]` |
| Agent type definition leads to predictable failure mode | `[t1k:skill-bug ...]` |

Marker format (ALL attributes required — regex silently rejects partial markers):

```
[t1k:skill-bug kit="<kit>" skill="<skill-or-agent>" bug="<one-line>" evidence="<path-or-id>"]
[t1k:lesson kit="<kit>" skill="<skill>" fragment="<file-or-section>" reason="<one-line>"]
[t1k:mcp-gap kit="<kit>" tool="<tool>" gap="<one-line>" evidence="<reproduction>"]
```

Marker MUST appear in assistant message text (NOT a tool call or file). Stop hook parses message text only.

## Dual-action (#341)

Mid-session marker emission MUST also spawn `/t1k:issue` (skill-bug/mcp-gap) or `/t1k:sync-back` (lesson) in the SAME turn. Stop-hook queue is end-of-session safety-net only. Full rule + anti-patterns: `docs/auto-issue-pipeline.md` § Dual-action.

Do NOT emit for: tool errors the hook already handles, partial markers, or inline `/t1k:issue` calls — markers + background sub-agent is the canonical path. See `docs/workflow-failure-auto-issue.md` for anti-patterns, spawning-agent brief template, and SubagentStop hook details.
