---
plan: scatter-layer-placement-split
created: 2026-06-04 22:20
owner: t1k-unity-developer (single agent, sequential phases - NO --team, NO --parallel)
brainstorm: plans/reports/brainstorm-scatter-layer-placement-split-20260604.md
follows: plans/authored-instance-scatter-editor/ (P1-P5 shipped)
baseline screenshot: plans/authored-instance-scatter-editor/screenshots/phase-5-before.png
unity instance: GrassInteract@de203215 (port 6403)
status: ready-to-cook
---

# Plan: ScatterLayer Placement-Axis Polymorphism Refactor

Split `ScatterLayer` placement axis into two concrete subclasses (`DensityScatterLayer` + `InstanceScatterLayer`) with an `IScatterPlacement` strategy interface. Eliminates `HasAuthoredInstances` flag + `[Obsolete] targetInstances` + `[FormerlySerializedAs]` + `[HideIf]` + `[InfoBox]` + `#pragma 0618` stack. Rendering axis (`ScatterKind { Grass, Mesh }`) untouched.

## Locked Decisions (from brainstorm - do NOT re-litigate)

| # | Decision | Source |
|---|---|---|
| D1 | Polymorphism scope: placement axis only; kind stays as enum | User confirmed |
| D2 | Type names: DensityScatterLayer + InstanceScatterLayer | User confirmed |
| D3 | Migration: one-shot menu (NOT MovedFrom attribute) | User confirmed |
| D4 | Engine API: IScatterPlacement strategy interface | User confirmed |


## Open Questions (defaults locked; correct before affected phase begins)

| # | Question | Default | Affected phase | Reversal cost |
|---|---|---|---|---|
| Q1 | IScatterPlacement per-call new vs cached on layer | Per-call new; stateless except layer ref; one alloc per Rebuild (not per frame). Brainstorm risk score = 6 (negligible). | R1 | Trivial - add NonSerialized private IScatterPlacement cached accessor on base; one-line change. |
| Q2 | Migration menu UX: per-asset confirm vs silent batch + summary | Silent batch with end-of-run summary (Debug.Log per asset + final tally). Per-asset .json backups make confirm prompts redundant. | R3 | Trivial - wrap each iteration in EditorUtility.DisplayDialog. |
| Q3 | Demo asset migration: automatic during R3 verification vs explicit user gate | Automatic during R3 - backup written before swap; R3 exit criterion is demo loads + renders identical to baseline so automatic migration IS the verification. | R3 | None - running the menu manually post-R3 is equivalent. |
| Q4 | After R5: hard-delete targetInstances from base immediately vs keep one-cycle Obsolete shim on DensityScatterLayer | Hard-delete immediately. P1-P5 deprecation cycle already ran; demo asset migrates as part of R3; no external consumers carry the field. | R5 | Cheap - re-add [Obsolete, SerializeField] private int targetInstances on DensityScatterLayer only. |

If any of Q1-Q4 is wrong for the cook lead, correct before the affected phase teammate spawns.

## Phases (sequential - critical-path order is load-bearing)

| # | Phase | Effort | Owns |
|---|---|---|---|
| R1 | Add IScatterPlacement + DensityPlacement + InstancePlacement (mirror existing bodies; not wired) | S | NEW: Runtime/IScatterPlacement.cs, Runtime/DensityPlacement.cs, Runtime/InstancePlacement.cs |
| R2 | Add DensityScatterLayer + InstanceScatterLayer concrete subclasses (alongside still-concrete ScatterLayer) | S | NEW: Runtime/DensityScatterLayer.cs, Runtime/InstanceScatterLayer.cs |
| R3 | Migration menu; run on demo asset; verify load + render parity | S | NEW: Editor/MigrateScatterLayerTypes.cs |
| R4 | Type-tighten consumers; HasAuthoredInstances reads become is-InstanceScatterLayer; narrow editor APIs | M | ~8 edits across runtime + editor |
| R5 | Rewire GrassScatter.Build to facade; promote ScatterLayer to abstract; delete targetInstances + HasAuthoredInstances + Obsolete stack | S | Runtime/GrassScatter.cs, Runtime/ScatterLayer.cs |
| R6 | Final verification: compile + harness + screenshot diff vs baseline | S | - |

**Critical path:** R1 -> R2 -> R3 -> R4 -> R5 -> R6. **R3 cannot start until R1+R2 ship** (needs both target types to exist). **R5 cannot start until R3 verifies** (else ScatterLayer.asset MonoScript GUID points at soon-abstract type -> demo unloadable; risk score 20).

