# Phase I -- Polish + EDITOR-UI-GUIDE.md + final validation pass

- Effort: S
- Parallel-safe: No (terminal -- depends on every other phase)
- Blocks: nothing (ship gate)

## Scope

Terminal gate. Light/dark theme audit, icon polish, EDITOR-UI-GUIDE.md, end-to-end smoke test from a blank project, validation badges + auto-fix lambda audit, CHANGELOG.md update.

## File ownership

- NEW: `Assets/GrassInteract/Editor/UI/EDITOR-UI-GUIDE.md` (kit doc)
- Modify: `CHANGELOG.md` at project root (or `Assets/GrassInteract/CHANGELOG.md`)
- Touch-ups only: any USS / UXML / Icon file across phases B-G as theme audit reveals issues
- Memory file: project-local `.claude/projects/.../memory/grassinteract-project-status.md` updated with completion status

## Pre-conditions

- Phases A through H all merged + compile-clean.
- Demo scene re-authored against a fresh TerrainScatterConfig (validates clean-break path).

## Step-by-step tasks

### I.1 -- Light + dark theme audit

1. Switch Unity Editor Preferences -> General -> Editor Theme to Personal (light).
2. Open every editor surface in sequence:
   - TerrainScatterConfig inspector (header + tile grid + empty state).
   - DensityScatterLayer inspector (each section in turn).
   - InstanceScatterLayer inspector (each section in turn, including record list + drop zone + detail panel).
   - Scene-view overlay (Mode toolbar) in Select/Place/Erase modes.
   - DensityPaintWindow.
   - Validation popovers.
   - QuickAddPopover.
3. Screenshot each panel.
4. Switch to Pro (dark), repeat screenshots.
5. Compare side-by-side -- any contrast violation (white on white, dark on dark), missing border on focus, clipped text -> fix in the owning USS file (do NOT edit per-phase files unless surgically necessary).
6. Document remaining cosmetic issues in CHANGELOG.md "Known visual issues" section.

### I.2 -- Validation badge + auto-fix audit

1. For each field that has a ValidationBadge, deliberately set an invalid state and confirm:
   - Badge turns the correct colour (red / yellow).
   - Hover popover shows the right error text.
   - Auto-fix button (if any) actually fixes the problem.
2. Compile a checklist in EDITOR-UI-GUIDE.md naming every validation rule.

### I.3 -- End-to-end smoke (clean break path)

1. Create a NEW empty TerrainScatterConfig from `Assets > Create > GrassInteract`.
2. Save it under `Assets/_Smoke/Smoke_Config.asset`.
3. Click `+ Density Layer` -- sub-assets created, layer renders white density texture, Default_Material applied.
4. Paint density in DensityPaintWindow -- save.
5. Click `+ Instance Layer` -- second tile appears.
6. Drag a prefab cube into the InstanceLayer's drop zone -- record added.
7. Open scene view, place a ScatterField bound to Smoke_Config.
8. Toggle scene overlay to Place mode -- click ground 5 times -- 5 records added.
9. Enter Play mode. Verify Density layer renders procedural grass. Verify Instance layer's records render the cube prefab. Verify the records' MeshColliders work (drop a runtime physics object).
10. Exit Play mode. Save scene. Reopen Unity. Verify everything persists.
11. Document the smoke in EDITOR-UI-GUIDE.md as "First-time user walkthrough".

### I.4 -- Memory Profiler 1-minute capture

1. Open the TerrainScatterConfig inspector with 5 layers (3 Density + 2 Instance with 100 records each).
2. Memory Profiler -> take baseline snapshot.
3. Scroll the LayerTileGrid up and down repeatedly for 60s.
4. Take a second snapshot. Diff -> verify GC.Alloc < 1 MB / minute. Document any leaks found in CHANGELOG.

### I.5 -- EDITOR-UI-GUIDE.md content

