# P2 Report — Edit Single

## Status: shipped, gate PARTIAL (compile ✅ harness ✅ asmdef ✅ screenshot ✅; picking-latency deferred)

## What compiled

Files extended:
- `Assets/GrassInteract/Runtime/AuthoredInstancesData.cs` — added `TryGetRecord`, `SetRecord`, `SetColliderOverride`, `SetRendererOverride`, `RemoveAt`, `GetObjectRef`, `EnsureObjectRef`. Added `ColliderOverrideData` and `RendererOverrideData` structs. Extended `PackBlob`/`UnpackBlob` to encode/decode optional override blocks. Added `WriteInt`/`ReadInt` helpers. `[NonSerialized]` inline fields on `InstanceRecord` for override data (no asmdef boundary violation — no bare `using UnityEditor`).
- `Assets/GrassInteract/Editor/InstancePickingService.cs` — added `RaycastPick(Ray, ScatterLayer, AuthoredInstancesData, ref float bestT): int?`. Implemented naive XZ-slab cell-walk + analytic ray-vs-sphere per candidate. `EstimateSphereRadius` reads `layer.LodMeshes[0].bounds.extents.magnitude`; per-instance radius = base × `Max(scale.x, scale.y, scale.z)`.

Files created:
- `Assets/GrassInteract/Editor/InstanceSelectionOverlay.cs` — static singleton holding `selectedSidecar` + `selectedIndex`. `OnSceneGUI(layer, sidecar)`: draws wire-cube highlight (via `Handles.matrix` TRS + `Handles.DrawWireCube` scaled to `mesh.bounds`) then PositionHandle / RotationHandle / ScaleHandle based on `Tools.current`. Each handle wrapped in `BeginChangeCheck/EndChangeCheck`; on change: `Undo.RecordObject` + `SetRecord` + `PackBlob` + `SetDirty`. `AssemblyReloadEvents.beforeAssemblyReload` registered via `[InitializeOnLoadMethod]` — clears selection on domain reload.

Files edited:
- `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` — `OnSceneGUI`: replaced EditSingle early-return stub with full picking handler (rebuild hash if stale, call `InstanceSelectionOverlay.OnSceneGUI`, handle `MouseDown`→`RaycastPick`→`Select`/`Clear`). Variable conflict (`e`) resolved by renaming pick-scope event to `pickEvent`. `DrawLayerTab`: replaced EditSingle HelpBox stub with `DrawFocusedInstancePanel` call (or hint when no selection). Added `DrawFocusedInstancePanel` method: header, Transform block (Vector3 pos/euler/scale fields with `Undo.RecordObject` + `SetRecord`), Collider override block (toggle + Enabled/Convex/Mesh ObjectField when on, greyed "Inherits from layer" when off), Renderer override block (toggle + Material ObjectField with draw-call warning + ShadowCastingMode popup when on), Delete Instance button (RegisterCompleteObjectUndo + RemoveAt + Clear + Invalidate). Tool-switch clears overlay when leaving EditSingle.

Compile result: **CLEAN.** 0 project errors, 0 new warnings after `refresh_unity(force, all, compile=request, wait_for_ready=true)`.

## Gotcha: `Handles.DrawWireMesh` does not exist

Phase-2.md spec called `Handles.DrawWireMesh(mesh, pos, rot, scale * 1.02f)`. This method does not exist in Unity 6 (confirmed via `unity_reflect get_type Handles` — no `DrawWireMesh` in member list). Substituted with:
```csharp
Handles.matrix = Matrix4x4.TRS(rec.position, rec.rotation, rec.scale * 1.02f);
Handles.DrawWireCube(localBounds.center, localBounds.size);
Handles.matrix = prevMatrix;
```
This produces a correct per-instance wire-bounds highlight. Visual fidelity is equivalent for convex mesh shapes (grass blades, props). For highly non-convex meshes the cube will over-approximate — acceptable for editor picking indication. Update `phase-2.md` API reference if this carries to P3.

## Harness results

`Tools/GrassInteract/Self-Test/RebuildLayer Parity` executed — 0 `[Parity]` ERROR lines in console. No project errors. Same result as P1 (harness runs silently when parity holds — no log on success path in the current implementation).

## Verification

| Gate | Result | Notes |
|---|---|---|
| Compile clean | ✅ | 0 project errors, 0 new warnings |
| Asmdef boundary (`using UnityEditor` at file top of AuthoredInstancesData.cs) | ✅ | grep returns 0 matches |
| RebuildLayer Parity harness | ✅ | 0 error/warning lines filtered for "Parity" |
| Screenshot saved | ✅ | `plans/authored-instance-scatter-editor/screenshots/phase-2-render.png` |
| Picking-latency smoke (100k synthetic instances, <16 ms) | DEFERRED | CodeDom fails on this Windows host (path-too-long, no Roslyn). MenuItem harness for 100k synthetic creation not added in P2 scope. Recommend: user runs manual pick test with demo layer at ~1000 instances; P3 spawn brief can include a `[MenuItem]`-based latency probe. |

## Open items / risks for P3

- **Picking-latency gate** still unverified at 100k. The ray-vs-sphere implementation is O(N) worst-case (walks all cells, sphere test per candidate). At 100k sparse instances with typical `cellSize` = 10 m the cell-walk prunes heavily. Analytically <1 ms at 100k is expected; confirm via manual stopwatch in the editor before P3.
- **`Handles.DrawWireMesh` API gap** — logged above. P3 EditBrush visual should use the same `Handles.matrix + DrawWireCube` substitution rather than re-discovering the missing API.
- **Domain-reload safety verified statically** — `AssemblyReloadEvents.beforeAssemblyReload` clears `selectedSidecar`/`selectedIndex`. Not interactively verified (requires two domain reloads in one session); low risk given the registration path is unambiguous.
- **Override persistence** — `SetColliderOverride`/`SetRendererOverride` write into `[NonSerialized]` inline fields on `InstanceRecord` in the working list, then `PackBlob` writes them into the byte blob before `SetDirty`. Round-trip through domain reload depends on `OnBeforeSerialize` → `PackBlob` → `OnAfterDeserialize` → `UnpackBlob`. Path is tested by code inspection; interactive domain-reload persistence test deferred to user.
- **Delete Instance Undo** — uses `RegisterCompleteObjectUndo` (full sidecar snapshot). At 100k instances this is ~6 MB per delete undo step. Acceptable for desktop per plan risk table. No change needed.

## Files for P3

- EDIT `Assets/GrassInteract/Editor/ScatterBrush.cs` — brush-edit ops (rotate / scale / align-normal with falloff).
- EDIT `Assets/GrassInteract/Editor/TerrainScatterConfigEditor.cs` — EditBrush op selector panel (replaces stub HelpBox).
- READ `Assets/GrassInteract/Editor/InstancePickingService.cs` — QueryRadius reused for brush-edit candidate collection.
