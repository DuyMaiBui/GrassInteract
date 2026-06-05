---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Always Ask on Any Unresolved Item — Strict AskUserQuestion Mandate

This is a strict extension of `~/.claude/rules/ask-before-deciding.md`. When in conflict, this file wins.

## Rule

**You MUST invoke `AskUserQuestion` for ANY of the following — even if the global rule would let you proceed silently:**

1. **Any question you would otherwise phrase to the user in prose** — yes/no, single-option confirmations, "should I…?", "can I…?", "do you want…?"
2. **Any unresolved item in a plan** before proceeding past it — `TBD` / `TODO` / `??` markers, conflicting requirements between phases.
3. **Any unresolved item in a report** before submitting it as final — listed-but-unanswered questions, ambiguous findings.
4. **Any ambiguity discovered mid-implementation** not covered by the prior plan/answer — edge cases, naming choices for new public APIs, file placement when multiple valid locations exist.
5. **Any default value or policy choice** not explicitly handed to you — fallback behaviors, threshold values, severity levels, retention durations.
6. **Any deletion, overwrite, or destructive action** whose blast radius is non-trivial — even if you have a strong default. Ask first.
7. **Any skill needing a multi-option decision.** Skill bodies MUST call `AskUserQuestion`. If deferred, load via `ToolSearch(query="select:AskUserQuestion")` first. No "skill emitted prose" exemption.

## How to apply

When in doubt → invoke `AskUserQuestion`. Asking is cheap; assuming is expensive. The bias is **always toward asking**.

## Plan / report deliverables

Before writing or finalizing ANY plan file or report:

1. Scan for unresolved markers: `TBD`, `TODO`, `???`, `open question`, `unresolved`, `pending`, `to be decided`, `unclear`.
2. For EACH unresolved item, batch into a single `AskUserQuestion` call (max 4 per call — split if more).
3. Only mark the deliverable "ready" / commit it after all items resolved.

Reports MAY include a final "Unresolved questions" section ONLY when the user explicitly accepted deferred items.

## Narrow exceptions (when NOT to ask)

| Scenario | Why no question |
|---|---|
| User just gave a direct command in the same turn | They told you what to do |
| Reporting results of an action already taken | Reporting ≠ deciding |
| Pure factual lookup | Information request |
| Plan approval flow — use `ExitPlanMode`, not `AskUserQuestion` | Different tool |
| Prior `AskUserQuestion` THIS session fully covers the next step | Re-asking = nagging |
| User unambiguously stated the decision in chat prose THIS session — no reasonable alternative interpretation | Prose-answered = answered |

**The threshold is "unambiguous"** — if you'd need to guess between two reasonable interpretations, ask.

## Related

- `ask-before-deciding.md` — global baseline (this file extends it)
- `~/.claude/CLAUDE.md` priority #2 — "Mandatory: Use AskUserQuestion"
