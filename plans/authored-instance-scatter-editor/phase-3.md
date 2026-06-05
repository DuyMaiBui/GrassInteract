---
phase: 3
name: edit-brush
effort: S
agent: t1k-unity-developer
blocked-by: P1, P2
blocks: P4, P5
---

# Phase 3 - Edit Brush

## Goal

Brush-edit mode: re-randomize rotation / nudge-scale / nudge-position / align-to-normal across all instances inside brush radius, with falloff weighting. Op selector inside the EditBrush block.

## Scope

IN: 4 brush-edit ops (RandomizeRotation, NudgeScale, NudgePosition, ToggleAlignNormal); falloff math reused from existing ScatterBrush; per-stroke Undo.

OUT: engine integration (P4); migration (P5).

## File Ownership

| File | Action |
|---|---|
| Editor/ScatterBrush.cs | EDIT - add EditBrush stamp path: query InstancePickingService.QueryRadius, apply selected op per index with falloff weight |
| Editor/TerrainScatterConfigEditor.cs | EDIT - render EditBrush op selector + per-op param block (e.g. NudgeScale shows delta range; NudgePosition shows nudgeRadius) |
| Runtime/AuthoredInstancesData.cs | EXTEND - batch SetRecords(IList<(int,InstanceRecord)>) for stroke-end commit (avoid per-stamp serialization) |

## Step-by-Step Tasks

1. **Define BrushEditOp enum**: RandomizeRotation, NudgeScale, NudgePosition, ToggleAlignNormal. Default = RandomizeRotation.
2. **Op selector UI** in TerrainScatterConfigEditor under EditBrush block: GUIToggleGroup with 4 toggles. Below it, per-op param panel:
   - RandomizeRotation: nothing (uses layer.YawRange).
   - NudgeScale: FloatField scaleDelta (default 0.1, range 0.01-1).
   - NudgePosition: FloatField nudgeRadius (default 0.2 m, range 0.05-2 m).
   - ToggleAlignNormal: nothing (boolean op).
3. **ScatterBrush.EditBrushStamp** (mouse-down): Undo.RegisterCompleteObjectUndo(sidecar, Brush Edit Stroke).
4. **Per-stamp**: InstancePickingService.QueryRadius(cursor, radius); for each idx, compute falloff w = ScatterBrush.SampleFalloff(distance/radius); apply op:
   - RandomizeRotation: newYaw = lerp(currentYaw, Random.Range(layer.YawRange), w * opacity).
   - NudgeScale: scale = lerp(scale, scale * (1 + Random.Range(-scaleDelta, scaleDelta)), w * opacity), clamped to layer.ScaleRange.
   - NudgePosition: dxz = Random.insideUnitCircle * nudgeRadius * w * opacity; pos.xz += dxz; resample y via ISurfaceSampler.SampleHeight(layer.surface, pos.xz); set pos.y.
   - ToggleAlignNormal: bit-flip overrideMask aligned bit; if newly-aligned, resample rotation from surface normal via existing align helper in GrassScatter.
5. **Batch commit**: per stamp, edits accumulate to a List<(int,InstanceRecord)>; flushed to AuthoredInstancesData via SetRecords at stroke-end OR on a throttle (every ~32 ms) to keep gizmo responsive. Brainstorm target: rotate 1000 inst <50 ms / stroke.
6. **Falloff visualization**: reuse existing brush-cursor shader; no change needed.
7. **Density-mask interaction**: EditBrush does NOT paint density mask (it only modifies authored records). Document in tooltip.
8. **Throughput probe**: paint a 1000-instance patch (from P1), switch to EditBrush + RandomizeRotation, single stamp over the patch, time the stamp. Record in phase-3-report.md (<50 ms target).

## Verification Gate

1. refresh_unity + read_console: clean compile.
2. ScatterInstanceCullHarness: PASS unchanged.
3. Manual EditBrush test: paint a 1000-inst patch via Place; switch to EditBrush + RandomizeRotation; single stamp visibly rotates blades inside radius with falloff (center most rotated, edge least).
4. NudgePosition test: blades inside radius shift; y is re-snapped to surface (no floaters).
5. ToggleAlignNormal test: stamp on a sloped patch; align-mask flips; rotation re-aligns to surface normal.
6. Undo: single Ctrl+Z reverts the full stroke (verified on a 1000-inst stamp).
7. Stroke throughput: <50 ms / 1000-inst stamp on desktop; record in phase-3-report.md.
8. Screenshot: before + after EditBrush stamp, save screenshots/phase-3.png.

## Exit Criteria

- 4 brush-edit ops functional with falloff.
- Per-stroke Undo works.
- Throughput within target.
- ScatterInstanceCullHarness PASS.
- phase-3-report.md written.

## Rollback Plan

- Revert ScatterBrush.cs to P2-end.
- Revert TerrainScatterConfigEditor.cs to P2-end.
- Revert AuthoredInstancesData.SetRecords addition (keep single-record SetRecord from P2).
- Existing sidecar data is unaffected (no schema change in P3).
- Rollback risk: LOW.

