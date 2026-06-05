---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Commit Scope Policy

<!-- updated 260523 -->

## Rule

Every conventional commit scope must be in the allow-list. Unknown scopes
return `[]` from `getAffectedModules` — the release workflow silently skips them
and prints `No releasable commits since last tag — exiting`. No error. No release.

**Skill names are NOT module names.** `fix(t1k-doctor):` and `feat(t1k-handoff):`
look legitimate but match no module. Use the module that owns the skill instead:

```bash
cat .claude/modules/*/module.json | jq '{name, skills}'
```

## Common scopes cheatsheet

| Use case | Scope |
|---|---|
| One module | `t1k-base`, `t1k-extended`, `t1k-maintainer`, or any module name |
| Kit-wide / multi-module | `modules`, `all`, `meta`, `kit` |
| Cross-cutting (canonical files, lifecycle, migration, spec) | `kit-spec`, `canonical`, `lifecycle`, `migration` |
| No scope | bumps ALL modules (release types) or no bump (non-release types) |

Full allow-list with source line references: `docs/commit-scope-policy.md`.

## How to apply

1. **Feature/fix for one module** — use the module name: `fix(t1k-base): ...`
2. **Kit-wide change** (CI, rules, multi-skill refactor) — use `feat(modules): ...`
3. **Cross-cutting concern** — use the matching meta-scope: `fix(canonical): ...`
4. **Verify after push** — if the release workflow says
   `No releasable commits since last tag — exiting`, the scope was wrong.
