# Brainstorm Report

- Task ID: 2026-06-06-git-workflow-skill
- Status: approved-draft
- Phase: brainstorm
- Date: 2026-06-06
- Slug: git-workflow-skill

## Request Summary
Implement the approved repo-local git-workflow skill task in this repo.

## Goal
Add a generic repo-local Git workflow skill, promote the task into the repo workflow artifacts, keep current pointers aligned to this task, and draft the build report for the implementation.

## Constraints
- Keep the skill generic and reusable across repos.
- Guidance only; do not authorize automatic commits, pushes, merges, rebases, or other automatic Git-changing flows.
- Require explicit user confirmation for push, PR creation, squash, merge, and rebase.
- Never combine PR creation and merge into one automatic flow.
- Allowed branch types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `hotfix`.
- Branch naming must use `type/<short-slug>`.
- Commit messages must use `type(scope): short summary`.
- Commit type should match branch type by default.
- Commit messages and PR titles must be generated from the actual change.
- PR titles must use `[type] short summary`.
- Unity Git safety must block on missing `.meta` files and incomplete asset moves or renames.
- Unity Git safety must warn on binaries without LFS and heavy scene or prefab churn.
- Do not change app code.
- Do not change agent prompts.
- Keep changes within `.opencode/` skill or config files and workflow artifact or build report files.
- Prefer no `.opencode/opencode.json` change if skill auto-discovery already works.

## Questions Asked
- None during implementation handoff; the request arrived with approved requirements and guardrails.

## Options Considered
### Option A
Add a repo-specific Git workflow document without promoting workflow artifacts.

Pros:
- smaller change set

Cons:
- weaker reuse
- current workflow pointers would remain stale

### Option B
Add a generic reusable skill and update workflow artifacts to make this the current task.

Pros:
- reusable across repos
- keeps workflow history and current pointers aligned
- preserves existing config if auto-discovery already works

Cons:
- requires several documentation updates

## Recommendation
Use the generic reusable skill plus workflow artifact promotion approach, and leave `.opencode/opencode.json` unchanged unless minimal wiring is proven necessary.

## User Decisions
- Promote the git-workflow task into workflow artifacts so current files reflect it.
- Create `.opencode/skills/git-workflow/SKILL.md` as a generic reusable skill.
- Update `.opencode/opencode.json` only if minimal wiring is required.
- Create draft workflow artifacts for task ID `2026-06-06-git-workflow-skill` under `brainstorm/`, `plan/`, and `plans/`.
- Update current pointers so the current task is `2026-06-06-git-workflow-skill`.
- Create a draft build report for this task under `build/`, and update `build/current.md`.
- Keep the skill guidance-oriented only.
- Require explicit confirmation for push, PR creation, squash, merge, and rebase.
- Never combine PR creation and merge into one automatic flow.
- Use only the approved branch types, branch format, commit format, and PR title format.
- Include the approved Unity-specific Git safety block and warning rules.
- Do not change app code or agent prompts.

## Unresolved Items
None.

## Handoff To Planner
Produce a canonical plan that:
- adds the generic repo-local `git-workflow` skill under `.opencode/skills/`
- inspects whether `.opencode/opencode.json` needs minimal wiring and leaves it unchanged if not
- creates the task workflow artifacts for `2026-06-06-git-workflow-skill`
- updates the current workflow pointers to this task
- creates a draft build report under `build/`

## Approval
- User approved: yes
- Approval note: Repo-local generic git-workflow skill task approved for implementation and workflow promotion.
