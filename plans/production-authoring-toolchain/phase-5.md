# Phase 5 (E) — Shared Gizmo Layer: `ScatterGizmos`

Effort: **S** · Blocked by: nothing hard (skeleton lands with first of C/D; field-bounds line after Phase 2) · Parallel-safe with Phase 3, Phase 4

## Goal

One SSOT static `ScatterGizmos` of `Handles` helpers consumed by Phase 3 (density brush) and Phase 4
(placement) plus a `ScatterField` field-bounds gizmo. No per-tool gizmo copies (requirement #6 + DRY).

## Reuse check

NEW file. Consumes Unity `Handles` only. `ScatterField.EngineWorldBounds` (already exposed) feeds the
field-bounds gizmo.

## File ownership

### Created (under `Assets/GrassInteract/Editor/`)
- `ScatterGizmos.cs` — `internal static class ScatterGizmos` with `Handles`-based helpers:
  - `BrushDisc(Vector3 center, Vector3 normal, float radius, Color color)` — projected brush disc (consumed by Phase 3).
  - `FalloffRing(Vector3 center, Vector3 normal, float inner, float outer, Color color)` — falloff ring (Phase 3).
  - `InstanceDot(Vector3 pos, float size, Color color)` — unselected instance marker (Phase 4).
  - `Normal(Vector3 pos, Vector3 normal, float length, Color color)` — surface/instance normal (Phase 3/4).
  - `Aabb(Bounds bounds, Color color)` — selected-instance / field bounds box (Phase 4 + field bounds).
  - All methods are thin `Handles` wrappers; no state; genre-neutral.
  - Named-constant colors / default sizes (no magic literals at call sites where avoidable).

### Modified (after Phase 2 lands)
- `Assets/GrassInteract/Editor/ScatterFieldEditor.cs` (created in Phase 2) — add an `OnSceneGUI`/`DrawGizmo`
  that draws the field bounds via `ScatterGizmos.Aabb` over `ScatterField.EngineWorldBounds`.
  (If Phase 2's inspector is a `CustomEditor`, the field-bounds draw can also be a `[DrawGizmo]` callback —
  pick one during cook; do NOT duplicate the AABB logic, call `ScatterGizmos.Aabb`.)

## Constraints

- Unity `Handles` only; editor-only; NO Odin; genre-neutral name `ScatterGizmos`.
- **SSOT enforcement:** exactly one `ScatterGizmos.cs`. In a parallel fan-out, allocate this file to ONE
  owner (land it first, or fold it into whichever of C/D is implemented first); C and D consume it. Two
  teammates must not both create it (see plan.md dependency graph + conflict map).

## Risk table

| Risk | L | I | Score | Mitigation |
|------|:-:|:-:|:-:|------------|
| Two parallel owners both create `ScatterGizmos.cs` (SSOT split) | 2 | 2 | 4 | Single allocated owner; land skeleton with first of C/D; documented in plan.md. |
| Field-bounds gizmo references Phase 2 inspector before it exists | 2 | 2 | 4 | Sequence the `ScatterFieldEditor` modification after Phase 2; the `ScatterGizmos` static itself has no Phase-2 dependency and can land independently. |

## Success criteria (manually validated in-editor — no automated editor-UI test)

- `ScatterGizmos` is the only gizmo-drawing source; Phase 3 and Phase 4 call it (grep: no inline `Handles.DrawWireDisc`/`DrawLine` duplicating these in the tool files).
- Density brush disc + falloff ring render via `ScatterGizmos` (visible when Phase 3 tool active).
- Placement dots/normals/AABB/TRS bounds render via `ScatterGizmos` (visible when Phase 4 tool active).
- Selecting a `ScatterField` draws its field-bounds AABB over `EngineWorldBounds`.
