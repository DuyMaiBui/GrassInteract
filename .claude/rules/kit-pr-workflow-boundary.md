---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Kit-PR Workflow Boundary — Consumer ≠ Kit Maintainer Shell

## Rule

When working inside a **consumer project**, you MUST NOT propose, offer, or perform any of the following on a kit PR — even when YOU opened the PR via `/t1k:sync-back` or `/t1k:issue`:

- `gh pr merge` on a `theonekit-*` PR
- `/t1k:babysit-pr` watching CI on a `theonekit-*` PR
- Resolving merge conflicts on a `theonekit-*` PR
- `gh pr review --approve` or push commits to a `theonekit-*` PR branch
- Asking "want me to babysit/merge/watch the kit PR?"

Kit-PR work belongs to the **kit maintainer session** (a `theonekit-*` clone). Full rationale + failure modes: `docs/kit-pr-workflow-boundary.md`.

## What you CAN do from a consumer project

- Spawn **background** `/t1k:sync-back` to OPEN a PR on the owning kit repo.
- Spawn **background** `/t1k:issue` to FILE an issue on the owning kit repo.
- Report the resulting URL — one line: `"PR opened: <url>"`.
- STOP there.

## How to apply

After opening a kit PR from a consumer:

1. Report the URL — one line.
2. Do NOT offer to babysit/merge.
3. Do NOT list kit-PR follow-ups in "Next Steps".
4. If user explicitly asks "babysit that PR" → `cd` into the kit clone and operate from there.

## Narrow exception

If the current cwd IS the kit (`git remote get-url origin | grep theonekit-`), the boundary does not apply — you ARE the kit maintainer.

## Related

- `rules/orchestration-rules.md` — `/t1k:sync-back` and `/t1k:issue` are background-only
- `docs/kit-pr-workflow-boundary.md` — failure modes, motivating incident, full rationale
