---
name: git-workflow
description: Git workflow guidance for branches, commits, PR handoff, and Unity Git safety. Use when the user asks to inspect changes, prepare Git actions, draft commit or PR text, or review safe version-control steps. Guidance only.
---

# Git Workflow

This skill is repo-agnostic and reusable across repositories.
It provides guidance for safe Git workflows and must not be treated as authorization for automatic Git-changing actions.

## Use This Skill When
- inspecting status or diff before Git actions
- proposing a branch name, commit message, or PR title
- reviewing Git safety risks before commit or PR work
- handling Unity-specific asset version-control concerns

## Core Guardrails
- Guidance only. Do not automatically commit, push, create a PR, squash, merge, or rebase because this skill was loaded.
- Require explicit user confirmation before any push, PR creation, squash, merge, or rebase.
- Never combine PR creation and merge into one automatic flow.
- Generate branch, commit, and PR suggestions from the actual inspected change.
- Do not stage, commit, or describe unrelated files as part of the same change.

## Branch Rules
- Allowed branch types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `hotfix`.
- Branch naming format: `type/<short-slug>`.
- Match the branch type to the actual change.
- If none of the allowed branch types fit, ask the user instead of inventing a new type.

## Commit Rules
- Commit format: `type(scope): short summary`.
- Commit type should match the branch type by default.
- Generate the commit message from the actual change, not from a guessed plan.
- Keep the summary short, factual, and scoped to what is really included.
- Keep the scope meaningful and narrow.

## PR Rules
- PR title format: `[type] short summary`.
- Generate the PR title from the actual change.
- Treat PR creation as a separate explicit step that requires user confirmation.
- Treat squash, merge, and rebase as separate explicit decisions that each require user confirmation.
- Do not assume merge strategy, squash behavior, or base branch changes without confirmation.

## Suggested Safe Flow
1. Inspect Git status and diff.
2. Identify the actual change scope.
3. Check for Git safety blockers and warnings.
4. Propose the branch name, commit message, and PR title from the inspected change.
5. Ask for explicit confirmation before any push, PR creation, squash, merge, or rebase.
6. Keep PR creation and merge as separate user-approved steps.

## Unity-Specific Git Safety

### Block
- Missing `.meta` files for added, moved, or renamed Unity assets.
- Incomplete asset move or rename where the asset path and `.meta` path do not move together.

### Warn
- New or changed binary assets that are not tracked with LFS when the repo expects LFS for that asset type.
- Heavy scene or prefab churn that may hide unintended serialized changes.

## Review Checklist
- branch type is one of the allowed types
- branch name uses `type/<short-slug>`
- commit message uses `type(scope): short summary`
- commit type matches branch type by default
- commit message is generated from the actual change
- PR title uses `[type] short summary`
- explicit confirmation exists before push, PR creation, squash, merge, or rebase
- missing `.meta` files block progress
- incomplete Unity asset moves or renames block progress
- binary/LFS and scene/prefab churn warnings are surfaced when relevant
