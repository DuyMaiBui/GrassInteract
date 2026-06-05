---
origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---

# Teammate Cleanup — Shutdown After Done

## Rule

Teammates spawned via `TeamCreate` + `Agent({team_name})` MUST be cleaned up after they finish. Trigger on `TaskUpdate(status: "completed")` flip — NOT on SendMessage receipt. Unlike `Agent({run_in_background: true})` sub-agents (which self-terminate), teammates persist until explicitly shut down.

Full sequence (5-step shutdown, spawn-brief addendum, anti-patterns, history): `docs/teammate-cleanup-after-done.md`.

## Why

Idle teammates hold mailbox connections + consume context budget; on long sessions with 8+ teammates this compounds. Established 2026-05-23 during DOTS-AI ChaosForge demo cook session.

## Related

- `rules/agent-completion-discipline.md`
- `skills/t1k-team/SKILL.md`
