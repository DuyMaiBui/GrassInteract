---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---

# Scaling — when to hand off to `/t1k:team`

Single-agent triage works for small backlogs. Past a threshold, the per-item analysis budget gets squeezed and verdicts become shallow. Delegate to `/t1k:team` instead.

## Triggers

| Trigger | Action |
|---|---|
| ≤ 20 items total AND ≤ 15 in any single repo AND items are roughly homogeneous | Continue with this skill's default workflow |
| > 20 items total | Hand off to `/t1k:team` — partition into clusters, one agent per cluster |
| > 15 items in any single repo | Hand off to `/t1k:team` — partition that repo by skill/file path |
| Items split into clearly-orthogonal categories (dependabot batch, sync-back PRs, bug fixes, tracker issues) — even at lower counts | Hand off to `/t1k:team` — per-category prompts produce sharper verdicts than a mega-prompt |
| Same skill / same file appears in 3+ open PRs (likely-duplicate cluster) | Hand off — assign that cluster to ONE agent so it can dedup within (e.g., 4 PRs touching `unity-localization` SKILL.md → one agent picks the winner) |

## Hand-off pattern

After Step 1 (Fetch) emits the item list, if any trigger above fires, STOP the default workflow and invoke `/t1k:team` with:

- **Partition rule**: by repo + by skill cluster (so dup-detection happens within an agent's own scope, not across agents)
- **Per-agent mandate**: read-only triage, return verdict per item: `merge` / `close-superseded` / `close-obsolete` / `needs-fix` / `defer`
- **Recursion cap**: each agent must NOT spawn sub-sub-agents
- **Concurrency**: 4 default, 8 max (per `rules/parallelize-batch-work.md` AI-judgment cap)
- **Aggregation**: parent skill batches all verdicts via `AskUserQuestion` (≤4 per call) for one approval round per partition

Then resume Steps 5–7 (Report / Action / Verify) on the aggregated verdicts.

## Why not always hand off?

For small backlogs, the team-spawn overhead and the inter-agent coordination cost outweigh the benefit. Default-single-agent stays cheap and fast for the common case.