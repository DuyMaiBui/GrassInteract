# WorldPainter — Prop Placement UX Overhaul

**Created:** 2026-06-17
**Status:** Ready for cook
**Scope:** 3 brainstorm-approved features, single-agent execution
**Build order:** R2 (remove Single) → R3 (ghost E/R adjust) → R1 (Select scale headroom)
**Brainstorm source:** this session (all design decisions locked — no open questions)

---

## Goal

Three prop-tool UX changes in WorldPainter's editor:

1. **R2** — remove the redundant "Single" prop tool (functionally identical to "Place").
2. **R3** — let the artist rotate/scale the Place-mode ghost via held `E`/`R` + LMB-drag, sticky across placements.
3. **R1** — make the Select-tool scale handle update live at any size (currently dead on default layers).

## Locked design decisions (from brainstorm)

| # | Decision | Choice |
|---|----------|--------|
| D1 | Ghost adjust input | **Hold `E`/`R` + LMB-drag**, horizontal delta drives value |
| D2 | Ghost adjust persistence | **Sticky** across placements; `Esc` resets to yaw=0 / mul=1 |
| D3 | Select-mode scale cap | **Allow beyond layer range** — re-derive `ScaleMax` from actual instance scales + headroom |

## Hard constraints (engine reality)

- **GPU prop path renders yaw-only rotation + uniform scale** (`packedYawScale` = 16-bit yaw + 16-bit scale). `E` adjusts yaw; `R` adjusts uniform scale. Pitch/roll/non-uniform scale are NOT representable — explicitly out of scope.
- Ghost preview must stay **byte-identical** to the placed instance (existing invariant — `PropGhostPreview` mirrors `EmitExactlyOneAt` line-for-line). Any override must be applied in BOTH paths identically.
- Rotation compose order with align-to-normal: **`normalRot * yawRot`** (matches the scatter path in `AddInstancesFromPropLayer`).
- Live single-slot patch must NOT trigger a GPU buffer realloc (the black-tile flicker guard). A whole-buffer `SetData` re-upload is acceptable; `Dispose`/realloc of `argsLodN` is not.

---

## Phase 1 — R2: Remove the "Single" prop tool

**Why first:** pure deletion, zero new behavior, shrinks the surface the next two phases touch. No behavior loss — `InstancePlaceTool.Apply` already calls `EmitExactlyOneAt`, identical to `InstanceSingleTool`.

### Files & edits
| File | Edit |
|------|------|
| `Assets/WorldPainter/Editor/Brush/Tools/InstanceBrushTools.cs` | Delete `InstanceSingleTool` class (lines ~63-79); fix the file-header doc comment that lists "Single (place exactly one at the cursor)". |
| `Assets/WorldPainter/Editor/Brush/Tools/BrushToolRegistry.cs` | Remove `new InstanceSingleTool(),` from `InstanceTools` (line ~46). Palette → `Place / Erase / Select`. |
| `Assets/WorldPainter/Editor/WorldPainter/WorldPainterState.cs` | `IsClickOnlyTool` (line ~268): drop the `|| toolId == "instance.single"` term; fix the xmldoc on line ~263 listing the three ids. |
| `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.cs` | `isPlacementTool` (line ~214): drop `|| activeToolId == "instance.single"`. HUD `modeLabel` switch (line ~398): delete the `"instance.single" => "Mode: Single placement"` arm. Comment line ~192-193 ("Place / Single / Select") → "Place / Select". |

### Verify
- Compile clean (`read_console` no errors).
- Props palette shows exactly 3 tools.
- `grep -rn "instance.single\|InstanceSingleTool" Assets/WorldPainter` returns **zero** hits.

### Success criteria
- [ ] No reference to `instance.single` or `InstanceSingleTool` anywhere.
- [ ] Place tool still places exactly one instance per click (unchanged).

---

## Phase 2 — R3: hold-E/R handle to rotate/scale the frozen ghost (sticky)

**Depends on:** Phase 1 (the placement-tool id set is now `place`/`erase`/`select`).

