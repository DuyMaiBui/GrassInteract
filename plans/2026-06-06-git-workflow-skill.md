# Canonical Plan

- Task ID: 2026-06-06-git-workflow-skill
- Status: completed
- Date: 2026-06-06
- Based On Brainstorm: brainstorm/2026-06-06-git-workflow-skill.md
- Based On Plan Report: plan/2026-06-06-git-workflow-skill.md

## Approved Scope
Implement the approved repo-local git-workflow skill task by:
- creating `.opencode/skills/git-workflow/SKILL.md` as a generic reusable skill
- promoting the task into workflow artifacts so current files reflect it
- updating `.opencode/opencode.json` only if minimal wiring is required
- creating the draft brainstorm, plan, canonical plan, and build artifacts for `2026-06-06-git-workflow-skill`
- updating the current workflow pointers so this is the active task

## Out Of Scope
- app code changes under `Assets/`
- agent prompt changes
- unrelated opencode customization
- automatic Git commits, pushes, merges, rebases, or PR creation behavior
- workflow refactors outside the requested skill and artifact updates

## Guardrails
- Keep changes within `.opencode/` skill or config files and workflow artifact or build report files only.
- Keep the skill generic and reusable across repos.
- Keep the skill guidance-oriented only.
- Require explicit user confirmation for push, PR creation, squash, merge, and rebase.
- Never combine PR creation and merge into one automatic flow.
- Use only the approved branch types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `hotfix`.
- Use branch naming `type/<short-slug>`.
- Use commit format `type(scope): short summary`.
- Keep the commit type aligned to the branch type by default.
- Generate commit messages and PR titles from actual inspected changes.
- Block on missing `.meta` files and incomplete Unity asset moves or renames.
- Warn on binaries without LFS and scene or prefab churn.

## Execution Steps

### Step 1
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/skills/git-workflow/SKILL.md`
- Action:
  - Create the repo-local `git-workflow` skill.
  - Encode the approved Git branch, commit, PR, and Unity safety guidance.
  - Keep the content generic, reusable, and guidance-only.
- Verification:
  - Skill frontmatter name matches the folder.
  - Description is valid and trigger-friendly.
  - The skill requires explicit confirmation for push, PR creation, squash, merge, and rebase.
  - The skill does not authorize automatic Git-changing actions.
- Stop If:
  - The skill drifts into repo-specific workflow assumptions.

### Step 2
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `.opencode/opencode.json`
- Action:
  - Inspect whether any minimal wiring is required for the new skill.
  - Leave the config unchanged if `.opencode/skills/` auto-discovery already covers the skill.
- Verification:
  - No unnecessary config changes are introduced.
  - Any config change would remain schema-valid.
- Stop If:
  - Wiring would require behavior beyond the approved scope.

### Step 3
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `brainstorm/2026-06-06-git-workflow-skill.md`
  - `brainstorm/current.md`
  - `plan/2026-06-06-git-workflow-skill.md`
  - `plan/current.md`
  - `plans/2026-06-06-git-workflow-skill.md`
  - `plans/current-plan.md`
- Action:
  - Create the workflow artifacts for this task.
  - Update current pointers so this task is the active brainstorm, plan, and canonical plan.
- Verification:
  - Task IDs, date, and slug are consistent.
  - Current pointers reference `2026-06-06-git-workflow-skill`.
  - The canonical plan matches the approved scope and guardrails.
- Stop If:
  - Current artifact updates would contradict the approved task.

### Step 4
- Status: done
- Owner: unity-editor-developer
- Target Files:
  - `build/2026-06-06-git-workflow-skill.md`
  - `build/current.md`
- Action:
  - Draft the build report after implementation.
  - Record files changed, verification performed, and the config wiring decision.
- Verification:
  - The build report exists under `build/`.
  - The report is clearly a draft.
  - The report does not claim compile, log, or test work that was not run.
- Stop If:
  - The report claims verification that did not actually happen.

### Step 5
- Status: done
- Owner: unity-reviewer
- Target Files:
  - `.opencode/skills/git-workflow/SKILL.md`
  - `plans/current-plan.md`
  - `build/2026-06-06-git-workflow-skill.md`
- Action:
  - Review the skill, current-plan state, and draft build report for contradictions or missing guardrails.
- Verification:
  - No contradiction remains against the approved Git workflow requirements.
  - Current task pointers and build report align to the implemented task.
- Stop If:
  - Missing guardrails or workflow artifact drift require replanning.

## Acceptance Criteria
- `.opencode/skills/git-workflow/SKILL.md` exists and is generic and reusable across repos.
- The skill is guidance-oriented only and does not authorize automatic commits, pushes, merges, rebases, or combined PR-create-and-merge flows.
- The skill requires explicit user confirmation for push, PR creation, squash, merge, and rebase.
- The skill uses only the approved branch types, branch naming format, commit format, default commit-type behavior, and PR title format.
- The skill includes the approved Unity Git block and warning conditions.
- `.opencode/opencode.json` is unchanged unless minimal wiring was truly required.
- `brainstorm/current.md`, `plan/current.md`, `plans/current-plan.md`, and `build/current.md` all match `2026-06-06-git-workflow-skill`.
- A draft build report exists under `build/` for this task.

## Build Return Conditions
- `git-workflow` skill is missing, repo-specific, or missing approved guardrails
- `.opencode/opencode.json` is changed unnecessarily or invalidly
- current workflow artifacts still point at a different task
- build report omits the unchanged-config decision or overstates verification

## Completion Rule
This task is complete only when the reusable `git-workflow` skill exists, current workflow artifacts point at this task, config wiring stays minimal, and the draft build report is present.
