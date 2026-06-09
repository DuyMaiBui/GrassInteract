# Plan: GrassInteract Production Authoring Toolchain

Date: 2026-06-09 18:08 · Status: ready for `/t1k:cook`
Source of truth: `plans/reports/production-authoring-toolchain-brainstorm.md` (design approved, self-contained).

Rebuild the authoring toolchain cleanly on the refactored runtime (composed-struct layers, V2 records,
`InstancedPropEngine`, `ScatterField` with `Rebuild`/`RebuildLayer`). Commit `af6c69e` deleted the entire
old `Editor/` suite — this plan does NOT restore those files; it re-architects on Unity `EditorTool` +
`Overlays` + `Handles`, plain IMGUI/UI-Toolkit panels, Unity-stdlib only.

## Phases

| Phase | Sub-project | Name | Scope | Effort |
|-------|-------------|------|-------|--------|
| 1 | A | Data model — PhysicMaterial per instance | Blob V2→V3, COLLIDER_BYTES 16→20, layer default + pool material; EditMode unit tests | M |
| 2 | B | Preview driver — edit-mode render + debounced rebuild | `ScatterField` edit-mode tick, `ScatterRebuildScheduler`, inspectors route through scheduler | M |
| 3 | C | Density paint — terrain-like `EditorTool` | `DensityPaintTool`, brush overlay, raycast→write R8 map, Paint/Erase/Smooth, brush disc gizmo | L |
| 4 | D | Placement tool — transform-like `EditorTool` | `InstancePlacementTool`, Place/Select+Transform/Erase, per-instance inspector with PhysicMaterial | L |
| 5 | E | Shared gizmo layer | `ScatterGizmos` static `Handles` helpers + `ScatterField` field-bounds gizmo | S |

## Feasibility

- **Reuse check:**
  - Phase 1 — EXTENDS existing `AuthoredInstancesData` blob codec, `InstanceScatterLayer`, `InstanceColliderPool` (no new file except the test asmdef). The V1→V2 migration chain + working-list API already exist; V3 follows the exact same pattern.
  - Phase 2 — EXTENDS `ScatterField` (edit-mode tick as `#if UNITY_EDITOR` partial / editor companion). `RebuildLayer(idx)` and `StepAll`/`SubmitAll` already exist (currently private; expose to the tick). `ScatterRebuildScheduler` is NEW (editor-only).
  - Phases 3/4/5 — NEW editor-only files under a fresh `GrassInteract.Editor` asmdef. Brush data (`BrushStamp`, `TerrainScatterConfig.brushStamps`), `DensityScatterLayer.densityMap`/`Validate`, and `AuthoredInstancesData` working-list edit API already exist and are reused as-is.
- **Complexity:** moderate overall. Phase 1 is mechanical (codec extension + tests). Phase 2 is the riskiest foundation (edit-mode loop). Phases 3/4 are the largest by surface area but lean on existing runtime APIs.

## Dependency graph

```
Phase 1 (A) ─┐
             ├─► Phase 4 (D)  (D needs A's PhysicMaterial payload + B's scheduler/preview)
Phase 2 (B) ─┤
             ├─► Phase 3 (C)  (C needs B's live re-scatter feedback)
             │
             └─► Phase 5 (E)  (E is a small shared dep; skeleton lands with whichever of C/D goes first)
```

- **Phase 1 (A)** — blocked by: nothing. Blocks: Phase 4 (PhysicMaterial payload).
- **Phase 2 (B)** — blocked by: nothing (parallel-safe with Phase 1; different files except both may touch `ScatterField` — sequence those edits). Blocks: Phases 3, 4 (live re-scatter), Phase 5 (field-bounds gizmo host).
- **Phase 3 (C)** — blocked by: Phase 2. Parallel-safe with Phase 4 (disjoint files) and Phase 5.
- **Phase 4 (D)** — blocked by: Phase 1 + Phase 2. Parallel-safe with Phase 3 and Phase 5.
- **Phase 5 (E)** — blocked by: nothing hard; its skeleton (`ScatterGizmos` static stub) should land alongside whichever of C/D is implemented first so both consume one SSOT. The `ScatterField` field-bounds gizmo line depends on Phase 2's editor companion existing.

