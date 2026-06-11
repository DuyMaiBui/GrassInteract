# Phase 9 — Namespace Rewrite → WorldPainter (ATOMIC, single batch)

**Effort:** L · **Blocked by:** P8 · **Compiles at boundary:** YES (only if atomic)

## Goal
Rewrite every `namespace GpuTerrain*` / `namespace GrassInteract*` declaration and every `using GpuTerrain*` / `using GrassInteract*` across all surviving files to the `WorldPainter` root, and set asmdef `rootNamespace` to `WorldPainter`/`WorldPainter.Editor`/`WorldPainter.Tests`. **This MUST be one atomic phase** — a half-renamed assembly does not compile (R3). No external consumers exist (scout-verified), so blast radius is internal-only.

## Namespace mapping (per library naming charter — one root per package)
- `GpuTerrain` → `WorldPainter`
- `GpuTerrain.Editor` → `WorldPainter.Editor`
- `GpuTerrain.Tests` → `WorldPainter.Tests`
- `GrassInteract` → `WorldPainter` (merged — both runtimes share the one root namespace)
- `GrassInteract.Editor` → `WorldPainter.Editor`
- `GrassInteract.Tests` → `WorldPainter.Tests`

> Sub-namespacing (e.g. `WorldPainter.Terrain`, `WorldPainter.Scatter`) is OPTIONAL and a judgment call — the charter wants ONE root per package, not necessarily flat. Default: flat `WorldPainter` / `WorldPainter.Editor` / `WorldPainter.Tests` to minimize churn and match the merged single-assembly model. If sub-namespaces are desired, that is a separate follow-up, not part of this atomic rename.

## Steps (ATOMIC — implement all, verify ONCE)
1. Pin instance.
2. Batch find-replace across ALL surviving `.cs`: `namespace GpuTerrain` / `namespace GrassInteract` → `namespace WorldPainter` (collapsing the `.Editor`/`.Tests` suffixes to the new roots); `using GpuTerrain...` / `using GrassInteract...` → `using WorldPainter...` (most cross-references were intra-namespace and may simply drop, since both runtimes now share `WorldPainter`). Watch for type-name collisions when two namespaces merge (e.g. if both had a `Constants` — scout found none, but verify on compile).
3. Set asmdef `rootNamespace` fields.
4. `refresh_unity(force)` — expect a FULL recompile + LONG domain reload (budget 60–180s; MCP timeout ≠ disconnect — wait + retry, never kill).
5. RE-PIN instance after the reload.
6. `read_console` (ALL errors — collect the full set, fix in one batch) → `run_tests` (301).

## Verification — GATE
`grep -rn "namespace GpuTerrain\|namespace GrassInteract\|using GpuTerrain\|using GrassInteract" Assets/WorldPainter` → ZERO hits. `run_tests` = 301 green.

## Rollback
`git revert <P9 sha>` or `git reset --hard <P8 sha>` — single rename commit; tree returns to P8 (compiling, green).

## Risk
R3 (half-rename) — atomic single batch is the mitigation. Namespace-merge collisions — surface on compile, fix in the same batch.
