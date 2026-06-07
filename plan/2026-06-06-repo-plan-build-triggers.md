# Plan Report

- Task ID: 2026-06-06-repo-plan-build-triggers
- Status: approved-draft
- Phase: plan
- Date: 2026-06-06
- Based On: brainstorm/2026-06-06-repo-plan-build-triggers.md

## Approved Input Summary
Implement the approved repo-local planning and build trigger behavior for this repo by adding repo-local English trigger coverage and repo-local `/plan` and `/build` commands through `.opencode/opencode.json`, while preserving clarification questions, direct-build confirmation, current plan auto-promotion, and build-current update behavior.

## Scope
Create or update:
- `.opencode/skills/brainstormer/SKILL.md`
- `.opencode/skills/planner/SKILL.md`
- `.opencode/skills/builder/SKILL.md`
- `.opencode/agents/brainstormer.md`
- `.opencode/agents/planner.md`
- `.opencode/agents/builder.md`
- `.opencode/opencode.json`

## Out Of Scope
- app code under `Assets/`
- Unity runtime or editor feature implementation unrelated to workflow entry
- global or user-level opencode customization
- unrelated workflow refactors
- direct build without explicit user confirmation

## Repo Areas Affected
- `.opencode/skills/`
- `.opencode/agents/`
- `.opencode/opencode.json`
- repo-local workflow artifact behavior for `plan/`, `plans/`, and `build/`

## Runtime Track
- None.
- This task does not modify runtime code.

## Editor Track
- Implement the repo-local planning and build trigger customization.
- Define repo-local `/plan` and `/build` through `.opencode/opencode.json`.
- Preserve clarification and approval safety in the phase prompts.
- Encode automatic current-plan promotion for newly created plans.
- Encode current build artifact updates when build starts.

## Test Track
- Validate that the approved English planning triggers appear in repo-local workflow files only.
- Validate that the approved English build triggers appear in repo-local workflow files only.
- Validate that repo-local `/plan` and `/build` are defined through `.opencode/opencode.json`.
- Validate that planning triggers still allow unresolved clarification questions.
- Validate that build asks whether to build directly or plan first when no approved plan exists.
- Validate that direct build requires explicit user confirmation.
- Validate that new plans auto-promote to `plan/current.md` and `plans/current-plan.md`.
- Validate that build-start behavior updates current build artifacts.
- Validate that no app code or global customization is touched.

## Risks
- Accidentally leaking the behavior into global or user-level configuration.
- Treating planning triggers as permission to skip unresolved clarification.
- Allowing direct build without explicit confirmation when no approved plan exists.
- Missing current-plan auto-promotion for newly created plans.
- Missing current build artifact updates when build starts.
- Assuming unsupported repo-local command files instead of the repo-local `.opencode/opencode.json` command surface.

## Verification Strategy
- Confirm repo-local planning trigger coverage includes `create plan`, `make plan`, and `plan it`.
- Confirm repo-local build trigger coverage includes `let build`, `build it`, `implement now`, and `start build`.
- Confirm repo-local `/plan` and `/build` are defined through `.opencode/opencode.json`.
- Confirm planning-trigger handling still asks questions when clarification is unresolved.
- Confirm build handling asks whether to build directly or plan first when no approved plan exists.
- Confirm any direct-build path requires explicit user confirmation.
- Confirm plan creation behavior promotes `plan/current.md` and `plans/current-plan.md`.
- Confirm build-start behavior updates current build artifacts.
- Confirm `.opencode/opencode.json` contains only the approved repo-local `/plan` and `/build` command-surface changes.
- Confirm no app code or non-repo-local customization is changed.

## Acceptance Criteria
- Repo-local planning trigger coverage exists for `create plan`, `make plan`, and `plan it`.
- Repo-local build trigger coverage exists for `let build`, `build it`, `implement now`, and `start build`.
- Repo-local `/plan` and `/build` commands are defined through `.opencode/opencode.json`.
- Planning triggers count as approval to plan, but unresolved clarification still requires questions.
- If no approved plan exists, build asks whether to build directly or plan first.
- Direct build occurs only after explicit user confirmation.
- Newly created plans auto-promote to `plan/current.md` and `plans/current-plan.md`.
- Current build artifacts update when build starts.
- The implementation stays repo-local and does not touch app code.

## Handoff To Builder
Builder must implement only from:
- `plans/current-plan.md`

The canonical plan for this task is:
- `plans/2026-06-06-repo-plan-build-triggers.md`

## Approval
- User approved: yes
- Approval note: Repo-local planning/build trigger implementation approved.
