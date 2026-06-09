---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: null
protected: false
---
# AI-Velocity Batch-Compile — Unity / DOTS engine specifics

Extends core `rules/ai-velocity-batch-compile.md` — Unity-specific additions only. Core supplies the universal rule (blind-implement the whole batch → verify ONCE → collect all errors → fix in parallel → verify a second time). This file supplies the **Unity compile / domain-reload nuances** that make the round-trip the dominant cost on a DOTS project.

## Why the round-trip dominates on Unity DOTS

Unity domain reloads run 30–120s on this Linux project; each compile is also a flake opportunity. The single biggest AI-time sink is the compile ↔ domain-reload cycle, so core's "verify once" rule pays off harder here than on a typecheck-only stack.

## Unity-specific verification gotchas

- **Compile = `read_console` (every compile error) + `run_tests` (every failure) in the SAME pass.** Do not stop at the first console error — collect the full DOTS error set before fixing.
- **`refresh_unity` no-ops on asmdef-only edits.** An `.asmdef` change may not trigger a recompile; force it (`refresh_unity(force, all)` or touch a `.cs` in the assembly).
- **MCP timeout ≠ bridge disconnect.** A timed-out MCP call usually means Unity is busy compiling/baking, not that the bridge dropped — do NOT kill/restart the editor to "speed things up."
- **A degraded MCP build may lack `manage_packages` / `execute_code`.** Fall back to `touch Packages/manifest.json` + `refresh_unity(force, all)` or `Client.Resolve()` via the available API.
- **Stale `packages-lock.json` cascades into "namespace not found" across unrelated packages** → silently drops test assemblies from Test Runner discovery (89 tests shown instead of ~1239). Resolve packages before trusting any "all green."

## Take direct control of the compile-gate

Spawning an editor-bound sub-agent whose main activity is *waiting* on `refresh_unity` / `run_tests` exhausts its token budget on idle waits. Instead, poll `Library/ScriptAssemblies/*.dll` mtime in a background Bash loop from the coordinator — it notifies on compile completion without consuming context. Editor-MCP-bound work (test runs, baking) still serializes on the main worktree even when independent file groups are fanned out.

## Anti-patterns (Unity)

- Treating every ECS system as immutable production that cannot be batch-refactored. Big DOTS refactors ARE authorized (see `library-quality-mandate-unity.md`); "production-grade" means *correct and well-named*, not *change-averse*.
- Reporting "done / all green" from a stale prior run without a fresh compile + full-suite run — a stale-lock cascade or no-op refresh can mask 90% of the suite being silently dropped from discovery.
- Spawning an editor-bound sub-agent that burns its whole budget on idle recompile waits — poll DLL mtime instead.

## Related

- `rules/ai-velocity-batch-compile.md` (core) — the universal batch-verify rule this extends.
- `rules/parallelize-batch-work.md` — fan out independent work instead of sequencing.
- `rules/library-quality-mandate-unity.md` — big DOTS refactors are authorized; ship the right answer.
- `rules/unity-forbidden-operations.md` — MCP-timeout ≠ disconnect; `refresh_unity` no-op gotcha; never kill/restart the editor.
- `CLAUDE.md` § "Critical Gotchas" — stale `packages-lock.json` recovery.
