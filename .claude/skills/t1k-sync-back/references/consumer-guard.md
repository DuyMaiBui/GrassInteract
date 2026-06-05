---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: t1k-maintainer
protected: true
---
# Consumer Guard

Use this when: deciding whether the current CWD is a valid consumer project (required first step before any sync-back operation).

## Detection (do this FIRST, before any pre-flight check)

1. Read the CWD repo name.
2. If it matches any kit source repo below → **REFUSE** with:
   `"This is a kit source repo. sync-back is consumer-only. Commit + push directly instead."`

**Kit source repos (never sync-back into these):**
`theonekit-core`, `theonekit-unity`, `theonekit-cli`, `theonekit-designer`,
`theonekit-cocos`, `theonekit-rn`, `theonekit-web`, `theonekit-nakama`,
`theonekit-release-action`, `t1k-telemetry-worker`

**Alternative heuristic:** if `.claude/modules/{name}/module.json` exists in CWD AND the `origin` field matches the CWD repo name → this is the owner repo, refuse.

## Why

Running sync-back inside the kit repo is a no-op at best (diffs local against local) and produces a PR-to-self at worst. The skill is designed for propagating `~/.claude/` edits made in a consumer project back up to the owning kit repo.

## What to do in a kit source repo instead

Commit and push directly with a conventional commit scope matching the module:
- `fix(t1k-base): ...`
- `docs(theonekit-core): ...`

See `rules/commit-scope-policy.md` for the full allow-list.