**Interaction model (revised per user, 2026-06-17):** the ghost follows the cursor; holding `E`
(rotate) or `R` (scale) **freezes** the ghost at its current spot and shows an on-canvas transform
**handle** (yaw disc / uniform-scale gizmo) the artist drags freely. Releasing the key un-freezes
(ghost resumes following cursor) while the dialled-in yaw/scale persist (sticky). `Esc` resets.
This replaced the original raw-LMB-drag-delta idea — a real gizmo is clearer and gives a stable
manipulation target.

### New file
`Assets/WorldPainter/Editor/Brush/PropPlaceGhostController.cs` — `internal static` class mirroring the `PropGhostPreview` static pattern:
- `public static float GhostYawDeg` / `GhostScaleMul` (sticky; `Reset()` → 0 / 1).
- Held-key state `RotateHeld` / `ScaleHeld` + `IsAdjusting`; `SetRotateHeld`/`SetScaleHeld`.
- Freeze anchor: `FrozenPainting` / `HasFrozen` / `LastGhostPainting`; `RecordGhostPoint`,
  `CaptureFrozen(point)` (pins on first hold), `ClearFrozen`, `ClearHeld` (drops latch + freeze on
  focus-loss / tool-switch).
- Handle write-back: `SetYaw(deg)` (wrap 0-360), `SetScaleMul(mul)` (clamp > 0).

### Edits
| File | Edit |
|------|------|
| `WorldPainterSculptTool.cs` | `HandlePlacePropGhost(...)`: latch `E`/`R` on KeyDown/KeyUp, `CaptureFrozen` on first hold, `Esc`→`Reset`. When frozen, draw the ghost at `FrozenPainting` + `DrawPlaceGhostHandles` (yaw `Handles.Disc` / uniform `Handles.ScaleHandle`, write-back to the controller) and return true so the placement switch is skipped (no place mid-adjust). `ClearHeld` on `MouseLeaveWindow` / non-place tool. |
| `PropGhostPreview.cs` | `Draw(layer, cursorWorld, valid, yawDeg, scaleMul)` — store the two extra fields. In `OnSceneGui`: `scale *= scaleMul`; build `yawRot = Quaternion.Euler(0, yawDeg, 0)`; final `rot = (alignToNormal ? normalRot : identity) * yawRot`. Keep the place-from-anchor pivot formula (`pivot = cursor - rot*(anchor*scale)`) unchanged. |
| `WorldPainterPropStampEmitter.cs` | `EmitExactlyOneAt(layer, exactPos, sampler, yawDeg, scaleMul)` (new optional-override params, default 0/1 to preserve callers). Apply identically: `scale = midScale * scaleMul`; `rot = (alignToNormal ? normalRot : identity) * Quaternion.Euler(0, yawDeg, 0)`. |
| `InstanceBrushTools.cs` (`InstancePlaceTool.Apply`) | Pass `PropPlaceGhostController.GhostYawDeg` / `GhostScaleMul` to `EmitExactlyOneAt`. |
| `WorldPainterSculptTool.cs` HUD | Add a live `$"Yaw: {GhostYawDeg:F0}°  Scale: ×{GhostScaleMul:F2}"` line when active tool is `instance.place`. |

### Byte-identical guard
The yaw/scale apply block MUST be copy-identical between `PropGhostPreview.OnSceneGui` and `EmitExactlyOneAt`. Add a one-line code comment in each pointing at the other (existing convention in these files).

### Verify
- Hold `E`, LMB-drag → ghost yaws; release → no instance placed; click (no key) → instance lands at the shown yaw/scale.
- Hold `R`, LMB-drag → ghost scales; Shift makes it fine. `Esc` resets ghost.
- Placed instance visually matches the ghost (no drift) at non-zero yaw + non-unit scale + align-to-normal slope.

### Success criteria
- [ ] Ghost yaw/scale adjustable only while `E`/`R` held; never places during adjust.
- [ ] Sticky values carry to the next placement; `Esc` resets.
- [ ] Placed instance == ghost on a sloped, align-to-normal layer.

---

## Phase 3 — R1: Select-mode scale updates live at any size

**Depends on:** nothing in P2 (independent), but sequenced last as it's the deepest (runtime buffer + engine + test).

