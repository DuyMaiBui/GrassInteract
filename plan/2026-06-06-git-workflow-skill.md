# Plan Report

- Task ID: 2026-06-06-git-workflow-skill
- Status: approved-draft
- Phase: plan
- Date: 2026-06-06
- Based On: brainstorm/2026-06-06-git-workflow-skill.md

## Approved Input Summary
Implement the approved repo-local generic git-workflow skill by adding the reusable skill under `.opencode/skills/`, updating workflow artifacts and current pointers for the task, drafting the build report, and leaving `.opencode/opencode.json` unchanged unless minimal wiring is truly required.

## Scope
Create or update:
- `.opencode/skills/git-workflow/SKILL.md`
- `.opencode/opencode.json` only if minimal wiring is required
- `brainstorm/2026-06-06-git-workflow-skill.md`
- `brainstorm/current.md`
- `plan/2026-06-06-git-workflow-skill.md`
- `plan/current.md`
- `plans/2026-06-06-git-workflow-skill.md`
- `plans/current-plan.md`
- `build/2026-06-06-git-workflow-skill.md`
- `build/current.md`

## Out Of Scope
- app code under `Assets/`
- Unity runtime or editor feature implementation
- agent prompt changes
- automatic Git commits, pushes, merges, rebases, or PR creation flows
- unrelated opencode customization

## Repo Areas Affected
- `.opencode/skills/`
- `.opencode/opencode.json` if strictly necessary
- `brainstorm/`
- `plan/`
- `plans/`
- `build/`

## Runtime Track
- None.
- This task does not modify runtime code.

## Editor Track
- Add the reusable repo-local `git-workflow` skill.
- Keep the content guidance-oriented and repo-agnostic.
- Encode the approved Git branch, commit, PR, and Unity safety guidance.
- Update workflow artifacts and current pointers to reflect this task.

## Test Track
- Validate that the skill frontmatter is present and matches the folder name.
- Validate that the skill content keeps explicit confirmation requirements for push, PR creation, squash, merge, and rebase.
- Validate that the skill does not authorize automatic commit, push, merge, or rebase behavior.
- Validate that current workflow artifacts all point to `2026-06-06-git-workflow-skill`.
- Validate that `.opencode/opencode.json` stays unchanged unless minimal wiring is actually needed.

## Risks
- Accidentally making the skill repo-specific instead of reusable.
- Accidentally implying automatic Git actions are allowed.
- Updating current pointers incompletely.
- Introducing unnecessary `opencode.json` changes.
- Omitting Unity `.meta` or asset-move safety rules.

## Verification Strategy
- Confirm `.opencode/skills/git-workflow/SKILL.md` exists with valid frontmatter.
- Confirm the skill contains the approved branch rules, commit rules, PR rules, explicit confirmation requirements, and Unity safety guardrails.
- Confirm `.opencode/opencode.json` is unchanged if skill auto-discovery already covers `.opencode/skills/`.
- Confirm `brainstorm/current.md`, `plan/current.md`, `plans/current-plan.md`, and `build/current.md` all point to `2026-06-06-git-workflow-skill`.
- Confirm the draft build report records the unchanged-config decision and limited verification scope accurately.

## Acceptance Criteria
- `.opencode/skills/git-workflow/SKILL.md` exists and is generic and reusable across repos.
- The skill is guidance-oriented only and does not authorize automatic commits, pushes, merges, rebases, or combined PR-create-and-merge flows.
- The skill requires explicit user confirmation for push, PR creation, squash, merge, and rebase.
- The skill uses only the approved branch types, branch naming format, commit format, default commit-type behavior, and PR title format.
- The skill includes the approved Unity Git block and warning conditions.
- `.opencode/opencode.json` changes only if minimal wiring is required.
- Workflow artifacts and current pointers reflect `2026-06-06-git-workflow-skill`.
- A draft build report exists under `build/` for this task.

## Handoff To Builder
Builder must implement only from:
- `plans/current-plan.md`

The canonical plan for this task is:
- `plans/2026-06-06-git-workflow-skill.md`

## Approval
- User approved: yes
- Approval note: Repo-local generic git-workflow skill implementation approved.