## File Ownership Matrix

| File | R1 | R2 | R3 | R4 | R5 | R6 |
|---|---|---|---|---|---|---|
| Runtime/IScatterPlacement.cs (NEW) | CREATE | - | - | - | - | - |
| Runtime/DensityPlacement.cs (NEW) | CREATE | - | - | - | - | - |
| Runtime/InstancePlacement.cs (NEW) | CREATE | - | - | - | - | - |
| Runtime/DensityScatterLayer.cs (NEW) | - | CREATE | - | extend (fields populated) | - | - |
| Runtime/InstanceScatterLayer.cs (NEW) | - | CREATE | - | extend (fields populated) | - | - |
| Editor/MigrateScatterLayerTypes.cs (NEW) | - | - | CREATE | - | - | - |
| Runtime/ScatterLayer.cs | - | - | - | - | edit (abstract, delete deprecated) | - |
| Runtime/GrassScatter.cs | - | - | - | - | edit (shrink to facade) | - |
| Runtime/ScatterField.cs | - | - | - | edit (is-InstanceScatterLayer) | - | - |
| Editor/TerrainScatterConfigEditor.cs | - | - | - | edit (type-aware UI) | - | - |
| Editor/ScatterBrush.cs | - | - | - | edit (narrow to InstanceScatterLayer) | - | - |
| Editor/InstancePickingService.cs | - | - | - | edit (narrow API) | - | - |
| Editor/InstanceSelectionOverlay.cs | - | - | - | edit (narrow API) | - | - |
| Editor/ScatterBakeToAuthored.cs | - | - | - | edit (creates InstanceScatterLayer sub-asset) | - | - |
| Editor/ScatterFieldRebuildLayerHarness.cs (existing) | gate | gate | gate | gate | gate | gate |

No two phases mutate the same file in overlapping turns. ScatterLayer.cs stays concrete until R5.

## Dependencies

- **Blocks:** future placement modes (Poisson, Stream) - design supports, not shipped.
- **Blocked by:** nothing. P1-P5 already shipped. Brainstorm design-approved.
- **External constraints:** Live Unity editor; never kill / Assets/Reimport All per unity-forbidden-operations.md. Multi-instance host - pin to GrassInteract@de203215 first MCP call every phase.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| **Demo ScatterLayer.asset MonoScript GUID refers to soon-abstract type** | 4 | 5 | **20** | Phasing is load-bearing: ScatterLayer stays concrete through R4. Migration menu (R3) MUST run + verify on demo BEFORE R5 promotes ScatterLayer to abstract. R5 exit criterion includes asset-presence check confirming demo carries DensityScatterLayer sub-asset (not legacy ScatterLayer). |
| Migration menu loses field data | 3 | 5 | **15** | EditorJsonUtility.ToJson -> FromJsonOverwrite exact-name round-trip; per-asset .json backup to plans/scatter-layer-placement-split/backups/ASSET-GUID.json before any destructive op; dry-run flag in menu code. |
| **Subagent stall during long edit phases** (deterministic in this repo; 5 stalls in P1-P5) | 5 | 3 | **15** | Each phase spawn-brief includes anti-stall guards: code-edits-only in subagent; gate verification in main loop; 150K commit checkpoint; no narration; first MCP call always set_active_instance GrassInteract@de203215. |
| TerrainScatterConfig.layers ref swap leaves orphan sub-asset | 3 | 3 | 9 | AssetDatabase.RemoveObjectFromAsset after entry swap, before SaveAssets. |
| ScatterFieldRebuildLayerHarness regresses | 3 | 3 | 9 | Verification gate every phase runs harness. |
| 101 layer.X reads need type-tightening | 4 | 2 | 8 | ~95% are shared base fields -> no change; only HasAuthoredInstances reads (~5 sites) need is-pattern. R4 owns this. |
| IScatterPlacement allocates per Rebuild | 3 | 2 | 6 | One alloc per layer per Rebuild (not per frame). Negligible. Cache on layer if profiling demands (Q1 default). |
| Legacy GrassLayer ScatterLayer alias still in use | 2 | 3 | 6 | Pre-R1 grep; brainstorm memory says retired; verify. |
| CreateAssetMenu clash with existing | 1 | 2 | 2 | Standard Unity behavior; new menu paths chosen distinct from existing. |

**Highest active risk = Demo asset MonoScript GUID (score 20)** - fully mitigated by load-bearing phase order. **Secondary highest = subagent stalls (score 15)** - mitigated by per-phase spawn-brief discipline.
