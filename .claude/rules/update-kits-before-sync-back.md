---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Update Kits Before Sync-Back

## Rule

**MANDATORY** before invoking `/t1k:sync-back` (or spawning a background `t1k-sync-back` sub-agent):

1. `t1k modules update` — refresh installed kits to latest released versions. **EXCEPTION:** if your project-local `.claude/` files have diverged ahead of the kit (edited but not yet synced back), DO NOT update first — `t1k modules update` would clobber those edits (#367). Sync-back FIRST, let the kit re-release, then update. See § Why.
2. `t1k --version` — confirm output. `All modules are up to date` = proceed.
3. **Per-file** GitHub HEAD check — for EACH file in your sync-back diff, query the kit's commit history with the `?path=<file>` filter (the `?path=` filter is **mandatory** — the global top-5 view drops commits when unrelated activity is busy and produces false-clears). If the top result for any path is newer than the SHA your local file is based on, READ that commit's diff and either rebase or abort the sync-back. Exact `gh api` commands + the multi-file loop: `docs/update-kits-before-sync-back.md`.
4. Only after 1–3 pass clean, spawn the sync-back sub-agent.

Full procedure, narrow exceptions, originating incident (2026-05-23): `docs/update-kits-before-sync-back.md`.

## Why

Sync-back is a one-way push. A stale local "regresses" commits the sub-agent never saw — silently reverting other contributors' work. `t1k modules update` brings you to the latest TAG, not the latest commit on `main` — step 3 closes that gap.

**Update-first is safe ONLY when local matches kit-shipped content** (#367). If your project-local files have diverged ahead of the kit's last release (you edited locally but never synced back), `t1k modules update` WILL clobber those local edits during extraction. In that case run `/t1k:sync-back` FIRST, let the kit re-release, then update. See [`prefer-local-over-global-edits.md`](prefer-local-over-global-edits.md) § "Gotcha — Updates CAN clobber" for the full behavior.

## Related

- [`prefer-local-over-global-edits.md`](prefer-local-over-global-edits.md) — `./.claude/` is canonical; update CAN clobber divergent edits (see its § "Gotcha — Updates CAN clobber").
- [`orchestration-rules.md`](orchestration-rules.md) — `/t1k:sync-back` is background-only.
- [`kit-pr-workflow-boundary.md`](kit-pr-workflow-boundary.md) — open PR from consumer; merge in kit-clone session.
- `skills/t1k-sync-back/SKILL.md` § "Pre-flight Step 0 — Kit-Freshness Guard" — skill body codifying steps 1–3.
