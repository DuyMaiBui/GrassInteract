---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Module Registry Sync

## Rule

After editing ANY `.claude/modules/*/module.json` file in a modular T1K kit, you MUST regenerate `.claude/t1k-modules.json` before committing.

```bash
node "/path/to/theonekit-release-action/scripts/generate-modules-registry.cjs" "$PWD"
```

(Path depends on local layout — check `/mnt/Work/1M/8. OneAI/theonekit-release-action/scripts/generate-modules-registry.cjs` if you have TheOneKit ecosystem cloned, or fall back to `.release-action/scripts/...` if CI has fetched it into the kit workspace.)

Stage the resulting `.claude/t1k-modules.json` change alongside your module.json edits in the SAME commit.

## Why

- `.claude/t1k-modules.json` is a generated rollup of all per-module `module.json` files. SSOT is per-module; the rollup is derived.
- CI gate `validate-modules-registry-sync.cjs` fails the build if the rollup drifts from SSOT byte-for-byte.
- Two sessions in a row (2026-04-20) hit this failure on otherwise-green PRs — each caused a CI round-trip + a follow-up commit. The regen takes <1 second; skipping it is purely cost, no benefit.

## When drift appears

Symptoms you'll see in CI logs:

```
[validate-modules-registry-sync] FAIL — .claude/t1k-modules.json drifted from per-module module.json SSOT.
Run: node scripts/generate-modules-registry.cjs <kit-root>
```

Fix: run the regen command above locally, commit the diff, push.

## `_origin` block

The generator STRIPS the `_origin` block from `t1k-modules.json`. CI re-injects it post-merge via `inject-origin-metadata.cjs`. Do not hand-add it back — let the pipeline own it.

## Related

- `rules/telemetry.md` — different topic (auto-lesson pipeline); not related
- Generator: `theonekit-release-action/scripts/generate-modules-registry.cjs`
- Gate: `theonekit-release-action/scripts/validate-modules-registry-sync.cjs`
