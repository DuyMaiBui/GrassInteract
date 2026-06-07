# Canonical Plan

- Task ID: 2026-06-06-repo-plan-build-triggers
- Status: approved
- Date: 2026-06-06
- Based On Brainstorm: brainstorm/2026-06-06-repo-plan-build-triggers.md
- Based On Plan Report: plan/2026-06-06-repo-plan-build-triggers.md

## Approved Scope
Implement the approved repo-local planning and build trigger task by:
- adding repo-local planning trigger coverage for `create plan`, `make plan`, and `plan it`
- adding repo-local build trigger coverage for `let build`, `build it`, `implement now`, and `start build`
- providing repo-local `/plan` and `/build` commands through `.opencode/opencode.json`
- treating planning triggers as approval to plan while still asking questions when clarification is unresolved
- asking whether to build directly or plan first when no approved plan exists
- allowing direct build only after explicit user confirmation
- auto-promoting newly created plans to `plan/current.md` and `plans/current-plan.md`
- updating current build artifacts when build starts

## Out Of Scope
- app code changes under `Assets/`
- global or user-level opencode customization
- unrelated workflow refactors
- direct build without explicit user confirmation
- feature implementation unrelated to this repo-local workflow behavior

## Guardrails
- Keep the implementation repo-local only.
- Change only repo-local opencode workflow files.
- Do not touch app code.
- Do not rely on global command or trigger configuration.
- Do not treat planning triggers as permission to skip unresolved questions.
- Do not allow direct build without explicit user confirmation when no approved plan exists.
- If repo reality shows that `.opencode/opencode.json` cannot provide the required repo-local `/plan` and `/build` surface, stop and return to planner instead of inventing unsupported command files or global wiring.

## Execution Steps

### Step 1
- Status: pending
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/skills/brainstormer/SKILL.md`
  - `.opencode/skills/planner/SKILL.md`
  - `.opencode/agents/brainstormer.md`
  - `.opencode/agents/planner.md`
- Action:
  - Encode repo-local planning trigger coverage for `create plan`, `make plan`, and `plan it`.
  - Treat planning triggers and the repo-local `/plan` entry defined through `.opencode/opencode.json` as approval to enter planning.
  - Preserve unresolved clarification questioning instead of silently skipping to a final plan.
  - Encode new-plan auto-promotion to `plan/current.md` and `plans/current-plan.md`.
- Verification:
  - The approved planning triggers are present in the repo-local planning workflow surfaces.
  - Unresolved clarification still routes to questions.
  - Plan auto-promotion behavior is explicit.
- Stop If:
  - The planning behavior would require global configuration.
  - The approved trigger behavior would skip unresolved clarification.

### Step 2
- Status: pending
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/skills/builder/SKILL.md`
  - `.opencode/agents/builder.md`
- Action:
  - Encode repo-local build trigger coverage for `let build`, `build it`, `implement now`, and `start build`.
  - When an approved plan exists, route build to the current canonical plan.
  - When no approved plan exists, require the build flow to ask whether to build directly or plan first.
  - Require explicit user confirmation before any direct-build path.
  - Encode current build artifact updates for build start.
- Verification:
  - The approved build triggers are present in the repo-local build workflow surfaces.
  - No-plan fallback questioning is explicit.
  - Direct-build confirmation is explicit.
  - Build-start current artifact update behavior is explicit.
- Stop If:
  - The build path would allow direct implementation without explicit confirmation.

### Step 3
- Status: pending
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/opencode.json`
- Action:
  - Define repo-local `/plan` and `/build` command entry points through `.opencode/opencode.json`.
  - Keep command registration repo-local only.
- Verification:
  - `/plan` and `/build` are defined in `.opencode/opencode.json`.
  - No unsupported `.opencode/commands/*.md` wiring is introduced.
  - No global or user-level command wiring is introduced.
  - The `opencode.json` change is minimal and schema-valid.
- Stop If:
  - Repo-local `/plan` and `/build` cannot be defined through `.opencode/opencode.json` without unapproved global behavior.

### Step 4
- Status: pending
- Owner: unity-reviewer
- Target Files:
  - `.opencode/skills/brainstormer/SKILL.md`
  - `.opencode/skills/planner/SKILL.md`
  - `.opencode/skills/builder/SKILL.md`
  - `.opencode/agents/brainstormer.md`
  - `.opencode/agents/planner.md`
  - `.opencode/agents/builder.md`
  - `.opencode/opencode.json`
- Action:
  - Review the repo-local trigger and `.opencode/opencode.json` command implementation for scope drift or missing safety rules.
  - Confirm the approved trigger phrases, approval behavior, no-plan fallback, direct-build confirmation, plan auto-promotion, and build-current update rules all remain intact.
- Verification:
  - Planning triggers, build triggers, and the repo-local `.opencode/opencode.json` command mappings match the approved task.
  - Current plan promotion and build-current update behavior are covered.
  - No app code or global customization was changed.
- Stop If:
  - Missing confirmation gates, missing `.opencode/opencode.json` command coverage, or scope drift require replanning.

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

## Build Return Conditions
- planning trigger coverage is missing or not repo-local
- build trigger coverage is missing or not repo-local
- `/plan` or `/build` is missing from repo-local `.opencode/opencode.json`
- unresolved clarification is skipped by planning triggers
- build can proceed without plan choice or explicit direct-build confirmation
- current plan promotion or build-current update behavior is missing
- app code or global customization is changed

## Completion Rule
This task is complete only when the repo-local triggers and the repo-local `.opencode/opencode.json` command surface exist, clarification and confirmation rules are preserved, current artifact behavior matches the approved task, and no app code or global customization was changed.
