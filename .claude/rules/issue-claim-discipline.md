---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# Issue Claim Discipline — Claim Before You PR

Prevents duplicate work on in-scope issues (`issueClaim.inScopeRepos` in `t1k-config-core.json`;
default `The1Studio/theonekit-*` + `t1k-*`). Before opening a PR for an issue, acquire the claim
via the SSOT script — never inline `gh` for claiming:

```
node .claude/scripts/t1k-issue-claim.cjs acquire <owner/repo#N>
```

- **Hard-block** on `state:"held"` (live foreign claim): surface `holder` + `prNumber`, stop. Override only with `--steal`.
- The claim is GitHub-native: `acquire` self-assigns the issue; the draft PR you then open (`Fixes #N`, `t1k:claim` label, `t1k-claim: owner/repo#N` marker) IS the durable claim. After opening it, run `acquire <ref> --pr <N>` (lowest-PR# tie-break; closes only your own losing PR).
- **Release** = mark the PR ready (`release <ref>`); merge/close auto-releases via GitHub state.
- **No TTL** — `t1k-triage` reports claims inactive > `stalenessDays` as stale (reporting only, never auto-closed).

All claim checks/mutations go through the script (SSOT, per `ai-driven-design.md`); the only sanctioned direct `gh` is `t1k-babysit-pr`'s `gh pr merge`. Config, residual-risk (no-CAS + tie-break backstop), and the worker upgrade path are documented in the script + `issueClaim` config block.
