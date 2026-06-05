# Phase 5: Review + final live verify

**Effort: S** | **Blocked by: all phases** | **Blocks: nothing (final gate)**

## Goal

Run a `unity-code-reviewer` pass over the full refactor and clear the 0 Critical/High gate, then do a
final live verification that every one of the five success criteria holds. No new feature code - only
fixes that the review surfaces.

## File ownership

- No new files. May make small fixes to any Phase 1-4 file IF the reviewer flags a Critical/High issue.
  Each such fix re-runs that file's phase verification gate.

## Concrete steps

1. **unity-code-reviewer pass** over the changed/new set: `GrassInteractInstanced.shader`,
   `GrassScatter.cs`, `GrassRenderer.cs`, `GrassBendSimulator.cs`, `GrassInteractor.cs`,
   `GrassInteractField.cs`, `GrassFieldSpace.cs`, `GrassLODConfig.cs`, `GrassInteractDemoBuilder.cs`,
   `GrassLayer.cs`, `README.md`. Focus areas: per-frame zero-GC (no allocation in `Step`/`Render`),
   no magic numbers (lean->degree constants + recoveryRate live in config, not inline), Unity C#
   conventions (camelCase private fields, `this.` prefix, `#nullable enable`), null-safety on the
   simulator/scatter result, and that NO `_Grass*` shader global or trample reference remains.
2. **Resolve every Critical/High finding.** Re-verify the affected phase gate after each fix. Medium/
   Low may be deferred only with an explicit one-line note in the review summary.
3. **Final live verification of all five success criteria** (see gate below).
4. **Update the project-status memory + skills if any manual correction recurred** (per
   manual-correction-implies-skill-gap): if a Unity DOTS/render gotcha was re-discovered, record it.

## In-editor verification gate (the five success criteria)

1. **Dumb shader / bug-class immunity:** grep the shader + all runtime for
   `_GrassTrample|_GrassWind|_GrassBend|_GrassFlatten|_GrassFieldRect|GrassInteractDeform|TrampleUpdate`
   -> ZERO hits. Shader compiles clean (`read_console` no errors, no magenta).
2. **Renders in Game AND Scene, edit + play:** open the rebuilt demo, confirm grass visible in both
   views in both modes.
3. **Interactor lean + C#-readable recovery:** the orbiting effector leans blades away + they recover;
   a temporary `simulator.GetBendState(i)` readout (removed after) confirms non-zero-under-effector ->
   decay-after, with NO GPU readback.
4. **Perf:** ~20k demo blades hold frame budget; README documents 50k soft ceiling + the wind escape
   hatch.
5. **Review gate:** `unity-code-reviewer` reports 0 Critical / 0 High.

## Rollback

If the reviewer surfaces a Critical issue that cannot be quickly fixed, restore the affected file from
its phase `_backup/` copy and re-plan that phase. The refactor is not "done" until all five criteria +
the 0 Critical/High gate pass.

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Reviewer finds a per-frame allocation (GC churn) | 2 | 3 | 6 | renderSlabs + bendState + phase are allocated once at construction; Step writes in place. If a hidden alloc is found (e.g. a LINQ or new Vector in the loop), hoist it out. |
| Reviewer finds a magic number (lean->degree constant) | 3 | 1 | 3 | Move any tuning constant into GrassLODConfig as a serialized field before claiming done. |
| A residual trample/global reference slips through | 2 | 3 | 6 | The criterion-1 grep is the hard gate; it must return zero hits across shader + runtime + editor. |
| "Works on my machine" non-reproducibility | 2 | 2 | 4 | Every gate is an objective command (grep zero-hits, read_console zero-errors, bendState log decays, renders-in-both-views) - not a subjective "looks fine". |
