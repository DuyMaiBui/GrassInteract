# Plan Report

- Task ID: 2026-06-06-opencode-unity-workflow
- Status: approved-draft
- Phase: plan
- Date: 2026-06-06
- Based On: brainstorm/2026-06-06-opencode-unity-workflow.md

## Approved Input Summary
Create an opencode workflow system for this Unity repo with:
- 3 skills
- 6 agents
- project-root workflow artifacts
- canonical plan driven build execution
- explicit approval gates
- embedded tester/reviewer results in build report
- question-tool-based decision collection
- exact reminder block included in all 3 skills only

## Scope
Create the opencode workflow structure and configuration for:
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

Create project-level artifact destinations for:
- `brainstorm/`
- `plan/`
- `build/`
- `plans/`

## Out Of Scope
- implementing game/runtime features under `Assets/`
- changing Unity package dependencies
- adding unrelated opencode customization
- creating unrelated MCP infrastructure
- changing repo code outside workflow setup

## Repo Areas Affected
- `.opencode/skills/`
- `.opencode/agents/`
- `.opencode/opencode.json`
- project root folders for reports and plans

## Runtime Track
- No runtime gameplay code is planned for this task.
- Runtime specialist is present as a workflow role only.

## Editor Track
- Create all workflow files and config.
- This task is effectively owned by the editor/tooling side because it configures opencode and project workflow files.

## Test Track
- Validate agent/skill naming consistency.
- Validate config schema shape.
- Validate prompt consistency with approved rules.
- Validate Unity tester instructions match Unity MCP expectations.

## Risks
- Prompt contradictions between skills and agents.
- Invalid `opencode.json` shape.
- Reminder block inconsistency across skills.
- Artifact path mismatches between reports, plans, and prompts.
- Builder permissions or instructions drifting beyond approved scope.

## Verification Strategy
- Check all skill frontmatter names match folder names.
- Check all agent names and descriptions are consistent.
- Check config uses project skill/agent locations correctly.
- Check builder instructions require canonical plan usage.
- Check planner instructions force return to brainstorm on unresolved decisions.
- Check reviewer instructions allow failing the build.
- Check question-tool rule is reflected in the skills.

## Acceptance Criteria
- All 3 skills exist with correct names and descriptions.
- All 6 agents exist with correct role boundaries.
- `opencode.json` is schema-valid.
- All workflow artifact paths point to project-level folders:
  - `brainstorm/`
  - `plan/`
  - `build/`
  - `plans/`
- The exact `<system-reminder>` block is included in all 3 skills only.
- Builder is constrained to approved plan execution only.
- Planner is constrained to approved brainstorm decisions only.
- Tester behavior matches approved Unity MCP scope.
- Reviewer can fail the build.

## Handoff To Builder
Builder must implement only from:
- `plans/current-plan.md`

The canonical plan for this task is:
- `plans/2026-06-06-opencode-unity-workflow.md`

## Approval
- User approved: yes
- Approval note: Plan structure and artifact location approved.
