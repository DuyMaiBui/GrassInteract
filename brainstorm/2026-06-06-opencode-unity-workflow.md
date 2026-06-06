# Brainstorm Report

- Task ID: 2026-06-06-opencode-unity-workflow
- Status: approved-draft
- Phase: brainstorm
- Date: 2026-06-06
- Slug: opencode-unity-workflow

## Request Summary
Create an opencode workflow system for this Unity project with a simplified phase flow and specialized agents.

## Goal
Define a reliable workflow with explicit phase handoff:
- brainstorm
- plan
- build

The system should support Unity-focused implementation, testing, and review while preserving user control over decisions.

## Constraints
- This is a Unity project.
- Runtime and editor implementation must be split into separate developer agents.
- Builder must implement only from an approved plan.
- Brainstorm must ask for scope, stack, and decision details instead of deciding silently.
- Planning artifacts and all reports must live in project-level folders, not under `.opencode/`.
- All decision questions should be asked with the `question` tool.
- The exact provided `<system-reminder>` block must be added to all 3 skills only:
  - `brainstormer`
  - `planner`
  - `builder`
- Tester should use Unity MCP for:
  - compile verification
  - log inspection
  - test creation
  - test verification
  - prefab creation when needed for validation
  - scene creation/setup when needed for validation
- Reviewer can fail the build.

## Questions Asked
- What overall workflow shape should be used?
- How should planning work?
- How should the Unity tester behave?
- How strict should the builder be?
- Should Unity developer work be split?
- How should reports be stored?
- How should test and review results be recorded?
- What approval gate should exist between brainstorm and plan?
- How should runtime/editor agents be selected?
- What flexibility should builder have?
- Where should artifacts live?
- Where should the reminder block be added?

## Options Considered
### Option A
Use mostly skills and minimal agents.

Pros:
- simpler file count
- less prompt maintenance

Cons:
- weaker specialization
- less explicit handoff boundaries

Risk:
- workflow intent becomes too implicit

### Option B
Use mostly agents and very thin skills.

Pros:
- clearer specialist roles
- strong execution separation

Cons:
- weaker workflow guardrails
- less automatic phase guidance

Risk:
- implementation may drift from desired phase behavior

### Option C
Use hybrid workflow with 3 skills and 6 agents.

Pros:
- clear workflow guidance
- specialist execution roles
- strong handoff structure
- matches user's desired flow

Cons:
- more files to maintain
- requires careful prompt consistency

Risk:
- prompt contradictions if not designed carefully

## Recommendation
Use the hybrid structure with:
- 3 skills:
  - `brainstormer`
  - `planner`
  - `builder`
- 6 agents:
  - `brainstormer`
  - `planner`
  - `unity-runtime-developer`
  - `unity-editor-developer`
  - `unity-tester`
  - `unity-reviewer`

This best matches the requested simplified flow while preserving specialist Unity behavior.

## User Decisions
- Use a hybrid workflow.
- Use a custom planner only.
- Split Unity developer into runtime and editor agents.
- Use 3 skills:
  - `brainstormer`
  - `planner`
  - `builder`
- Use 6 agents:
  - `brainstormer`
  - `planner`
  - `unity-runtime-developer`
  - `unity-editor-developer`
  - `unity-tester`
  - `unity-reviewer`
- Require a report after each phase:
  - brainstorm
  - plan
  - build
- Store both current and history reports.
- Embed tester and reviewer results inside the build report.
- Require explicit user approval before moving from brainstorm to plan.
- Let planner decide whether runtime, editor, or both developers are needed.
- Allow builder flexibility for code details only.
- Use per-phase folders.
- Use one canonical plan file per feature.
- Force builder to stop on ambiguity or scope conflict.
- Force planner to return to brainstorm on unresolved decisions.
- Use file-level actionable plans.
- Use date-plus-slug task naming.
- Write draft reports first, then promote to current after approval.
- Track build progress by updating plan status during execution.
- Allow reviewer to fail the build.
- Move all reports and plan artifacts to project-level folders:
  - `brainstorm/`
  - `plan/`
  - `build/`
  - `plans/`
- Add the exact provided `<system-reminder>` block to all 3 skills only.
- Always ask decision questions with the `question` tool.

## Unresolved Items
None.

## Handoff To Planner
Create the full workflow system with the approved structure, artifact locations, rules, and prompts. Planning must produce:
- `plan/2026-06-06-opencode-unity-workflow.md`
- `plans/2026-06-06-opencode-unity-workflow.md`

## Approval
- User approved: yes
- Approval note: Brainstorm decisions approved for planning.
