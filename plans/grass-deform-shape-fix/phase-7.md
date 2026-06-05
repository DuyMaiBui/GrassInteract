# Phase 7 - Re-Tune Defaults for the Corrected Model

Route: both | Effort: S | Blocked by: Phase 6 (need the corrected model rendering before tuning)

## Objective

Re-tune the bend/flatten defaults for the CORRECTED deform model. The old values were chosen to compensate for
the buggy gradient model (e.g. a large bendStrength to force any visible lean despite the zero-core), so they are
wrong for the fixed model. Tune against Phase-6 objective criteria, not nostalgia.

## File ownership

- Assets/GrassInteract/Demo/GrassInteractDemoConfig.asset - bendStrength + flatten YAML values.
- Assets/GrassInteract/Runtime/GrassLODConfig.cs - the DEFAULT field initializers for bendStrength (1.5) and
  flatten (0.08), and any NEW tunable added in Phase 2 (e.g. trampleHeadingBlend). Keep defaults sensible for a
  brand-new field with no demo overrides.

## IMPORTANT discrepancy to resolve

The live demo asset currently has bendStrength = 3.5 and flatten = 0.08 (NOT the 1.5 the brief/code-default
states). GrassLODConfig.cs defaults bendStrength = 1.5. So the demo asset already overrides the code default to
an inflated 3.5 - almost certainly to fight the buggy model. Phase 7 OWNS reconciling both:
- Set GrassLODConfig.cs default bendStrength to a value sensible for the corrected BOUNDED model.
- Set the demo asset bendStrength/flatten to the tuned values that pass Phase-6 criteria at the demo footprint.
- Because the corrected lean is footprint-bounded, the inflated 3.5 will likely need to come DOWN substantially;
  confirm empirically, do not guess.

## Concrete steps

1. Start from the corrected model (Phase 4 landed, Phase 6 green at the OLD values - which may now look weak or
   wrong). Sweep bendStrength + flatten across a few values at worldRadius 0.5 and 2.5.
2. Choose values where: the core clearly mats DOWN (flatten reads as pressed), the lean is a gentle contained
   bend within the footprint, and a moving interactor leaves a readable trail. Capture top-down orthographic
   before/after.
3. Update BOTH the code default (GrassLODConfig.cs) and the demo asset (GrassInteractDemoConfig.asset).
4. If Phase 2 added trampleHeadingBlend, set its default (0 = pure radial unless the directional mat-down is
   wanted in the demo) and document it.
5. Feed the chosen bendStrength back to Phase 5 so the headroom inequality is checked against the FINAL value.

## Success criteria

- Demo asset + code default bend/flatten produce a contained, mats-down, footprint-bounded deform at both
  radii in Phase-6-style captures.
- No magic numbers introduced; every value is a config field.
- bendStrength reconciled between code default and demo asset (no unexplained 3.5 override left behind).

## Verify

- Re-run the Phase-6 captures (at least static 0.5/2.5 + one moving) at the new values; all four criteria still
  pass and the look is improved (mats-down reads clearly).
- refresh_unity + read_console clean.

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All.