Sections:
- Overview (what changed in this rewrite + why).
- Quick start (first-time user walkthrough from I.3).
- Component catalog (every reusable from B with a one-line description + screenshot).
- Theming (how to add a new variant; the USS token system).
- Drag-prefab depth limit (1024 transforms; how to extend).
- DensityPaintWindow keyboard shortcuts (CTRL+Z, etc.).
- LOD section: Auto-generate dependency (UnityMeshSimplifier optional).
- Validation rules catalog from I.2.
- Known visual issues from I.1.
- Scene-view overlay reference (Mode + Tool + snap + persistence).
- TheOne.Pooling integration status from H.0.
- Migration note: legacy demo assets deleted; re-author against new config.

### I.6 -- CHANGELOG.md update

1. Add entry under "Unreleased":
   - BREAKING: ScatterKind enum removed.
   - BREAKING: grassMaterial/meshMaterial collapsed into single `material` field.
   - BREAKING: per-layer collider fields removed (now per-record on InstanceScatterLayer).
   - BREAKING: legacy Demo/TerrainScatterConfig + sub-assets deleted -- re-author required.
   - ADDED: pure UIToolkit editor surface for TerrainScatterConfig + both layer types.
   - ADDED: DensityPaintWindow.
   - ADDED: scene-view Overlay (Select/Place/Erase modes).
   - ADDED: InstanceColliderPool + InstanceFrustumCuller.
   - FIXED / IMPROVED items as discovered during the cook.

### I.7 -- Final assertion sweep (project-wide)

Run each command, assert ZERO results:

1. `grep -rln "Sirenix" Assets/GrassInteract/Runtime/`
2. `grep -rln "ScatterKind\|\.Kind\b" Assets/GrassInteract/`
3. `grep -rln "grassMaterial\|meshMaterial\|GrassMaterial\|MeshMaterial" Assets/GrassInteract/Runtime/`
4. `grep -rln "TODO\[Phase-" Assets/GrassInteract/` -- all phase-handoff TODOs from A.6 etc must now be resolved.
5. `refresh_unity` + `read_console` -- zero errors AND zero new warnings introduced by the rewrite.

### I.8 -- Update project memory

1. Append to `.claude/projects/.../memory/grassinteract-project-status.md`:
   - "Pure UIToolkit scatter editor shipped 2026-06-DD".
   - Schema migration to V2 noted.
   - Clean-break demo asset deletion noted.
2. Create `.claude/projects/.../memory/scatter-uitoolkit-editor.md` documenting the component catalog briefly + pointing to EDITOR-UI-GUIDE.md.

## Validation criteria

1. Every I.1 panel renders cleanly in both themes (no contrast issues, no clipping).
2. Every I.2 validation rule fires + every auto-fix works.
3. I.3 end-to-end smoke completes without errors.
4. I.4 GC.Alloc < 1 MB / minute on scroll.
5. EDITOR-UI-GUIDE.md exists + has every section in I.5.
6. CHANGELOG.md has the entry from I.6.
7. Every assertion in I.7 returns ZERO results.
8. Project memory updated per I.8.
9. Final commit + push.

## Risks (in-phase)

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Theme audit uncovers structural USS issues that need re-architecture | 2 | 4 | 8 | If any panel needs >1 hour of fixes, file a follow-up issue + scope-down I to the doc work + critical fixes; document remaining issues in CHANGELOG "Known visual issues". |
| Memory Profiler reveals a non-trivial leak | 2 | 4 | 8 | If allocations are >1 MB/min, find the culprit via Memory Profiler snapshot diff; common cause is unhooked Undo subscriber or stale element binding. Fix in the offending phase's code. |
| End-to-end smoke uncovers a behavior regression | 2 | 5 | 10 | Track to the offending phase, file fix in same I commit; do not declare ship until smoke passes clean. |
| EDITOR-UI-GUIDE.md drifts from actual behavior by ship time | 2 | 3 | 6 | Write each I.5 section by SCREENSHOTTING the current behavior, not from memory. |

## Effort: S

Estimate 2-4 hours. Mostly verification, screenshots, and doc-writing. Theme audit can balloon if structural USS issues found; budget extra +2 hours buffer.
