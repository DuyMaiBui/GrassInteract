---
phase: 5
name: migration-and-deprecation
effort: S
agent: t1k-unity-developer
blocked-by: P1, P2, P3, P4
blocks: none
---

# Phase 5 - Migration + targetInstances Deprecation

## Goal

One-shot Bake Procedural Layer -> Authored menu that runs current GrassScatter once, writes the result to AuthoredInstancesData sidecar, and flips HasAuthoredInstances=true. Apply to demo layer. Deprecate targetInstances via [Obsolete] + [FormerlySerializedAs] (Q1 default). Update ScatterLayer.Validate to accept authored layers without a density map.

## Scope

IN: ScatterBakeToAuthored editor menu (Tools > GrassInteract > Bake Procedural Layer to Authored); demo layer migration; ScatterLayer.Validate update; targetInstances [Obsolete]+[FormerlySerializedAs] (per Q1).

OUT: hard-deletion of targetInstances (cycle 2, separate plan).

## File Ownership

| File | Action |
|---|---|
| Editor/ScatterBakeToAuthored.cs | CREATE - MenuItem Tools/GrassInteract/Bake Procedural Layer to Authored; one-shot freeze of GrassScatter output into sidecar |
| Runtime/ScatterLayer.cs | EDIT - mark targetInstances [Obsolete]+[FormerlySerializedAs(targetInstances)]; update Validate() to accept HasAuthoredInstances=true layers regardless of density map |
| Editor/TerrainScatterConfigEditor.cs | EDIT (minor) - hide targetInstances field from inspector when HasAuthoredInstances=true; show one-line deprecation HelpBox when field still serialized |
| Demo scene layer asset (under Assets/) | MIGRATE - run the bake menu against demo scene layer; commit resulting sidecar |

## Step-by-Step Tasks

1. Confirm Q1 + Q4 defaults still hold (read plan.md Locked Assumptions). If user has corrected: adjust deprecation strategy / re-invoke semantics before coding.
2. Author ScatterBakeToAuthored: MenuItem(Tools/GrassInteract/Bake Procedural Layer to Authored) operates on the currently-selected ScatterLayer asset. Guard: layer != null, layer.HasAuthoredInstances == false (otherwise show EditorUtility.DisplayDialog with Already authored - overwrite? Cancel/Overwrite per Q4 one-shot freeze with confirm).
3. Bake execution: capture pre-bake state; call GrassScatter.Build(layer) once at current seed; iterate produced records; AuthoredInstancesData.CreateInstance + populate Records via batch SetRecords (from P3); AssetDatabase.AddObjectToAsset(sidecar, layer); layer.AuthoredInstances = sidecar; layer.HasAuthoredInstances = true; EditorUtility.SetDirty(layer); AssetDatabase.SaveAssets.
4. Post-bake parity check: invoke ChunkInstanceLayoutVerify against the freshly-baked authored layer; MUST PASS byte-identical to the pre-bake procedural baseline (proves bake is lossless, Q4 freeze).
5. Update ScatterLayer.Validate: if HasAuthoredInstances == true: accept null/empty density map (it is just a placement mask now, not required). Pre-existing validation rules (mesh non-null, etc.) still apply.
6. Deprecate targetInstances field: add [System.Obsolete(Use HasAuthoredInstances + AuthoredInstancesData. Will be removed in next cycle.)] above the field. Add [UnityEngine.Serialization.FormerlySerializedAs(targetInstances)] to preserve serialized data round-trip on existing demo assets. Keep field nominally readable so demo asset serialization does not lose data on load before migration.
7. Inspector cleanup: in TerrainScatterConfigEditor, hide targetInstances row when HasAuthoredInstances=true; when HasAuthoredInstances=false AND targetInstances still has its old serialized value, show a yellow HelpBox: This layer is procedural. Use Tools > GrassInteract > Bake Procedural Layer to Authored to convert.
8. Migrate demo layer: open demo scene; select the demo ScatterLayer asset; invoke Tools > GrassInteract > Bake Procedural Layer to Authored; verify resulting sidecar instance count matches pre-bake procedural count (record number in phase-5-report.md); screenshot demo scene before + after; compare visually for parity.
9. Re-invoke semantics (Q4): invoking the menu on an already-authored layer triggers EditorUtility.DisplayDialog(Bake to Authored, This layer is already authored. Overwrite with a fresh procedural bake? All authored edits will be lost., Overwrite, Cancel). Cancel = no-op. Overwrite = wipe sidecar Records + re-bake at current seed.
10. Documentation: add a one-paragraph note to AuthoredInstancesData.cs class XML doc explaining the migration path and the density-map role flip (mask vs count-multiplier).

## Verification Gate

1. refresh_unity + read_console: clean compile. [Obsolete] field MUST NOT produce new warnings on any consumer (use pragma warning disable 0618 in internal read-sites if any exist; expect none).
2. ScatterInstanceCullHarness: PASS.
3. ChunkInstanceLayoutVerify: PASS against freshly-baked demo layer.
4. Demo layer migration: pre-bake screenshot vs post-bake screenshot visually identical (record both as screenshots/phase-5-before.png and screenshots/phase-5-after.png).
5. Demo layer asset: HasAuthoredInstances=true after bake; AuthoredInstances sidecar populated with N records where N == pre-bake procedural instance count (record actual numbers in phase-5-report.md).
6. Re-invoke confirm dialog appears when menu run on already-authored layer.
7. targetInstances field: still loads on existing assets (no data loss); shows [Obsolete] tooltip / deprecation HelpBox when relevant.
8. Validate() accepts authored layer with null density map (unit-style probe: instantiate ScatterLayer with HasAuthoredInstances=true, Density=null, assert Validate() returns true).

## Exit Criteria

- Bake menu functional + confirm-on-overwrite.
- Demo layer migrated cleanly with visual parity.
- targetInstances deprecated but data-preserving.
- Validate() accepts authored layers.
- ChunkInstanceLayoutVerify + ScatterInstanceCullHarness both PASS.
- phase-5-report.md written with pre/post bake counts + SHAs + screenshot diff result.

## Rollback Plan

- Delete Editor/ScatterBakeToAuthored.cs + meta.
- Revert ScatterLayer.cs: drop [Obsolete] + [FormerlySerializedAs] from targetInstances; revert Validate() change.
- Revert TerrainScatterConfigEditor.cs inspector hide/HelpBox lines.
- Demo layer rollback: set demo layer asset HasAuthoredInstances=false; remove AuthoredInstances sub-asset via Assets > Remove Sub-Asset; the original procedural targetInstances value is preserved (that is why FormerlySerializedAs matters).
- Rollback risk: LOW-MEDIUM. The demo asset migration is the only externally-visible state change; rollback re-flips the flag and removes the sub-asset. Original procedural data is intact.

## Post-Phase: Plan-Wide Closeout

- Verify final acceptance against brainstorm Success Metrics table (plan.md section).
- Update Assets/GrassInteract/README.md (if it exists) with one-paragraph mention of authored mode.
- Update memory/grassinteract-project-status.md noting the milestone.
- File follow-up plan stub for cycle-2 hard-delete of targetInstances (out of scope here per Q1).