**Parallel-safe after foundation:** Phases 3, 4, 5 share no files (each owns distinct editor files; Phase 5's `ScatterGizmos.cs` is the only shared consumer — land it first or as part of C). If fanned out to parallel teammates, allocate Phase 5's `ScatterGizmos.cs` to ONE owner and have C/D consume it; do not let two teammates both create it.

## File-ownership conflict map

| File | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Phase 5 |
|------|---------|---------|---------|---------|---------|
| `Runtime/AuthoredInstancesData.cs` | **modify** | — | — | — | — |
| `Runtime/InstanceScatterLayer.cs` | **modify** | — | — | — | — |
| `Runtime/InstanceColliderPool.cs` | **modify** | — | — | — | — |
| `Runtime/ScatterField.cs` | — | **modify** (expose Step/Submit/RebuildLayer to tick) | — | — | — |
| `Editor/GrassInteract.Editor.asmdef` | — | **create** | (consume) | (consume) | (consume) |
| `Editor/ScatterFieldEditorTick.cs` | — | **create** | — | — | — |
| `Editor/ScatterRebuildScheduler.cs` | — | **create** | (consume) | (consume) | — |
| `Editor/*Editor.cs` inspectors | — | **create** (route through scheduler) | — | (per-instance section) | — |
| `Editor/DensityPaintTool.cs` | — | — | **create** | — | — |
| `Editor/InstancePlacementTool.cs` | — | — | — | **create** | — |
| `Editor/ScatterGizmos.cs` | — | — | (consume) | (consume) | **create** |
| `Tests/Editor/GrassInteract.EditorTests.asmdef` + tests | **create** | — | — | — | — |

Only `ScatterField.cs` is touched by Phase 2 and structurally read by Phase 5's gizmo line — sequence Phase 5's `ScatterField` field-bounds gizmo after Phase 2's editor companion lands. No other file is owned by two phases.

## Cross-phase Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|:---:|:---:|:---:|------------|
| Edit-mode clock + `SceneView.RepaintAll()` spins the editor (busy-loop, fan drag) | 4 | 4 | 16 | **HIGH** — gate the tick behind BOTH `previewEnabled` toggle AND "a Scatter `EditorTool` is active OR a ScatterField is selected". Default preview OFF. `previewColliders` OFF by default (no 50k GO spawn). Repaint only when something marked dirty this frame. (Phase 2) |
| Blob V3 migration breaks the V1→V2→V3 chain (data loss on existing assets) | 3 | 5 | 15 | **HIGH** — EditMode round-trip tests: V3 pack/unpack, V2→V3 sets matRefIdx=-1, V1→V2→V3 end-to-end, COLLIDER_BYTES boundary. Version byte gate (`blob[0]`) routes to the correct unpacker; V2 path preserved verbatim. (Phase 1) |
| GPU indirect engine renders differently in edit mode vs play loop | 2 | 3 | 6 | CPU-tier fallback already triggers on GPU self-test failure (`ScatterField.TryBuildGpuEngine`). Edit-mode tick drives the same `SubmitAll`; if GPU path misbehaves in edit mode, `forceTier=ForceCpu` is the documented authoring fallback. (Phase 2) |
| 50k live re-scatter on every keystroke stalls the editor | 4 | 3 | 12 | `ScatterRebuildScheduler` ~150 ms idle debounce + per-layer `RebuildLayer(idx)` (never full `Rebuild()`). Async/incremental scatter explicitly deferred. (Phase 2, consumed by 3/4) |
| Two parallel teammates both create `ScatterGizmos.cs` (SSOT split) | 2 | 2 | 4 | Allocate `ScatterGizmos.cs` to one owner; land its skeleton with the first of C/D. Documented in dependency graph. |
| Editor asmdef edit doesn't trigger recompile (asmdef-only no-op) | 2 | 2 | 4 | After creating `GrassInteract.Editor.asmdef`, touch a `.cs` in it or `refresh_unity(force, all)` (per `ai-velocity-batch-compile-unity.md`). |

Risk score ≥ 15 = high; both high risks (edit-mode spin, blob migration) have mandated mitigations that MUST be in place before their phase is marked done.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| 1 (A) Data model | M | No blocker. Mechanical codec + tests. |
| 2 (B) Preview driver | M | No blocker. Highest-risk foundation (edit-mode loop). |
| 3 (C) Density paint | L | Blocked by 2. Largest UI surface. |
| 4 (D) Placement tool | L | Blocked by 1 + 2. Largest UI surface. |
| 5 (E) Shared gizmos | S | Skeleton lands with first of C/D; field-bounds line after 2. |
| **Total** | **~2 M + 2 L + 1 S** | **Critical path: 1 → 2 → 4** (D depends on both foundations). C and E run in parallel off 2. |

Foundation (1 + 2) is sequential-ish and unblocks everything. After foundation, 3/4/5 fan out in parallel (disjoint files; allocate `ScatterGizmos.cs` to one owner).

## Constraints (apply to every phase)

- Unity `EditorTool` API + `Overlays` + `Handles`; plain IMGUI/UI-Toolkit for tool panels. **NO Odin (Sirenix) in editor tools** — Odin stays on runtime layer inspectors only.
- Unity-stdlib only — zero third-party. PhysicMaterial, EditorTool, Handles, Overlays, SceneView are all stdlib → passes `library-third-party-decoupling`.
- Genre-neutral naming: `ScatterGizmos`, `InstancePlacementTool`, `DensityPaintTool`, `ScatterRebuildScheduler`.
- Editor code lives in a fresh `Assets/GrassInteract/Editor/` asmdef (`GrassInteract.Editor`) referencing the runtime asmdef. Runtime files keep **zero** `UnityEditor` usage (edit-mode tick is an editor companion or `#if UNITY_EDITOR` partial).
- The1Studio C# conventions (`this.` prefix, `camelCase` private fields no underscore, `PascalCase` public, `#nullable enable`).

## Test gate

- **Phase 1 (runtime additions):** EditMode unit tests required — V3 pack/unpack round-trip, V2→V3 migration, V1→V2→V3 chain, pool material assignment. Zero failures before phase done.
- **Phases 2–5 (editor-UI tools):** manually validated in-editor. **No automated editor-UI test requirement** — stated explicitly. Each phase lists reproducible manual validation steps as its success criteria.

## Cook handoff

`/t1k:cook plans/production-authoring-toolchain/plan.md`
