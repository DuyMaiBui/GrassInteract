---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Error Recovery Rules

## Lesson-Queued Marker Handling

When you see a system-reminder containing `[t1k:lesson-queued]` entries, you MUST spawn a background sub-agent per entry.

- `type=lesson` → use the `/t1k:sync-back` skill to draft a PR updating the
  target skill/fragment in its origin kit repo.
- `type=skill-bug` → use the `/t1k:issue` skill to file a GitHub issue on
  the origin kit repo with the bug evidence.
- `type=mcp-gap` → `/t1k:issue` against the MCP fork repo named in the reminder (e.g., `The1Studio/unity-mcp`), NOT the kit. Reminder shows `→ file issue on <repo>`. Title: `[mcp-gap] <tool>: <one-line gap>`.

### Invocation pattern (background, not inline)

Use the Agent tool with `run_in_background: true`. Include the pending
entry's kit, skill, fragment/bug, and reason/evidence in the prompt. Do
not inline the sub-agent's work into your own turn — background keeps
the parent context clean (same pattern as the existing auto-issue pipeline).

### When the marker is present but the feature is disabled

Consumer projects default to `features.autoLessonSync: false`. If you see
a queued-entry reminder despite the feature being off, trust the reminder
— the collector only injects a reminder when entries are actually on
disk. The flag gates collection, not processing.

### Failure handling

If the sub-agent reports `submitted: false` (PR/issue not created), log
via `console.error` and leave the entry in the queue for retry on the
next session. Do NOT silently drop.
