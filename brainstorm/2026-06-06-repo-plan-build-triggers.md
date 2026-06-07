# Brainstorm Report

- Task ID: 2026-06-06-repo-plan-build-triggers
- Status: approved-draft
- Phase: brainstorm
- Date: 2026-06-06
- Slug: repo-plan-build-triggers

## Request Summary
Add the approved repo-local planning and build triggers for this repo's opencode workflow, and promote the task into the workflow artifacts.

## Goal
Allow users to enter planning or build from approved English trigger phrases and repo-local `/plan` and `/build` commands while preserving clarification, approval, and direct-build safety rules.

## Constraints
- Repo-local only, not global.
- Repo-local `/plan` and `/build` commands must exist.
- Approved English planning triggers: `create plan`, `make plan`, `plan it`.
- Approved English build triggers: `let build`, `build it`, `implement now`, `start build`.
- Planning triggers count as approval to plan, but unresolved clarification still requires questions.
- If no approved plan exists, build must ask whether to build directly or plan first.
- Direct build is allowed only after explicit user confirmation.
- New plans must auto-promote to current.
- Current build artifacts must update when build starts.
- Do not touch app code.
- Do not add global opencode customization.

## Questions Asked
- None during approval handoff; the trigger phrases, fallback behavior, and repo-local scope were already decided.

## Options Considered
### Option A
Use global or user-level planning/build customization.

Pros:
- fewer repo files

Cons:
- violates repo-local scope
- harder to keep behavior versioned with the repo

### Option B
Implement repo-local English triggers plus repo-local `/plan` and `/build` commands in this repo's opencode workflow.

Pros:
- matches the approved repo-local scope
- keeps behavior versioned with the repo
- supports repo-local current artifact rules

Cons:
- requires coordinated prompt and command updates

### Option C
Allow build triggers to start direct implementation whenever no approved plan exists.

Pros:
- faster initial flow

Cons:
- removes the approved safety gate
- conflicts with explicit confirmation requirements

## Recommendation
Use repo-local trigger and repo-local command customization only, keep planning triggers as approval to plan rather than approval to skip questions, and require explicit user confirmation before any direct build without an approved plan.

## User Decisions
- Keep the task repo-local only.
- Support planning triggers: `create plan`, `make plan`, `plan it`.
- Support build triggers: `let build`, `build it`, `implement now`, `start build`.
- Provide repo-local `/plan` and `/build` commands.
- Treat planning triggers as approval to plan, not as permission to ignore unresolved clarification.
- When no approved plan exists, build must ask whether to build directly or plan first.
- Allow direct build only after explicit user confirmation.
- Auto-promote newly created plans to current.
- Update current build artifacts when build starts.
- Do not touch app code.

## Unresolved Items
None.

## Handoff To Planner
Produce a canonical plan that:
- updates only repo-local opencode workflow files
- adds repo-local `/plan` and `/build` command coverage
- encodes the approved English planning and build triggers
- preserves clarification questions and direct-build confirmation rules
- auto-promotes new plans to current plan artifacts
- updates current build artifacts when build starts
- leaves app code untouched

## Approval
- User approved: yes
- Approval note: Repo-local planning/build trigger behavior and command coverage approved for planning.
