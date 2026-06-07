---
name: unity-conventions
description: Unity conventions, module structure, and stack guidance for this repo. Use when implementing, testing, reviewing, or updating Unity-focused prompts and workflows here.
---

# Unity Conventions

This skill is the repo-local single source of truth for generic Unity conventions.
It is intentionally generic and must not be derived from current repo code.

## Use This Skill When
- implementing Unity runtime or editor work
- testing or reviewing Unity changes
- updating Unity-focused repo-local opencode prompts or workflows

## Operating Rules
- Treat this skill as guidance within the approved task scope only.
- Do not use this skill to justify unrelated refactors, package churn, or architecture rewrites.
- If following these conventions would require broader rework than the approved task allows, stop and return to planning.
- If repo reality materially conflicts with these conventions, surface the conflict instead of silently working around it.

## Code Conventions
- Use `#nullable enable`.
- Write XML docs only for interface, public, or protected API.
- Qualify instance member access with `this.`.
- Default to `internal` visibility.
- Default to `sealed` types unless extension is required.
- Use `static` only for stateless helpers.
- Keep one top-level type per file.
- Do not place `UnityEditor` references in runtime code.
- Use `#if UNITY_EDITOR` only for small runtime-adjacent hooks, not as a substitute for proper assembly separation.
- Avoid per-frame allocations in hot paths.
- Avoid alloc-heavy LINQ or ZLinq usage in hot paths.

## Structure Conventions
- Organize work as full feature modules.
- Use per-feature asmdefs by default.
- Use namespaces in the shape of `Project`, `Project.Feature`, `Project.Editor`, and `Project.Editor.Feature`.
- Runtime feature folders should normally use:
  - `Api/`
  - `Data/`
  - `Services/`
  - `Systems/`
  - `Presentation/`
- Keep `MonoBehaviour` classes thin.
- Use `ScriptableObject` for configuration and authoring-time behavior.
- `Services` contain business or application logic.
- `Systems` contain ticking or orchestration logic.
- `Api` contains contracts, facades, and events only.

## Stack Guidance
- Unity 6
- URP
- Odin
- VContainer
- MessagePipe
- UniTask
- ZLinq
- LitMotion

## Usage Rules
- Prefer broad VContainer usage for composition.
- MessagePipe is acceptable inside a feature.
- Use UniTask in runtime code by default.
- Odin can be used broadly, but core runtime architecture should not depend on it.
- ZLinq is acceptable generally, but never in hot paths.
- Use LitMotion for UI, presentation, and gameplay animation.

## Block And Return Conditions
Stop and return when the approved task would require any of the following:
- a runtime/editor boundary violation
- a structure conflict that needs broader rework
- repo reality materially conflicting with the approved conventions

If the issue is a same-scope implementation defect, return to the builder.
If the issue needs scope, structure, or architecture changes, return to the planner.
