---
phase: R1
plan: scatter-layer-placement-split
agent: t1k-unity-developer
effort: S (~0.5 day)
unity instance: GrassInteract@de203215 (port 6403)
---

# R1 — IScatterPlacement Strategy Interface + Two Implementations

## Goal

Introduce the placement-strategy abstraction. Create `IScatterPlacement` plus `DensityPlacement` and `InstancePlacement` implementations that mirror the existing two code paths in `GrassScatter.Build` (procedural-density and authored-instances). NOT WIRED to anything yet; existing code paths in `GrassScatter` remain authoritative through R4.

R1 is additive-only. Demo render behavior is byte-identical to baseline.

## Scope

**IN:**
- New file `Runtime/IScatterPlacement.cs` — interface definition (one method: `Build(...)`).
- New file `Runtime/DensityPlacement.cs` — strategy mirroring procedural code path from current `GrassScatter.Build`.
- New file `Runtime/InstancePlacement.cs` — strategy mirroring authored-instances code path (currently `GrassScatter.BuildFromAuthored` or equivalent).
- Pre-flight grep for legacy `GrassLayer : ScatterLayer` alias (risk score 6); if found, surface in report and STOP.

**OUT:**
- Wiring (no consumer calls `layer.CreatePlacement()` yet — that lands in R5).
- Concrete subclass types (R2).
- `ScatterLayer` becoming abstract (R5).
- Any edit to `GrassScatter.cs`, `ScatterLayer.cs`, or `ScatterField.cs`.

## File Ownership

| File | Action | Notes |
|---|---|---|
| Runtime/IScatterPlacement.cs | CREATE | Single method: `GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler)`. Internal interface; namespace matches existing runtime. |
| Runtime/DensityPlacement.cs | CREATE | `internal sealed class DensityPlacement : IScatterPlacement`. Holds `private readonly ScatterLayer layer` (NOT `DensityScatterLayer` yet — that type does not exist until R2). Body lifted verbatim from current `GrassScatter.Build` procedural branch. |
| Runtime/InstancePlacement.cs | CREATE | `internal sealed class InstancePlacement : IScatterPlacement`. Holds `private readonly ScatterLayer layer`. Body lifted verbatim from current `GrassScatter.BuildFromAuthored` (authored-instances branch). |

## Step-by-Step Tasks

1. **Pre-flight grep (read-only):**
   - `grep -rn "class GrassLayer" Assets/` — confirm no `GrassLayer : ScatterLayer` alias still in use. If found, STOP and report.
   - `grep -rn "GrassScatter.Build" Assets/` — locate current `Build`/`BuildFromAuthored` entry points.
2. **Read `Runtime/GrassScatter.cs`** — identify the procedural vs authored branch boundaries; note local variables, signatures, and any private helpers shared between branches (`BuildFieldBounds`, etc.). Shared helpers stay in `GrassScatter` for now.
3. **Author `Runtime/IScatterPlacement.cs`:**
   - `#nullable enable`.
   - Namespace identical to `ScatterLayer.cs` namespace.
   - One method: `GrassScatterResult Build(Vector3 origin, InstanceBatchPool pool, ISurfaceSampler sampler);`
   - Add XML doc comment summarizing strategy responsibilities (density vs authored).
4. **Author `Runtime/DensityPlacement.cs`:**
   - `#nullable enable`.
   - `internal sealed class DensityPlacement : IScatterPlacement`.
   - Single readonly field `private readonly ScatterLayer layer;` with `this.` member-access. Use `ScatterLayer` (base) NOT `DensityScatterLayer`; that subclass does not exist until R2.
   - Constructor takes `ScatterLayer layer` and assigns.
   - `Build(...)` body = exact copy of the procedural branch from `GrassScatter.Build`. Reference `this.layer.X` for layer fields.
5. **Author `Runtime/InstancePlacement.cs`:**
   - Same pattern as `DensityPlacement` but body lifted from authored-instances branch.
6. **Match code conventions** per `.claude/rules/code-conventions-unity.md`: camelCase private fields (NO underscore), mandatory `this.` prefix, `#nullable enable` in all 3 new files.
7. **Match assembly definition.** New `Runtime/*.cs` files belong to the runtime asmdef (NOT editor). Verify by reading existing `Runtime/*.cs` namespaces and assembly tags.
8. **Write `phase-1-report.md`** stating: files created, pre-flight grep result, code paths mirrored line-by-line (note any divergence and justification).

## Verification Gate (main-loop runs after teammate exits)

1. `mcp__UnityMCP__set_active_instance unity_instance="GrassInteract@de203215"` — FIRST MCP call.
2. `refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)`.
3. `read_console(types=[Error, Warning], count=30)` — 0 NEW project errors.
4. `execute_menu_item menu_path="Tools/GrassInteract/Self-Test/RebuildLayer Parity"` — no `[Parity] ERROR`.
5. Screenshot game-view -> `plans/scatter-layer-placement-split/screenshots/phase-1-render.png` — visual parity vs `plans/authored-instance-scatter-editor/screenshots/phase-5-before.png`.
6. Asset-presence check: SKIP (R1 does not touch assets).

## Exit Criteria

- 3 new files compile cleanly (R1 adds zero errors, zero new warnings beyond pre-existing).
- Demo still renders byte-identical (no consumer wired to new types yet, so this is mechanical).
- Parity harness PASS.
- `phase-1-report.md` written.

## Rollback Plan

R1 is purely additive — rollback = delete the 3 new files. No mutations to existing code. Zero risk to baseline.

## Anti-Stall Guard Reminders

- **First MCP call = `set_active_instance unity_instance="GrassInteract@de203215"`.** Multi-instance host; wrong instance corrupts unrelated project state.
- **No progress narration.** Read → edit → next.
- **150K commit checkpoint** (per `agent-completion-discipline.md`): if context reaches ~150K mid-phase, STOP, write `phase-1-report.md` (status: in-progress + remaining tasks), exit. Main-loop resumes with fresh context.
- **No Unity restart, no `Assets/Reimport All`** (per `unity-forbidden-operations.md`). `refresh_unity` only.
- **Edits-only-in-subagent.** Do NOT run the verification gate yourself — that is main-loop responsibility AFTER you exit.
