# Plan Report

- Task ID: 2026-06-06-unity-agent-conventions
- Status: approved-draft
- Phase: plan
- Date: 2026-06-06
- Based On: brainstorm/2026-06-06-unity-agent-conventions.md

## Approved Input Summary
Implement the approved repo-local opencode customization for generic Unity conventions by adding one shared conventions source, aligning the builder/runtime/editor/tester/reviewer prompts to it, updating current workflow artifacts, and drafting a build report.

## Scope
Create or update:
- `.opencode/skills/unity-conventions/SKILL.md`
- `.opencode/agents/builder.md`
- `.opencode/agents/unity-runtime-developer.md`
- `.opencode/agents/unity-editor-developer.md`
- `.opencode/agents/unity-tester.md`
- `.opencode/agents/unity-reviewer.md`
- `.opencode/opencode.json` only if minimal wiring is required
- `brainstorm/2026-06-06-unity-agent-conventions.md`
- `brainstorm/current.md`
- `plan/2026-06-06-unity-agent-conventions.md`
- `plan/current.md`
- `plans/2026-06-06-unity-agent-conventions.md`
- `plans/current-plan.md`
- `build/2026-06-06-unity-agent-conventions.md`
- `build/current.md`

## Out Of Scope
- runtime or editor feature code under `Assets/`
- package dependency changes
- unrelated opencode customization
- changes to `brainstormer` or `planner` prompts
- non-workflow repo refactors

## Repo Areas Affected
- `.opencode/skills/`
- `.opencode/agents/`
- `.opencode/opencode.json` if strictly necessary
- `brainstorm/`
- `plan/`
- `plans/`
- `build/`

## Implementation Track
- Add one repo-local SSOT for generic Unity conventions.
- Keep the charter generic and not derived from current repo code.
- Update targeted agent prompts to load or follow that SSOT and enforce block/return behavior.
- Update current artifact pointers so `plans/current-plan.md` matches this task.
- Draft the build report after implementation.

## Risks
- Duplicating conventions across agents instead of using one shared source.
- Accidentally treating stack guidance as permission for package or architecture changes.
- Leaving current workflow artifacts pointed at the previous task.
- Introducing unnecessary `opencode.json` wiring changes.

## Verification Strategy
- Confirm the shared conventions source exists under `.opencode/skills/` with valid frontmatter.
- Confirm the five targeted agent prompts reference the shared conventions source and enforce the approved blockers.
- Confirm the charter remains generic and repo-agnostic.
- Confirm `.opencode/opencode.json` is only changed if wiring is actually needed.
- Confirm `brainstorm/current.md`, `plan/current.md`, and `plans/current-plan.md` point to this task.
- Confirm a draft build report exists under `build/`.

## Acceptance Criteria
- One shared conventions source exists under `.opencode/skills/` and acts as the SSOT for generic Unity conventions.
- `builder`, `unity-runtime-developer`, `unity-editor-developer`, `unity-tester`, and `unity-reviewer` align to that source with concise prompts.
- Agent prompts enforce blocking or return behavior for runtime/editor boundary violations, structure conflicts needing broader rework, and material repo/convention conflicts.
- `.opencode/opencode.json` is changed only if minimal wiring is required.
- `plans/current-plan.md` matches this approved task.
- Brainstorm, plan, and build artifacts exist for `2026-06-06-unity-agent-conventions` and current pointers are updated where appropriate.

## Handoff To Builder
Builder must implement only from:
- `plans/current-plan.md`

The canonical plan for this task is:
- `plans/2026-06-06-unity-agent-conventions.md`

## Approval
- User approved: yes
- Approval note: Repo-local Unity agent conventions customization approved.
