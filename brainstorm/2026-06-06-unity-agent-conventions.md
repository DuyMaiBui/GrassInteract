# Brainstorm Report

- Task ID: 2026-06-06-unity-agent-conventions
- Status: approved-draft
- Phase: brainstorm
- Date: 2026-06-06
- Slug: unity-agent-conventions

## Request Summary
Implement the approved repo-local opencode customization for generic Unity conventions.

## Goal
Establish one repo-local shared source of truth for generic Unity conventions, align the Unity workflow agents to it, and update workflow artifacts so the current canonical plan matches the approved task.

## Constraints
- Work only in this repo.
- Keep changes inside `.opencode/` plus workflow artifact docs and build report files.
- Keep conventions generic and not derived from current repo code.
- Do not perform unrelated refactors.
- Update `.opencode/opencode.json` only if minimal wiring is required.
- Do not alter `brainstormer` or `planner` prompts unless absolutely required.

## Approved Shared Charter
### Code Conventions
- `#nullable enable`
- XML docs only for interface, public, or protected API
- `this.` qualification
- `internal` by default
- `sealed` by default
- `static` only for stateless helpers
- one top-level type per file
- no `UnityEditor` in runtime code
- `#if UNITY_EDITOR` only for small hooks
- avoid per-frame allocations in hot paths
- avoid alloc-heavy LINQ or ZLinq in hot paths

### Structure Conventions
- full feature modules
- per-feature asmdef by default
- namespaces: `Project`, `Project.Feature`, `Project.Editor`, `Project.Editor.Feature`
- runtime folders: `Api`, `Data`, `Services`, `Systems`, `Presentation`
- thin `MonoBehaviour`
- `ScriptableObject` for config and authoring-time behavior
- `Services` = business or app logic
- `Systems` = ticking or orchestration
- `Api` = contracts, facades, and events only

### Stack Guidance
- Unity 6
- URP
- Odin
- VContainer
- MessagePipe
- UniTask
- ZLinq
- LitMotion

### Usage Rules
- VContainer broad use
- MessagePipe okay inside a feature
- UniTask runtime only by default
- Odin broad use but core runtime architecture should not depend on it
- ZLinq okay generally, never in hot paths
- LitMotion for UI, presentation, and gameplay animation

### Agent Blockers
Agents must block and return on:
- runtime/editor boundary violations
- structure conflicts needing broader rework
- repo reality materially conflicting with the approved conventions

## User Decisions
- Use one shared conventions source under `.opencode/skills/` as the SSOT.
- Keep agent duplication concise and point targeted agents at the shared source.
- Update the builder, runtime developer, editor developer, tester, and reviewer prompts only.
- Keep `brainstormer` and `planner` prompts unchanged.
- Update workflow artifacts for the new task using date `2026-06-06` and slug `unity-agent-conventions`.
- Create a draft build report for the task after implementation.
- Change `.opencode/opencode.json` only if minimal wiring proves necessary.

## Unresolved Items
None.

## Handoff To Planner
Produce a canonical plan that:
- adds the shared Unity conventions source under `.opencode/skills/`
- aligns the specified agent prompts to that source
- updates current workflow artifacts for this task
- creates a draft build report under `build/`

## Approval
- User approved: yes
- Approval note: Shared conventions charter and task scope approved for planning and implementation.
