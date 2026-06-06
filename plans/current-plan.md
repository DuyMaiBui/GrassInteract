# Canonical Plan

- Task ID: 2026-06-06-opencode-unity-workflow
- Status: completed
- Date: 2026-06-06
- Based On Brainstorm: brainstorm/2026-06-06-opencode-unity-workflow.md
- Based On Plan Report: plan/2026-06-06-opencode-unity-workflow.md

## Approved Scope
Create the opencode workflow system for this Unity project with:
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
- project-level workflow artifact folders:
  - `brainstorm/`
  - `plan/`
  - `build/`
  - `plans/`
- `.opencode/` used only for:
  - skills
  - agents
  - `opencode.json`

## Out Of Scope
- gameplay/runtime feature development
- Unity scene content unrelated to workflow verification
- package changes
- non-workflow repo refactors

## Guardrails
- Builder may decide code details only.
- Builder may not change scope.
- Builder may not change architecture.
- Builder may not change acceptance criteria.
- Builder must return to planner on ambiguity or scope conflict.
- Planner is the routing authority for runtime/editor/test/review roles.
- Reviewer may fail the build.
- All decision questions must use the `question` tool.
- The exact provided `<system-reminder>` block must be added to all 3 skills only.

## Execution Steps

### Step 1
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/skills/brainstormer/SKILL.md`
  - `.opencode/skills/planner/SKILL.md`
  - `.opencode/skills/builder/SKILL.md`
- Action:
  - Create the 3 skill files.
  - Include correct frontmatter and descriptions.
  - Include the exact approved `<system-reminder>` block in all 3 skills only.
  - Encode report-writing, approval, and question-tool rules.
- Verification:
  - Frontmatter name matches folder name.
  - Description is trigger-friendly and scoped.
  - Reminder block is exact and present in all 3 skills only.
  - Builder skill requires approved canonical plan usage.
- Stop If:
  - Reminder block wording is changed.
  - Skill behavior conflicts with approved workflow.

### Step 2
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/agents/brainstormer.md`
  - `.opencode/agents/planner.md`
  - `.opencode/agents/unity-runtime-developer.md`
  - `.opencode/agents/unity-editor-developer.md`
  - `.opencode/agents/unity-tester.md`
  - `.opencode/agents/unity-reviewer.md`
- Action:
  - Create the 6 agent files with approved responsibilities.
  - Keep runtime/editor separation explicit.
  - Make tester responsible for Unity MCP compile, logs, tests, prefab/scene setup when needed for validation.
  - Make reviewer able to fail the build.
- Verification:
  - Agent names and descriptions are valid.
  - Runtime agent does not own editor work.
  - Editor agent does not define runtime implementation.
  - Reviewer prompt clearly allows failure on serious findings.
- Stop If:
  - Agent responsibilities drift from approved structure.
  - Unity MCP assumptions exceed approved tester scope.

### Step 3
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/opencode.json`
- Action:
  - Create project opencode config.
  - Register skills and agents.
  - Use schema-valid config structure.
  - Align permissions with approved workflow.
- Verification:
  - `$schema` is present.
  - Config shape matches opencode schema.
  - Skill and agent names match created files.
- Stop If:
  - Schema uncertainty exists.
  - Permission design requires unapproved behavior changes.

### Step 4
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `brainstorm/`
  - `plan/`
  - `build/`
  - `plans/`
- Action:
  - Create the project-level workflow artifact folders.
  - Do not create fake finalized reports unless explicitly intended.
- Verification:
  - Folder layout matches approved structure.
  - Future reports and plans have valid destinations.
- Stop If:
  - User wants template files pre-created differently.

### Step 5
- Status: done
- Owner: unity-tester
- Target Files:
  - `.opencode/skills/brainstormer/SKILL.md`
  - `.opencode/skills/planner/SKILL.md`
  - `.opencode/skills/builder/SKILL.md`
  - `.opencode/agents/*.md`
  - `.opencode/opencode.json`
- Action:
  - Validate naming consistency, config structure, and workflow rule consistency.
  - Verify tester instructions align with Unity MCP compile/log/test/prefab/scene validation role.
- Verification:
  - No naming mismatches.
  - No obvious invalid config keys.
  - No contradiction between skills and agents.
- Stop If:
  - Validation reveals workflow-level contradictions requiring replanning.

### Step 6
- Status: done
- Owner: unity-reviewer
- Target Files:
  - `.opencode/skills/brainstormer/SKILL.md`
  - `.opencode/skills/planner/SKILL.md`
  - `.opencode/skills/builder/SKILL.md`
  - `.opencode/agents/brainstormer.md`
  - `.opencode/agents/planner.md`
  - `.opencode/agents/unity-runtime-developer.md`
  - `.opencode/agents/unity-editor-developer.md`
  - `.opencode/agents/unity-tester.md`
  - `.opencode/agents/unity-reviewer.md`
  - `.opencode/opencode.json`
- Action:
  - Review for contradictions, permission risks, missing gates, and role drift.
  - Confirm the implemented workflow still enforces:
    - explicit approval before planning
    - plan-only building
    - reviewer authority to fail
- Verification:
  - No serious workflow contradiction remains.
  - Handoff rules are enforceable.
- Stop If:
  - Serious findings require return to planner.
  - Build should fail due to unresolved workflow flaws.

## Acceptance Criteria
- `.opencode/skills/` contains exactly the 3 approved skills.
- `.opencode/agents/` contains exactly the 6 approved agents.
- `.opencode/opencode.json` exists and is schema-valid.
- All reports are project-level:
  - `brainstorm/`
  - `plan/`
  - `build/`
- Canonical plan is project-level:
  - `plans/`
- The exact `<system-reminder>` block is present in all 3 skills only.
- Builder uses approved canonical plan only.
- Planner returns to brainstorm on unresolved decisions.
- Reviewer can fail the build.
- Decision questions are routed through the `question` tool.

## Build Return Conditions
- Config schema mismatch
- artifact path mismatch
- contradiction between skill rules and agent rules
- tester instructions conflict with approved Unity MCP scope
- workflow ambiguity that would force re-scoping during build

## Completion Rule
This task is complete only when all required files and folders exist, validation passes, and reviewer does not fail the workflow implementation.
