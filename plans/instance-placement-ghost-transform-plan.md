# Plan: Instance Placement — Mesh Ghost + Transform Gizmo

**Branch:** `feat/grass-authoring-toolchain` · **Engine:** Unity 6 · Editor tooling
**Source:** brainstorm 2026-06-10 (approved) — `plans/reports/instance-placement-ghost-transform-brainstorm.md`
**Scope:** ENHANCE the existing `InstancePlacementTool` — NOT a new tool. ~180 LOC across 4 files.
**Execution:** one parallel wave (3 near-disjoint units) → one compile + test gate.

## Approved decisions (AskUserQuestion, 2026-06-10)
| Topic | Choice |
|---|---|
| Ghost render | LOD0 mesh + real render material, ~50% alpha; unlit-green fallback |
| Transform gizmo | Single instance, follows Unity W/E/R (`Tools.current`) |
| Data display | Both — scene `Handles.Label` + editable `InstancePanel` transform fields |
| Delivery | Enhance `InstancePlacementTool` in place |

## Ground truth (verified this session)
- `InstancePlacementTool.cs` `[EditorTool("Instance Placement", typeof(InstanceScatterLayer))]` — Place/Scatter/Select/Erase already implemented; raycast, Undo, `Commit`→`ScatterRebuildScheduler.MarkDirty` all working.
- Place mode today draws only `ScatterGizmos.BrushDisc` (wire disc) — `OnToolGUI` L113–119.
- `BuildRecord` (L208–220): yaw = `RandomYaw? Random.Range(0,360):0`; align = `AlignToNormal? FromToRotation(up,normal):identity`; scale = `Random.Range(min,max)`.
- `DrawSingleSelectHandles` (L282–303) ALREADY draws `PositionHandle`+`RotationHandle`+`ScaleHandle` **stacked** → refine to single W/E/R handle.
- `ScatterRenderConfig` (`Runtime/ScatterRenderConfig.cs`): `Material? Material` getter; `ScatterLod[] Lods` getter; `Lods[0]` = LOD0 highest detail; `LodMeshes` helper exists. **`ScatterLod.mesh` field accessor + empty-LOD guard must be confirmed at implement time.**
- `ScatterInstanceTiltConfig` = runtime spring-sim, NOT an authoring transform → ghost does NOT apply tilt.
- `InstancePanel.cs` — IMGUI `DrawInstanceGUI` shows collider fields only (no pos/rot/scale); `InstancePlacementToolTracker.ActiveTool` exposes `SelectedIndex`/`MultiSelection`.
- `AuthoredInstancesData`: `TryGetRecord(idx, out InstanceRecord)`, `SetRecord(idx, rec)`, `PackBlob()`.
- Tests: `GrassInteract.EditorTests` is the compile + regression gate.
- Conventions: `#nullable enable`, `this.` prefix, camelCase private fields (no underscore), namespace `GrassInteract.Editor`.

## Dependency graph — 3 near-disjoint units → one parallel wave
```
U1  NEW  Editor/ScatterStudio/InstanceGhostPreview.cs        (Piece 1 — ghost helper)
    EDIT Editor/InstancePlacementTool.cs  → OnPlace/OnToolGUI ghost call (Place branch only)
U2  EDIT Editor/InstancePlacementTool.cs  → DrawSingleSelectHandles rewrite + scene Handles.Label
U3  EDIT Editor/ScatterStudio/InstancePanel.cs → Transform section above collider section
    (opt) EDIT Editor/ScatterGizmos.cs → label helper if not inlined
```
**Overlap note:** U1 and U2 both edit `InstancePlacementTool.cs` but in **disjoint regions** (U1 = Place branch ~L113–119 + `OnPlace`; U2 = `DrawSingleSelectHandles` + `OnSelect` label). If run in parallel, assign BOTH `InstancePlacementTool.cs` edits to a single teammate to avoid an index race, OR sequence U1→U2. Recommended for solo `/t1k:cook`: do U1, U2, U3 sequentially in one agent (small scope). U3 is fully disjoint and may parallelize.

---

## Phase 1 — Piece 1: Mesh ghost preview (Place mode)

**Files:** NEW `Editor/ScatterStudio/InstanceGhostPreview.cs`; EDIT `InstancePlacementTool.cs` (Place branch).

