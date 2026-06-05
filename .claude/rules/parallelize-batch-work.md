---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Parallelize Batch Work — Fan Out, Don't Sequence

For any batch operation with **>30 independent items** OR **>5 minutes** estimated wall time, default to a parallel execution strategy.

## Pre-flight: Always Check for Parallelization (Lower Threshold)

Before sequencing ANY multi-step operation:

1. **Count independent units** — distinct repos, files, PRs, subagent tasks. Independence means: no read-then-write data dependency between them.
2. **If count ≥ 2, explicitly state your parallelization decision**: either "parallelizing N units via background sub-agents" or "sequencing because [reason]".
3. **Default bias = parallelize.** Sequencing requires a stated reason.

**Working-tree safety:** parallel sub-agents on the SAME git repo SHARE the working tree. Either (a) sequence them inside a single sub-agent, OR (b) use `git worktree add` for true parallel branches. Parallel sub-agents on DIFFERENT repos are always safe.

## Rule

When about to write a sequential loop over a batch, STOP and pick a parallel strategy:

| Scenario | Strategy | Default concurrency |
|---|---|---|
| HTTP/network I/O — independent requests | Single Node/Python process with bounded `Promise.all` worker pool | 20 concurrent |
| File IO at scale — thousands of files | `xargs -P` or GNU `parallel` | 8 (CPU count) |
| AI judgment — N items needing per-item Claude reasoning | N background subagents, each owning a chunk | 4–8 (cap at 8) |
| Mixed (fetch + reason + write) | Phase 1 parallel fetch (script), Phase 2 parallel reason (subagents) | 20 + 4–8 |
| Independent shell commands with no shared state | Single message with multiple Bash tool calls | unlimited |

## How to apply

1. Estimate: items × per-item time. If >5 min, parallelize.
2. Verify independence + idempotency.
3. Pick strategy from the table. Don't invent.
4. Use `AskUserQuestion` for concurrency degree when non-obvious (4 vs 8 vs 16).
5. Smoke first (per `preview-first-batch.md`) using the parallel implementation.
6. Aggregate to one tally (recorded / dup / error), not raw per-item logs.

## Anti-patterns

Sequential loop over 300+ items; `await fetch()` in a loop; re-fetching staged data each subagent run; killing+restarting a subagent rather than resuming; asking "should I parallelize?" instead of just picking concurrency.

## Related

`preview-first-batch.md` · `always-ask-on-unresolved.md` · `ai-driven-design.md` · `skills/t1k-team/references/intra-phase-fanout.md`
