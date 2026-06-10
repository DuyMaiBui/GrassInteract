# Brainstorm — Instance Placement: Mesh Ghost + Transform Gizmo

Date: 2026-06-10 · Status: Design approved → plan requested

## Problem statement

Authoring an `InstanceScatterLayer` needs a scene-view placement tool where (a) selecting the
instance layer shows a **mesh preview that follows the mouse**, click = add instance data; and
(b) a **Select mode** where clicking a placed instance shows its transform + collider data and a
scene-view transform gizmo (translate / rotate / scale).

## Key finding — most of this already exists

`Assets/GrassInteract/Editor/InstancePlacementTool.cs` (`[EditorTool("Instance Placement", typeof(InstanceScatterLayer))]`)
already implements Place / Scatter / Select / Erase with raycast, Undo, and debounced rebuild.
The request is a **narrow enhancement**, not a new tool.

| Request | Current state | Work |
|---|---|---|
| Mesh preview follows mouse (Place) | ❌ flat wire disc (`ScatterGizmos.BrushDisc`) | NEW: mesh ghost |
| Click to add data | ✅ `OnPlace`→`BuildRecord`→`AddRecord` | reuse |
| Select → click instance | ✅ `OnSelect`+`PickNearest` | reuse |
| Transform gizmo on selected | ⚠️ all 3 handles stacked (`DrawSingleSelectHandles` L290–292) | REFINE → W/E/R single |
| Show transform + collider data | ⚠️ `InstancePanel` has collider only; no per-instance pos/rot/scale; no scene label | ADD transform fields + scene label |

## Approved decisions (AskUserQuestion)

- **Ghost render:** LOD0 mesh + real render material, semi-transparent (fallback unlit-green).
- **Transform gizmo:** single instance, follows Unity W/E/R active tool.
- **Data display:** both — scene `Handles.Label` + editable `InstancePanel` transform fields.
- **Delivery:** enhance existing `InstancePlacementTool` in place.

## Design — 3 pieces

### Piece 1 — `InstanceGhostPreview` (NEW static helper, ~70 LOC)
- Source: `layer.render.Lods[0].mesh` + `render.Material` (`ScatterRenderConfig.cs`: `Lods[0]` = LOD0; `Material` getter).
- Transform mirrors `BuildRecord`: pos=`hit.point`; rot=`AlignToNormal? FromToRotation(up,normal):identity` × yaw; scale=mid(`ScaleMin`,`ScaleMax`).
- **RandomYaw caveat:** random yaw is rolled per click and can't be previewed → ghost uses yaw 0 (stable footprint); HUD-noted. Tilt config is runtime spring-sim, not an authoring transform → not applied.
- Render via `Graphics.DrawMesh` in `duringSceneGui`, ~50% alpha via `MaterialPropertyBlock`; only when `hasHit`.
- **Spacing feedback:** tint red when `RespectsSpacing` is false (placement will be rejected).

### Piece 2 — W/E/R transform gizmo (refine `DrawSingleSelectHandles`)
Replace 3-stacked handles with single handle by `Tools.current`:
Move→`PositionHandle`, Rotate→`RotationHandle`, Scale→`ScaleHandle` (uniform .x), default→Move.
Same `BeginChangeCheck`→`Undo`→`SetRecord`→`Commit` flow (unchanged data path).

### Piece 3 — Transform + collider data display
- Scene: compact `Handles.Label` near selected instance (pos/rot-euler/scale, collider on/off).
- `InstancePanel`: add a **Transform** section above collider section — `Vector3Field` pos,
  `Vector3Field` rot(euler), `FloatField` scale; Undo-wrapped via `SetRecord`+`MarkDirty`.
  Keeps gizmo edits and panel edits in agreement.

## Files

| File | Change | ~LOC |
|---|---|---|
| `Editor/ScatterStudio/InstanceGhostPreview.cs` | NEW | 70 |
| `Editor/InstancePlacementTool.cs` | ghost call in Place; W/E/R handle rewrite; scene label | 50Δ |
| `Editor/ScatterStudio/InstancePanel.cs` | add Transform fields section | 50Δ |
| `Editor/ScatterGizmos.cs` | optional label helper | 10 |

Reused unchanged: `BuildRecord`, `OnPlace`, `OnSelect`, `PickNearest`, `Commit`,
`ScatterRebuildScheduler`, `ScatterAuthoringState`, `AuthoredInstancesData.*`, `InstancePlacementToolTracker`.

## Risks
- Ghost yaw ≠ placed yaw under RandomYaw (accepted, HUD-noted).
- Transparent ghost depends on material's transparent path → unlit-green fallback guarantees visibility.
- Verify exact `ScatterLod.mesh` accessor + a null-LOD guard (layer may have no LODs assigned) at implement time.

## Success criteria
- Selecting an instance layer + activating the tool shows a mesh ghost tracking the cursor; click places an instance matching the ghost footprint.
- Select mode: click shows gizmo honoring W/E/R; drag edits the instance with Undo + live rebuild.
- Selected instance's pos/rot/scale + collider state visible in both scene label and InstancePanel, mutually consistent.