### Steps
1. **Verify LOD accessor** → confirm `render.Lods[0].mesh` field name (read `ScatterLod.cs`); add guard: if `render.Lods` is null/empty or `Lods[0].mesh == null` → ghost is a no-op (fall back to existing wire disc so Place still has a cursor). → verify: null-LOD layer shows disc, no exception.
2. **Create `InstanceGhostPreview`** (static, `#nullable enable`, namespace `GrassInteract.Editor`):
   - `static void Draw(InstanceScatterLayer layer, Vector3 hitPoint, Vector3 hitNormal, bool spacingOk)`.
   - Compute transform mirroring `BuildRecord` minus randomness: pos=`hitPoint`; rot=`AlignToNormal? FromToRotation(up,hitNormal):identity` (yaw 0 — documented: RandomYaw can't be previewed); scale=mid(`ScaleMin`,`ScaleMax`).
   - Build `Matrix4x4.TRS(pos, rot, Vector3.one*scale)`.
   - Material: clone `render.Material` into a cached preview material; set alpha ~0.5 via `MaterialPropertyBlock` (`_Color`/`_BaseColor`); if material lacks a transparent path or is null → cached unlit-green fallback material. Tint **red** when `!spacingOk`.
   - `Graphics.DrawMesh(mesh, matrix, mat, layer:0, camera:null, submesh:0, props)` — called from `OnToolGUI` (runs within scene GUI; `DrawMesh` queues for the SceneView camera).
   - Static cached materials; dispose on `AssemblyReloadEvents.beforeAssemblyReload` (match `ScatterBrushPreview` pattern).
3. **Wire into Place branch** — in `OnToolGUI`, replace the Place-mode portion of the `hasHit && Mode != Select` disc block: when `Mode==Place && hasHit`, call `InstanceGhostPreview.Draw(layer, hit.point, hit.normal, RespectsSpacing(...))` instead of (or layered with) the disc; keep disc for Scatter/Erase. → verify: ghost tracks cursor, turns red where a click would be spacing-rejected.
4. **HUD note** — extend `DrawMinimalHud` to show "yaw randomized on place" when `RandomYaw` is on. → verify: label visible.

### Verify
- Ghost mesh matches what `OnPlace` creates (footprint/scale/normal-align); click places an instance under the ghost.
- Null-LOD layer: no exception, disc fallback.
- No leaked materials after domain reload.

---

## Phase 2 — Piece 2: W/E/R transform gizmo + scene label (Select mode)

**Files:** EDIT `InstancePlacementTool.cs` (`DrawSingleSelectHandles`).

### Steps
1. **Replace stacked handles** with single `Tools.current`-driven handle:
   - `Move/Transform/None` → `Handles.PositionHandle(pos, rot)`.
   - `Rotate` → `Handles.RotationHandle(rot, pos)`.
   - `Scale` → `Handles.ScaleHandle(Vector3.one*scale, pos, rot, GetHandleSize(pos))` → uniform `.x`.
   - Keep the existing `EditorGUI.BeginChangeCheck`→`Undo.RegisterCompleteObjectUndo`→`SetRecord`→`Commit` flow; write back only the field the active handle changed (avoid clobbering rot/scale when only moving). → verify: each of W/E/R drives only its channel; Undo restores.
2. **Scene `Handles.Label`** near the selected instance: `pos (xyz) / rot (euler) / scale / collider:on|off`, small offset above `rec.position`, `EditorStyles.miniLabel`. → verify: label readable, updates live during drag.

### Verify
- W selects move handle, E rotate, R scale; switching Unity tools switches the gizmo.
- Drag edits persist, rebuild fires, Undo works.
- Label values match `InstancePanel` values for the same instance.

---

## Phase 3 — Piece 3: Transform section in InstancePanel

**Files:** EDIT `Editor/ScatterStudio/InstancePanel.cs`.

### Steps
1. **Add Transform section** in `DrawInstanceGUI` (IMGUI, above the collider block, single-select only):
   - `Vector3Field`/`EditorGUILayout.Vector3Field` position; rotation as euler (`rec.rotation.eulerAngles` → `Quaternion.Euler`); `FloatField` scale (clamp `>0.0001`).
   - On change: `Undo.RegisterCompleteObjectUndo(authored,"Edit Instance Transform")` → `SetRecord` → `PackBlob` → `SetDirty` → `MarkDirty(field, layerIdx)` (mirror the existing collider-edit pattern in the same method). → verify: editing a field moves the instance + scene gizmo/label agree.
2. **Consistency** — ensure gizmo-drag (Phase 2) and panel-edit write the same `InstanceRecord` fields so the two surfaces never diverge. → verify: drag in scene updates panel on repaint; edit in panel updates scene.

### Verify
- Transform fields appear above collider fields for a single selection; hidden for multi-select (batch already handles that).
- Edits are Undo-wrapped and trigger live rebuild.

---

## Gate (run once, after all 3 phases)
1. `mcp__UnityMCP__refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=True)` then `read_console` → zero compile errors (read ALL errors, not first).
2. `run_tests` → `GrassInteract.EditorTests` green (no regressions).
3. Manual smoke in SceneView: select an `InstanceScatterLayer`, activate the tool → ghost follows cursor; click places matching instance; Select + W/E/R edits selected instance; panel + scene label consistent.

### Risk Assessment
| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| `ScatterLod.mesh` accessor name differs from assumption | 3 | 2 | 6 | Phase 1 Step 1 reads `ScatterLod.cs` first; guard added regardless |
| Render material has no transparent path → ghost invisible/opaque | 3 | 3 | 9 | Unlit-green fallback material guarantees visibility |
| U1+U2 both edit `InstancePlacementTool.cs` → index race if parallelized | 3 | 3 | 9 | Sequence U1→U2 in one agent, or single-teammate ownership of that file |
| `Graphics.DrawMesh` not visible from `OnToolGUI` timing | 2 | 3 | 6 | Fallback: move ghost draw to a `SceneView.duringSceneGui` push like `ScatterBrushPreview` |
| Gizmo write-back clobbers untouched channels | 2 | 3 | 6 | Write only active-handle field; full record round-trips via `SetRecord` |

### Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Mesh ghost | M (~0.5d) | Material/transparency is the only unknown |
| Phase 2: W/E/R gizmo + label | S (~0.3d) | Refines existing code |
| Phase 3: Panel transform fields | S (~0.3d) | Mirrors existing collider pattern; fully disjoint |
| Total | ~1–1.5d | Critical path: Phase 1 (material) → gate |

---

## Cook handoff
`/t1k:cook plans/instance-placement-ghost-transform-plan.md`