### Root cause (confirmed in scout)
`ChunkedInstanceBuffer.PatchInstance` (line ~507) and the render VS decode scale as `packed/65535 * ScaleMax`. `ScaleMax` is baked from `scaleRange.y`; default layer range `(1,1)` ⇒ `ScaleMax=1` ⇒ the Select scale handle cannot grow an instance past 1 (looks dead). Position/rotation have no such ceiling.

### Edits
| File | Edit |
|------|------|
| `ChunkedInstanceBuffer.cs` (bake, ~line 207) | Derive `ScaleMax = Max(scaleRange.y, maxObservedInstanceScale) * HEADROOM` (`HEADROOM = 1.5f`, named const). Re-derive on every (re)bake so the deferred mouse-up rebuild restores a correct ceiling. |
| `ChunkedInstanceBuffer.cs` (`PatchInstance`, line ~494) | If the patched `scale > ScaleMax`: raise `ScaleMax` to `scale * HEADROOM`, **re-encode + re-upload the whole instance buffer once** (full `SetData`, not realloc), and signal the engine to refresh `_ScaleMax2`. Within-ceiling drags keep the existing O(1) single-slot path. Return a flag or expose `ScaleMax` so the engine can re-push the uniform. |
| `InstancedPropEngine.cs` (~lines 343 / 441) | After a `PatchInstanceTransform` that raised `ScaleMax`, re-set the per-material `_ScaleMax2` uniform to `instanceBuffer.ScaleMax` (lockstep — else the live preview decodes against a stale divisor). |
| `WorldPainterPropTransformEdit.cs` (`TryLivePatch` / `CommitRecord`) | No structural change; confirm the scale write-back already flows here (it does). |
| `ChunkedInstanceBufferTests.cs` (line ~224) | Update the decode assertion to use the **re-derived** `ScaleMax` (with headroom), not `scaleRange.y`. Add a test: patch an instance scale above the baked ceiling → buffer `ScaleMax` rises and decode round-trips the new scale. |

### Verify
- New EditMode test green (`run_tests`).
- Default-range layer: select an instance, drag Scale handle up → mesh grows live past 1.0 (no cap), no flicker in Game/Inspector views.
- Mouse-up rebuild preserves the enlarged scale.

### Success criteria
- [ ] Select scale handle updates the rendered mesh live at any size, including on default `(1,1)` layers.
- [ ] No black-tile flicker during the drag (no GPU realloc).
- [ ] `_ScaleMax2` uniform stays in lockstep with the buffer's `ScaleMax`.
- [ ] All WorldPainter EditMode tests pass.

---

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|------------------|--------------|-------|------------|
| P2 LMB-drag-while-key-held leaks a placement click | 3 | 4 | 12 | Fully `e.Use()` MouseDown/Drag/Up while `E`/`R` held; manual verify "release places nothing". |
| P2 ghost ≠ placed instance drift (override applied in only one path) | 3 | 4 | 12 | Copy-identical apply block + cross-reference comments; explicit sloped-layer verify step. |
| P3 whole-buffer re-encode causes per-frame upload storm on large prop counts | 2 | 3 | 6 | Re-encode only when crossing the ceiling (rare), not per frame; within-ceiling stays O(1). |
| P3 `_ScaleMax2` lockstep missed → live preview decodes stale | 2 | 4 | 8 | Engine re-pushes uniform whenever `ScaleMax` rises; covered by new round-trip test. |
| P3 headroom re-derivation changes existing baked visuals | 2 | 3 | 6 | Headroom only raises the divisor ceiling; encoded ratio preserved — verify a populated prop layer renders unchanged. |

No risk ≥ 15. Highest are the two P2 input/identity risks (12) — both mitigated by full event consumption + the byte-identical guard.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: R2 remove Single | S (~0.5d) | Mechanical deletion; compile + grep gate. |
| Phase 2: R3 ghost E/R adjust | M (~1.5d) | New controller + event plumbing + dual-path apply; most verification surface. |
| Phase 3: R1 scale headroom | M (~1.5d) | Runtime buffer + engine lockstep + test update. |
| **Total** | **~3.5d** | Critical path: P2 then P3 (independent of each other, but P3 is the deepest single item). |

## Cook handoff

```
/t1k:cook plans/260617-worldpainter-prop-placement-ux/plan.md
```
