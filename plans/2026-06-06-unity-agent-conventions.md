# Canonical Plan

- Task ID: 2026-06-06-unity-agent-conventions
- Status: completed
- Date: 2026-06-06
- Based On Brainstorm: brainstorm/2026-06-06-unity-agent-conventions.md
- Based On Plan Report: plan/2026-06-06-unity-agent-conventions.md

## Approved Scope
Implement the approved repo-local opencode customization for generic Unity conventions by:
- adding one repo-local shared conventions source under `.opencode/skills/`
- aligning these agent prompts to that shared source:
  - `.opencode/agents/builder.md`
  - `.opencode/agents/unity-runtime-developer.md`
  - `.opencode/agents/unity-editor-developer.md`
  - `.opencode/agents/unity-tester.md`
  - `.opencode/agents/unity-reviewer.md`
- updating workflow artifact docs so `plans/current-plan.md` matches this task
- changing `.opencode/opencode.json` only if minimal wiring is required
- creating a draft build report under `build/`

## Out Of Scope
- runtime or editor feature development under `Assets/`
- package dependency changes
- unrelated prompt or repo refactors
- changes to `brainstormer` or `planner` prompts
- conventions derived from current repo code

## Guardrails
- Keep conventions generic.
- Keep changes inside `.opencode/` plus workflow artifact docs and report files only.
- Prefer one shared source of truth and keep agent duplication concise.
- Do not use stack guidance as permission for package or architecture changes.
- Agents must block and return on runtime/editor boundary violations, structure conflicts needing broader rework, or repo reality materially conflicting with the approved conventions.

## Execution Steps

### Step 1
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/skills/unity-conventions/SKILL.md`
- Action:
  - Create the repo-local shared conventions source.
  - Encode the approved generic Unity code, structure, stack, usage, and blocker guidance.
  - Keep the document repo-agnostic and suitable as the SSOT.
- Verification:
  - Skill frontmatter name matches the folder.
  - Description is valid and trigger-friendly.
  - Charter content remains generic and not derived from current repo code.
- Stop If:
  - The conventions drift into repo-specific implementation assumptions.

### Step 2
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/agents/builder.md`
  - `.opencode/agents/unity-runtime-developer.md`
  - `.opencode/agents/unity-editor-developer.md`
  - `.opencode/agents/unity-tester.md`
  - `.opencode/agents/unity-reviewer.md`
- Action:
  - Update the targeted prompts to align with the shared conventions source.
  - Keep duplicated guidance concise.
  - Encode the approved block and return behavior.
- Verification:
  - Each targeted prompt references the shared conventions source.
  - Boundary, structure, and repo-reality blockers are explicit.
  - Builder remains responsible for canonical-plan-only execution.
- Stop If:
  - Prompt changes expand scope beyond the approved agent set.

### Step 3
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/opencode.json`
- Action:
  - Inspect whether minimal wiring is required for the shared conventions source.
  - Leave the config unchanged if auto-discovery already covers the new skill.
- Verification:
  - No unnecessary config changes are introduced.
  - Any config change would remain schema-valid.
- Stop If:
  - Additional wiring would require behavior beyond the approved scope.

### Step 4
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `brainstorm/2026-06-06-unity-agent-conventions.md`
  - `brainstorm/current.md`
  - `plan/2026-06-06-unity-agent-conventions.md`
  - `plan/current.md`
  - `plans/2026-06-06-unity-agent-conventions.md`
  - `plans/current-plan.md`
- Action:
  - Create the workflow artifacts for this approved task.
  - Update current pointers so the builder can legitimately follow the current canonical plan.
- Verification:
  - Task IDs, date, and slug are consistent.
  - Current pointers reference this task.
  - The canonical plan matches the approved scope.
- Stop If:
  - Current artifact updates would contradict the approved task.

### Step 5
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `build/2026-06-06-unity-agent-conventions.md`
  - `build/current.md`
- Action:
  - Draft the build report after implementation.
  - Capture files changed, config wiring decision, validation summary, and reviewer outcome.
- Verification:
  - The build report exists under `build/`.
  - The report is clearly a draft.
  - The report matches the implemented scope.
- Stop If:
  - The report claims verification that was not actually performed.

### Step 6
- Status: done
- Owner: unity-reviewer
- Target Files:
  - `.opencode/skills/unity-conventions/SKILL.md`
  - `.opencode/agents/builder.md`
  - `.opencode/agents/unity-runtime-developer.md`
  - `.opencode/agents/unity-editor-developer.md`
  - `.opencode/agents/unity-tester.md`
  - `.opencode/agents/unity-reviewer.md`
  - `plans/current-plan.md`
  - `build/2026-06-06-unity-agent-conventions.md`
- Action:
  - Review the shared source, prompt alignment, current-plan state, and build report draft for contradictions or missing blockers.
- Verification:
  - No serious contradiction remains between the shared source and targeted agent prompts.
  - The current plan and build report reflect the implemented task.
- Stop If:
  - Unresolved blocker handling or artifact drift requires replanning.

## Acceptance Criteria
- `.opencode/skills/unity-conventions/SKILL.md` exists as the repo-local SSOT for generic Unity conventions.
- The five targeted agent prompts align to that shared source and stay concise.
- Block and return behavior is explicit for runtime/editor boundary violations, broader structure conflicts, and material repo/convention conflicts.
- `.opencode/opencode.json` is unchanged unless minimal wiring was truly required.
- `brainstorm/current.md`, `plan/current.md`, and `plans/current-plan.md` all match `2026-06-06-unity-agent-conventions`.
- A draft build report exists under `build/` for this task.

## Build Return Conditions
- shared conventions source is missing or repo-specific
- targeted prompts do not align with the shared source
- blocker behavior is missing or contradictory
- current workflow artifacts still point at the previous task
- config wiring is changed unnecessarily or invalidly

## Completion Rule
This task is complete only when the shared conventions source exists, the targeted prompts align to it, current workflow artifacts point at this task, config wiring remains minimal, and the draft build report is present.
