# Phase 8 - Docs Sync: Library Skill + Reference Notes

Route: both | Effort: S | Parallel-safe with Phases 6-7 | Part of definition-of-done (library-quality mandate)

## Objective

Update the owning library skill docs in the SAME change as the fix (library-quality mandate: skill update is
not a follow-up). Capture the corrected deform model, the format gotcha, and the verification traps so the next
implementer inherits the knowledge instead of rediscovering it.

## Finding: NO grass / interact-deform skill exists

A search of .claude/skills/**/SKILL.md found NO skill owning grass interaction / trample deform (the nearest
Unity skills are rendering/URP/shader-graph/game-patterns, none cover this library). This is a skill GAP.

## Concrete steps

1. Decide doc home (batched into the AskUserQuestion if it surfaces; otherwise default below). DEFAULT: since no
   dedicated skill exists and this is a project library, write the durable notes into a project-local reference
   doc next to the library - e.g. Assets/GrassInteract/README.md or a docs/ note in the project - AND record the
   skill gap so a t1k-unity skill can later absorb it via sync-back. Do NOT silently invent a new kit skill from
   a consumer project (kit-wide-fix-discipline.md): if the knowledge belongs in a t1k-unity skill, file it via
   /t1k:sync-back (background) targeting the owning kit, do not hand-edit a kit skill locally.
2. Document, in the chosen doc:
   - The corrected deform model (Route B vector bake OR Route A scalar straight-down + clamped lean, whichever
     shipped) and WHY the old gradient-lean-away model was wrong (DEFECT 1 zero-core, DEFECT 2 overshoot).
   - The RT format gotcha extended: R8 samples zero in-shader (existing note) AND the Phase-1 verified
     multi-channel format result (which format passed/failed the in-shader read test).
   - The verification traps: top-down ORTHOGRAPHIC only (perspective injects radial parallax); the effector has
     no [ExecuteAlways] so moving cases need play mode or manual transform moves; reconfirm isPlaying before
     moving-play captures.
3. Update in-code comments touched by the fix so they describe the NEW model (the GrassInteractDeform.hlsl
   header comment still describes gradient lean-away; the TrampleUpdate.shader header still says single-channel
   RHalf). Keep/extend SSOT fence comments.
4. If the fix touched any t1k-unity skill content, spawn a BACKGROUND /t1k:sync-back per orchestration-rules.md
   (background-only) AFTER local commit. Report the PR URL one-line and STOP (kit-pr-workflow-boundary.md).

## Success criteria

- The corrected model, the format gotcha, and the verification traps are documented in a project-local doc.
- In-code header comments (GrassInteractDeform.hlsl, TrampleUpdate.shader, GrassTrampleMap.CreateRT) describe
  the SHIPPED model + format, not the old one.
- The skill gap is recorded; if kit-skill content is implicated, a background sync-back PR is opened (URL
  reported, not babysat).

## Verify

- Re-read the updated docs/comments: they match the shipped Route (B or A) and the shipped RT format.
- grep confirms no comment still claims gradient lean-away / single-channel RHalf if Route B shipped.

## Unity safety

Doc/comment-only phase. NEVER kill/quit the Editor; NEVER Reimport All.
