---

origin: theonekit-core
repository: The1Studio/theonekit-core
module: null
protected: true
---
# CI/CD Trigger Design

Workflow authoring rules. Full rationale + examples: `docs/ci-cd-trigger-design.md`.

1. **`timeout-minutes` mandatory** — lint 5, unit 10, integration 20, cross-platform 30, e2e/release 45 (GH default 6h causes runaway outliers).
2. **Paths filter any workflow >2 min p50** — `pull_request.paths` MUST mirror `push.paths` (asymmetry trap).
3. **NEVER `paths:` on required-check workflows** — GH reports skipped-by-paths required checks as `Expected — Waiting` permanently → merge deadlock. Use step-level `if:` or stub-success job. NOT safe: `quality-gates.yml`, `release.yml`.
4. **`concurrency.cancel-in-progress: true`** on PR workflows, group `${{ github.workflow }}-${{ github.ref }}`.
5. **Filter `t1k-ci-sync[bot]`** — `if: github.event.head_commit.author.name != 't1k-ci-sync[bot]'`.
6. **Diff-based logic requires `fetch-depth: 0`** — shallow default returns empty diff → silent under-trigger (worse than over-triggering).
7. **Selective execution requires kill switch** — `T1K_GATE_FULL_RUN=1` forces all gates unconditionally (else: misclassification → revert PR deadlock).
8. **Selective execution requires post-merge full-suite gate** — `on: push: branches: [main]` no path filter, safety net.
