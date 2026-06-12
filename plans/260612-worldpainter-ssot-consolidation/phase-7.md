# Phase 7 — Dual-mode prop placement

**Effort:** L · **Wave:** D (PARALLEL fan-out) · **Depends on:** P1 (per-tile prop TRS buckets), P5 (prop layer defs + active-layer API) · **Blocks:** P9

## Goal

Two sub-modes per prop layer, switched by an **explicit toggle UI + shortcut key**:
- **Scatter (brush):** drag brush → randomly places prop instances in footprint (density, jitter, random yaw, scale range, ground-snap).
- **Transform (select):** click an instance → move/rotate/scale gizmo for fine edits.

Per-layer **anchor config** (pivot offset, ground-snap, align-to-surface-normal) — layer-wide, no per-instance override. Inspector preview: square LOD0 mesh preview + live instance count/stats + in-scene per-instance gizmos (no heavy list). Instances are explicit TRS records **bucketed per-tile** (P1 buckets) so they bake/stream with the tile.

## File-ownership group (this phase = ONE concurrent subagent in WAVE D)

**G7.1 — Prop placement (Editor + Runtime, disjoint from P4/P5/P8)**
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterPropLayerCard.cs` *(edit)* — per-layer anchor config (pivot/ground-snap/align-normal); mode toggle (Scatter↔Transform) + shortcut key; LOD0 preview; live count/stats.
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterPropStampEmitter.cs` *(edit)* — Scatter mode: brush footprint → random TRS instances (density, jitter, yaw, scale range, ground-snap) written to the per-tile bucket of the overlapped tile.
- `Assets/WorldPainter/Runtime/Scatter/InstanceScatterLayer.cs` *(edit)* — anchor config fields (layer-wide).
- `Assets/WorldPainter/Runtime/Scatter/AuthoredInstancesData.cs` *(edit — coordinate w/ P1)*: P1 adds the per-tile bucket keying; P7 adds the TRS write/read accessors. **Coordination:** P1 owns the bucket *structure*; P7 owns *placement accessors*. If both must edit, P1 ships a stub bucket API and P7 fills placement — else demote P7's runtime edit behind P1.
- Transform mode: SceneView click-pick an instance → standard `Handles` move/rotate/scale gizmo → write back to the bucket. (In `WorldPainterPropStampEmitter` or a sibling new file `WorldPainterPropTransformEdit.cs` owned solely by P7.)

## Non-overlap proof (WAVE D safety)

- P7 owns `WorldPainterPropLayerCard`, `WorldPainterPropStampEmitter`, (new) `WorldPainterPropTransformEdit`, `InstanceScatterLayer`.
- P5 owns the *other* palette files + the prop-section shell; P7 owns prop *placement behavior*. Disjoint files.
- `AuthoredInstancesData.cs` shared with P1 only — resolved by P1-ships-structure / P7-fills-accessors stub rule (P1 lands in WAVE A, frozen before P7 starts).

## Parallelizable vs sequential

**Parallel** with P4/P5/P8. Internally sequential (Scatter mode before Transform mode).

## Verification

1. **Compile:** `read_console` + `run_tests` in one pass.
2. **Existing test stays green:** `WorldPainterPropStampEmitterTests`, `AuthoredInstancesDataBlobTests`.
3. **New test:** `WorldPainterPropPlacementTests.cs` — Scatter a brush footprint over a tile → assert N TRS instances appear in that tile's bucket with yaw/scale within configured ranges; Transform-edit one instance → assert its TRS updated in the bucket; per-layer anchor (ground-snap) applied.
4. Inspector: live count updates after scatter; LOD0 preview renders; per-instance gizmos visible in SceneView.

## Success criteria (maps to design success criterion 6)

- Brush-scatter + transform-edit both work, switched by explicit toggle + shortcut.
- Per-layer anchor (pivot/ground-snap/align-normal) applied layer-wide.
- Inspector shows LOD0 preview + live count + in-scene gizmos (no heavy list).
- Instances bucketed per-tile (bake/stream with the tile in P8).
- Project compiles; tests green.
