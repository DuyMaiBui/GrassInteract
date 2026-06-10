# Plan: Terrain-Style Density Brush Redesign

Date: 2026-06-10 · Source design: `plans/reports/density-brush-terrain-style-redesign-design.md` (approved) · Skill: t1k-plan

Replaces the white-plane heatmap + CPU per-pixel paint with: (a) a strictly opt-in heatmap overlay,
(b) a normal-aligned textured brush decal cursor, and (c) a GPU brush-splat pipeline that reads back to
the existing R8 PNG SSOT only on the 0.15s rebuild tick. Editor-only, no DOTS.

All design decisions are approved and frozen — this plan encodes them as phases. No open decisions.

---

## Scope summary

### In scope (6 phases)
1. Kill the white plane in `ScatterDensityOverlay.cs` (delete `Sprites/Default` fallback, strict shader gate, bind live RT).
2. New `ScatterBrushPreview.cs` — normal-aligned textured brush decal.
3. New `DensityPaintBrush.shader` + `DensityPaintGPU.cs` — GPU splat + gated readback.
4. Modify `DensityPaintTool.cs` — GPU splat + stroke interpolation; swap wire-disc preview for decal.
5. Modify `DensityMapFactory.cs` — add `RT->Color[]` readback helper (reused by tick + EndStroke).
6. EditMode tests — UV mapping, stroke-interpolation spacing, RT-readback golden.

### Out of scope (explicit — do NOT touch)
- Instance-placement tool (`InstancePlacementTool`, `ScatterGizmos.InstanceDot/Normal/Aabb` stay as-is).
- Brush library UI.
- Layer rail.

### SSOT — reuse, do NOT duplicate
- `GrassFieldSpace` (world↔UV) — the only world↔UV mapping. New GPU/UV code derives from it; never re-derive a rect.
- `ScatterAuthoringState.I` — all brush state (Size/Opacity/Falloff/Flow/PaintMode/ActiveStamp/OverlayVisible). No EditorPrefs, no new state singleton.
- `ScatterRebuildScheduler` (0.15s debounce, `MarkDirty`) — the only live-rebuild trigger. Readback hooks this tick; never call `RebuildLayer` directly and never readback per paint event.
- `DensityMapFactory.PersistPixels` — the only PNG-persist path (R8 contract unchanged).
- `DensityPaintTool.ResolveStamp` — the only stamp→`Texture2D` resolver. `ScatterBrushPreview` and `DensityPaintGPU` consume the resolved texture; they do NOT re-resolve `StampRef`.

---

## Feasibility

- **Reuse check:** all 5 SSOT seams above already exist and are exercised by current code (read & confirmed). New work is additive (2 new `.cs` + 1 `.shader`) plus surgical edits to 3 existing files.
- **Complexity:** moderate. The GPU splat + linear-RT + gated-readback wiring is the only genuinely new mechanism; everything else is rewiring existing flow.
- **asmdef:** all files live under the existing `Assets/GrassInteract/Editor/GrassInteract.Editor.asmdef` boundary. No asmdef change → no asmdef-only no-op-refresh gotcha.
- **Conventions:** `this.` prefix mandatory; private fields `camelCase` (no underscore); constants `UPPER_SNAKE_CASE`; `#nullable enable`; one responsibility per file.

---

## Phase dependency graph

```
P1 (overlay)   ─ parallel-safe ─┐
P2 (preview)   ─ parallel-safe ─┤
P5 (readback helper in factory) ┴─ blocks ─> P3 (GPU pipeline) ─ blocks ─> P4 (tool rewire) ─ blocks ─> P6 (tests)
```

- **Critical path:** P5 → P3 → P4 → P6.
- **Parallel-safe:** P1 and P2 own disjoint files from the critical path and may land in any order (recommend P5 + P1 + P2 first as a batch, then P3, P4, P6).
- **File-ownership rule:** no two phases edit the same file. `DensityPaintTool.cs` is owned exclusively by P4; `ScatterDensityOverlay.cs` by P1; `DensityMapFactory.cs` by P5.

---

## Phase 1 — Kill the white plane (strict opt-in heatmap)

**Objective:** No unbound-texture quad can ever render. Heatmap is opt-in (`OverlayVisible` already defaults `false`) and renders only when the dedicated shader resolves; it binds the live paint RenderTexture during a stroke so it stays correct.

**File ownership (exact paths):**
- Modify: `Assets/GrassInteract/Editor/ScatterDensityOverlay.cs` (sole owner this phase)

**Steps:**
1. Delete `BuildFallbackShader()` entirely (the `Sprites/Default` → `Unlit/Transparent` fallback at lines 226-239). Errors-over-silent-fallback: never render an unbound `_MainTex` quad.
2. In `CreateHeatmapMaterial()` (lines 204-224): if `Shader.Find(SHADER_NAME)` returns null → `Debug.LogWarning` **once** (guard already exists via `materialCreateAttempted`) and return `null!`; the existing `if (quadMesh == null || heatmapMaterial == null) return;` guard in `OnSceneGui` then draws nothing. Remove the now-dead reference to the fallback.
3. Add a live-RT bind seam: extend `SetActiveLayer` (or add an overload `SetActiveRenderTexture(RenderTexture? rt)`) so `DensityPaintGPU` (P3) can hand the in-flight stroke RT to the overlay. In `OnSceneGui`, prefer the bound RT when present, else fall back to `activeLayer.DensityMap`, binding it to `_DensityTex` (line 104). The seam is created here but only populated by P3 — until then it is null and the map binds as today.
4. Confirm `OverlayVisible` gate (line 76) stays first — no change needed (default `false` already verified in `ScatterAuthoringState`).

**Verify / success criteria:**
- Grep `ScatterDensityOverlay.cs` for `Sprites/Default` and `Unlit/Transparent` → **0 hits**.
- With `Hidden/GrassInteract/DensityHeatmap` absent: enabling the overlay logs exactly one warning and draws nothing (no white plane). Confirm via Scene view + Console.
- With overlay off (default): zero overlay draw calls regardless of shader presence.
- Project compiles clean (`read_console`, zero errors).

**Risk Assessment**

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| Dedicated heatmap shader never authored → overlay silently does nothing | 3 | 2 | 6 | Acceptable by design (opt-in, off by default). One-time warning surfaces the cause; not on the paint critical path. |
| RT-bind seam left null forever if P3 slips | 2 | 1 | 2 | Seam falls back to `DensityMap` binding (current behavior) when RT is null — no regression. |

**Timeline**

| Phase | Effort | Notes |
|---|---|---|
| Phase 1 | S | Deletion + one-line gate + null-safe RT seam. Parallel-safe with P2/P5. |

---

## Phase 2 — Normal-aligned textured brush decal

**Objective:** Replace the wire-disc cursor (`BrushDisc`/`FalloffRing`) with a single quad oriented to `hit.normal`, sized to brush radius, textured with the resolved stamp (procedural soft disc when stamp=None), mode-tinted, alpha=falloff — drawn via `Graphics.DrawMeshNow` with one cached material + mesh.

**File ownership (exact paths):**
- New: `Assets/GrassInteract/Editor/ScatterStudio/ScatterBrushPreview.cs` (sole owner)
- (Consumed by P4; this phase does not edit `DensityPaintTool.cs`.)

**Steps:**
1. New `internal static class ScatterBrushPreview` mirroring `ScatterDensityOverlay`'s resource pattern: one cached `Mesh quad`, one cached `Material decalMaterial`, both `HideFlags.HideAndDontSave`, destroyed on `AssemblyReloadEvents.beforeAssemblyReload`.
2. Build the decal material from an unlit transparent shader the editor always has (`Hidden/Internal-GUITextureClip` is wrong for world space — use `Unlit/Transparent`-class; if a project unlit shader exists prefer it). If the shader is null, `Debug.LogWarning` once and draw nothing (no fallback foot-gun — same discipline as P1).
3. `Draw(Vector3 hitPoint, Vector3 hitNormal, float radius, Texture2D? stampTex, Color tint, float alpha)`:
   - Orientation: `Quaternion rot = Quaternion.LookRotation(tangent, hitNormal)` where `tangent` is any vector orthogonal to `hitNormal` (e.g. `Vector3.Cross(hitNormal, Vector3.right)` with a near-parallel guard).
   - Build a `Matrix4x4.TRS(hitPoint + hitNormal * Y_OFFSET, rot, new Vector3(radius*2, radius*2, 1))` (Y_OFFSET small const to avoid z-fighting).
   - Texture: `stampTex` when non-null; otherwise bind a procedural soft-disc texture built once (radial smoothstep, `UPPER_SNAKE_CASE` size const) reused like `ScatterDensityOverlay.rampTexture`.
   - Tint = `tint`; material color alpha = `alpha` (= falloff). Set `_MainTex` + `_Color`, `SetPass(0)`, `Graphics.DrawMeshNow(quad, matrix)`.
4. Do NOT re-resolve `StampRef` here — the caller (P4) passes the already-resolved texture from `DensityPaintTool.ResolveStamp`. (SSOT: one resolver.)

**Verify / success criteria:**
- New file compiles; no allocations inside `Draw` (mesh/material/proc-texture all cached — confirm by inspection).
- Manual: decal tilts to follow a sloped collider's normal, scales with brush size, shows the stamp shape (or soft disc when None), red tint in Erase mode.
- Grep `ScatterBrushPreview.cs` for `Sprites/Default` → 0 hits; for `ResolveStamp` → 0 hits (no duplicate resolver).

**Risk Assessment**

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| `Quaternion.LookRotation` degenerate when tangent ∥ normal (flat-up surfaces) | 4 | 2 | 8 | Pick tangent via cross with the world axis least parallel to the normal; guard + fallback to `Vector3.forward`. Unit-cover the up-normal case conceptually in P6 math (decal-orient is visual, but the tangent helper is pure math). |
| Decal z-fights with ground at grazing camera angles | 3 | 1 | 3 | Y_OFFSET along normal + transparent material with ZWrite off. |
| No unlit shader available on a stripped editor | 1 | 2 | 2 | Warn once + draw nothing (no white-quad fallback). Editor always ships Unlit/Transparent in practice. |

**Timeline**

| Phase | Effort | Notes |
|---|---|---|
| Phase 2 | M | New decal renderer + procedural disc + orient math. Parallel-safe with P1/P5. |

---

## Phase 3 — GPU brush splat pipeline

**Objective:** Replace the CPU per-pixel write with GPU splatting into a linear RenderTexture, with mode-specific blends, continuous-stroke interpolation, gated per-tick readback to CPU pixels, and a final readback → PNG on stroke end.

**File ownership (exact paths):**
- New: `Assets/GrassInteract/Editor/Resources/DensityPaintBrush.shader` (sole owner)
- New: `Assets/GrassInteract/Editor/ScatterStudio/DensityPaintGPU.cs` (sole owner)
- Depends on: P5 readback helper in `DensityMapFactory.cs` (must land first).

**Steps:**

**Shader (`DensityPaintBrush.shader`, `Hidden/GrassInteract/DensityPaintBrush`):**
1. Single-pass blit-style shader that renders a brush-mask quad in UV space into the RT. Inputs: `_BrushTex` (stamp/proc mask), `_Strength`, `_Mode` (0=Paint,1=Erase,2=Smooth), `_Inner` (falloff inner radius). Linear write (`Blend`/no sRGB). One subshader, no fog, ZTest Always, Cull Off.
2. Paint = additive clamp01, Erase = subtractive clamp01, Smooth = sample current RT around the texel and lerp toward neighbor average by strength (separable-blur approximation; parity with the old CPU 3×3 average is NOT required — design-accepted, tune kernel).
3. Place under `Editor/Resources/` so it is reliably included; `Shader.Find("Hidden/GrassInteract/DensityPaintBrush")` resolves. If null → `Debug.LogError` once and disable GPU paint (tool falls back to a no-op + surfaced error; never silently swallow).

**`DensityPaintGPU.cs`:**
4. `internal sealed class DensityPaintGPU` holding: the active `RenderTexture rt`, cached `Material splatMaterial` + `Mesh unitQuad`, `Vector2 lastPaintUv`, and a `bool readbackInFlight` guard.
5. `BeginStroke(Texture2D densityMap)`:
   - Allocate `rt = new RenderTexture(w, h, 0, GraphicsFormat.R8_UNorm)` with `sRGB = false` (linear). If R8 RenderTexture is unsupported (`SystemInfo.IsFormatSupported`) → fall back to `R16_SFloat`/`RFloat`, `Debug.LogWarning` **once**.
   - `Graphics.Blit(densityMap, rt)` to seed from the current map.
   - Hand `rt` to `ScatterDensityOverlay` via the P1 seam so the overlay shows the live stroke.
6. `PaintAt(Vector2 centerUv, float radiusUv, float strength, Texture2D? brushTex, int mode, float inner)`:
   - Set material params; render the unit quad positioned at `centerUv` scaled to `radiusUv` into `rt` (push/pop `RenderTexture.active`, `GL.LoadOrtho` UV space). No readback here.
7. `StrokeTo(Vector2 fromUv, Vector2 toUv, float radiusUv, float spacingFactor, ...)`:
   - Interpolate stamps along `fromUv→toUv` at `spacing = radiusUv * spacingFactor` (gap-free fast drags). Always stamp at `toUv`. Update `lastPaintUv`.
8. `RequestLiveReadback(Action<Color[]> onDone)`:
   - `AsyncGPUReadback.Request(rt, 0, ...)`; on completion convert to `Color[]` (R channel → `(r,r,r,1)`), assign to `densityMap` via `SetPixels`+`Apply(false)`, then invoke `onDone` (which calls `ScatterRebuildScheduler.MarkDirty`). Guard `readbackInFlight` so only one is pending. **This is invoked from the 0.15s tick path only (P4), never per paint event.**
9. `EndStroke()`:
   - Final synchronous readback (`RenderTexture.active = rt; tex.ReadPixels(...)` or completed `AsyncGPUReadback`) → `Color[]` → `densityMap.SetPixels/Apply` → `DensityMapFactory.PersistPixels(...)` (unchanged R8 PNG contract).
   - Clear the overlay RT seam (rebind to `DensityMap`), release `rt` (`RenderTexture.ReleaseTemporary`/`Destroy`), reset `lastPaintUv`.
10. Use `DensityMapFactory`'s new readback helper (P5) for the RT→Color[] conversion in both step 8 and step 9 — one converter, no duplicate.

**Verify / success criteria:**
- Shader compiles (no console shader errors); `Shader.Find` resolves the hidden name.
- Painting writes the RT (visible via the live-bound overlay) without any per-event `GetPixels`/`SetPixels` CPU loop (grep `DensityPaintGPU.cs` for `GetPixels(` → 0; for per-event readback → only inside `RequestLiveReadback`/`EndStroke`).
- RT format is linear (sRGB off) — assert in code + log the chosen format once.
- Fast drag produces a continuous (gap-free) stroke.

**Risk Assessment**

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| **Live scatter reads CPU pixels** (`DensityPlacement.Build` line 69 `GetPixelBilinear`) → RT alone won't re-scatter | 5 | 4 | **20** | **MANDATORY:** readback RT→`densityMap` (CPU) gated to the 0.15s `ScatterRebuildScheduler` tick only, never per paint event. `MarkDirty` after each readback. Without this, live scatter shows stale density. Owns the gating contract in step 8. |
| RT not linear (sRGB write) → density values gamma-shifted, mismatched R8 semantics | 4 | 4 | 16 | Force `rt.sRGB = false` + linear `GraphicsFormat`; assert + log chosen format; golden test in P6 verifies center value. |
| R8 RenderTexture format unsupported on editor target | 3 | 3 | 9 | `SystemInfo.IsFormatSupported` check → `R16_SFloat`/`RFloat` fallback + log once. Readback helper handles both formats. |
| `AsyncGPUReadback` latency stacks if requested faster than it completes | 3 | 2 | 6 | `readbackInFlight` guard skips a new request while one is pending; tick is 0.15s so at most ~6-7/s. |
| Hidden shader stripped from a build that runs editor tests | 2 | 3 | 6 | Under `Editor/Resources/`; editor-only assembly; `Shader.Find` + log-once error path keeps tests from silently passing on a missing shader. |

**Timeline**

| Phase | Effort | Notes |
|---|---|---|
| Phase 3 | L | New shader + GPU pipeline + async readback + format fallback. On critical path; blocked by P5. |

---

## Phase 4 — Rewire DensityPaintTool

**Objective:** Replace the CPU `PaintAt` per-pixel loop with `DensityPaintGPU` splat + stroke interpolation; swap the wire-disc preview for `ScatterBrushPreview`; gate the live readback to the existing scheduler tick.

**File ownership (exact paths):**
- Modify: `Assets/GrassInteract/Editor/DensityPaintTool.cs` (sole owner this phase)
- Depends on: P2 (`ScatterBrushPreview`), P3 (`DensityPaintGPU`).

**Steps:**
1. Replace stroke state: drop `pixels`/`texW`/`texH` CPU buffers; hold a `DensityPaintGPU gpu` instance and `lastPaintUv`/`hasLastPaint`.
2. `BeginStroke`: `Undo.RegisterCompleteObjectUndo(map, "Paint Density")`; `this.activeStamp = ResolveStamp(field)` (keep the existing resolver — SSOT); `this.gpu.BeginStroke(map)`; set `lastPaintUv`.
3. `MouseDown`/`MouseDrag` handler (lines 85-95): compute `centerUv = new GrassFieldSpace(origin, bounds).WorldToUv(hit.point)` and `radiusUv` from brush size / bounds (reuse the exact ratio math at current lines 185-186 — `size / bounds.x`, `size / bounds.y`). Call `gpu.StrokeTo(lastPaintUv, centerUv, radiusUv, SPACING_FACTOR, strength, activeStamp, paintMode, inner)`. `strength = opacity * flow * stampAlpha`.
4. **Live readback hook (the gating contract):** register the per-tick readback against the scheduler. Add a hook so that when the 0.15s debounce flush for this (field, layerIdx) fires, `gpu.RequestLiveReadback(pixels => ScatterRebuildScheduler.MarkDirty(field, layerIdx))` runs first, then the scheduler rebuilds with fresh CPU pixels. Implement by calling `MarkDirty` after each `RequestLiveReadback`, NOT by readback-per-event. (Keep `ScatterRebuildScheduler` the only rebuild trigger.)
5. `EndStroke`: `gpu.EndStroke()` (final readback → `PersistPixels` inside the GPU class) → `ScatterRebuildScheduler.MarkDirty(field, layerIdx)` for the final state.
6. Preview swap: replace the `BrushDisc` + `FalloffRing` calls (lines 78-79) with `ScatterBrushPreview.Draw(hit.point, hit.normal, size, ResolveStamp-cached-tex, BrushColorForMode(), falloff)`. Keep `ScatterGizmos.BrushDisc/FalloffRing` defined (used by the instance tool — out of scope).
7. Delete the dead CPU `PaintAt` body and `NeighborAverage` (now in the shader). Remove now-unused `using`/fields your change orphaned.
8. Add `SPACING_FACTOR` as a `UPPER_SNAKE_CASE` const (terrain-like ~0.25).

**Verify / success criteria:**
- `DensityPaintTool.cs` no longer contains a per-pixel `for` loop over the brush footprint (grep for `GetPixelBilinear` / `SetPixels` in this file → 0 hits except via `DensityPaintGPU`/factory).
- Live re-scatter still updates while painting, at tick cadence (not per event) — confirm grass density changes ~6-7×/s during a held stroke, not every mouse-move.
- Stroke end persists to PNG (reopen scene / domain reload → painted density survives).
- Undo restores pre-stroke density.
- Compiles clean; `ResolveStamp` still the only stamp resolver.

**Risk Assessment**

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| Readback accidentally wired per paint event (perf cliff + stale-vs-live confusion) | 4 | 4 | 16 | Hard rule: `RequestLiveReadback` invoked only from the MarkDirty/tick path; code-review grep that `MouseDrag` does NOT call readback. Mirror the scheduler's debounce contract. |
| Undo no longer captures GPU-only state (RT changes not in undo stack) | 3 | 3 | 9 | `RegisterCompleteObjectUndo(map)` at BeginStroke snapshots the CPU `Texture2D`; EndStroke writes CPU pixels back, so undo restores the pre-stroke map. Verify undo after a stroke. |
| `radiusUv` anisotropy (non-square field bounds) makes the brush elliptical in UV but circular in world | 3 | 2 | 6 | Keep separate `radUvX`/`radUvY` like the current CPU code (lines 185-186); the shader mask samples in normalized brush space, world-circularity preserved. |
| Orphaned fields/usings after deleting CPU kernel cause warnings-as-errors | 2 | 2 | 4 | Remove `pixels`/`texW`/`texH`/`NeighborAverage` and their usings in the same edit; compile-verify. |

**Timeline**

| Phase | Effort | Notes |
|---|---|---|
| Phase 4 | M | Rewire stroke lifecycle + preview swap + delete CPU kernel. On critical path; blocked by P2 + P3. |

---

## Phase 5 — Readback helper in DensityMapFactory

**Objective:** Add one `RenderTexture → Color[]` readback helper, reused by both the 0.15s tick readback and `EndStroke`. No duplicate conversion logic.

**File ownership (exact paths):**
- Modify: `Assets/GrassInteract/Editor/DensityMapFactory.cs` (sole owner this phase)

**Steps:**
1. Add `internal static Color[] ReadbackToPixels(RenderTexture rt)`:
   - Push `RenderTexture.active = rt`; create a transient `Texture2D` matching `rt` size in a CPU-readable format (`RGBA32` is fine for the temp); `ReadPixels(new Rect(0,0,w,h), 0, 0)`; `Apply(false)`; pop `RenderTexture.active`.
   - Map each texel's R channel into a `Color(r,r,r,1)` array (R8 density semantics — matches `PersistPixels`' RGBA32-from-R encoding at lines 150-155).
   - Destroy the temp texture; return the array. Handle both R8 and RFloat source formats (read `.r` either way).
2. (Optional, only if AsyncGPUReadback path needs it) add `internal static Color[] FromRequest(AsyncGPUReadbackRequest req, int w, int h)` that maps the `NativeArray<float>`/`NativeArray<byte>` to `Color[]` — single converter for the async case so `DensityPaintGPU` does not inline it.
3. Keep `PersistPixels` and `CreateBlank` unchanged (SSOT for create/persist).

**Verify / success criteria:**
- New helper compiles; no behavioral change to `CreateBlank`/`PersistPixels` (grep diff is additive-only).
- Round-trip identity: a known RT (all-0.5 R) → helper → `Color[]` where every `.r ≈ 0.5` (the P6 golden uses this).

**Risk Assessment**

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| Temp-texture format mismatch loses precision (R8 quantization) | 2 | 2 | 4 | Acceptable — density is R8 by contract; document the quantization in the helper summary. |
| `ReadPixels` reads from the wrong active RT if not pushed/popped correctly | 2 | 3 | 6 | Strict push/pop of `RenderTexture.active` in a try/finally; never leave a dangling active RT. |

**Timeline**

| Phase | Effort | Notes |
|---|---|---|
| Phase 5 | S | One additive helper. Parallel-safe with P1/P2; unblocks P3. Do this first. |

---

## Phase 6 — EditMode tests

**Objective:** Lock the pure-math seams (UV mapping, stroke-interpolation spacing) and an RT-readback golden so regressions are caught without a GPU-visual eye.

**File ownership (exact paths):**
- New: `Assets/GrassInteract/Tests/Editor/DensityBrushMathTests.cs` (or the project's existing EditMode test folder — match it; create under the Editor test asmdef)
- Depends on: P3 (GPU class for the golden), P5 (readback helper).

**Steps:**
1. **UV mapping test:** `GrassFieldSpace.WorldToUv`/`UvToWorld` round-trip — known origin+bounds, assert center world ↔ (0.5,0.5) UV and corners ↔ (0,0)/(1,1). (Reuses the SSOT struct; guards against drift.)
2. **Stroke-interpolation spacing test:** pure function that, given `fromUv`, `toUv`, `radiusUv`, `spacingFactor`, returns the stamp positions. Assert: count = `ceil(dist / (radiusUv*spacingFactor))`, endpoints included, even spacing within tolerance, gap-free (max gap ≤ spacing). Extract the interpolation math into a `static` testable method on `DensityPaintGPU` so the test does not need a GPU.
3. **RT-readback golden:** allocate a small linear R8 RT, blit/clear to a known value, splat one centered stamp at full strength via the paint material, `DensityMapFactory.ReadbackToPixels`, assert the center texel's R ≈ expected (full) and a corner ≈ seed value. (Validates linear/sRGB correctness + the converter.) If the harness cannot create a GPU RT in batchmode, gate this test with the project's existing GPU-availability guard pattern and assert the math-only paths unconditionally.
4. Tangent helper (from P2) up-normal case: assert `Quaternion.LookRotation(tangent, Vector3.up)` does not throw / produces a valid basis for a straight-up normal.

**Verify / success criteria:**
- All new EditMode tests pass (`run_tests` EditMode, zero failures). Per development-principles Test-Pass Gate: zero failures before "done".
- Tests are independent and descriptively named.

**Risk Assessment**

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|---|---|---|---|
| GPU RT golden can't run in headless CI/batchmode | 3 | 2 | 6 | Gate the GPU golden behind `SystemInfo.graphicsDeviceType != Null`; keep UV + spacing math tests unconditional so the core contract is always covered. |
| Stroke-interp math not extractable without GPU coupling | 2 | 3 | 6 | Design `StrokeTo` to delegate to a `static` pure `ComputeStampPositions(...)` — the test targets that, not the GPU render. |

**Timeline**

| Phase | Effort | Notes |
|---|---|---|
| Phase 6 | M | 3-4 EditMode tests, one GPU-gated. Last on critical path. |

---

## Consolidated risk register (scores ≥ 15 — mitigation mandated before that phase starts)

| Risk | Phase | Score | Mitigation (gate) |
|------|-------|-------|-------------------|
| Live scatter reads CPU pixels (`GetPixelBilinear`) → RT alone is stale | P3 | **20** | Per-tick readback to `densityMap`, gated to the 0.15s `ScatterRebuildScheduler` tick ONLY. Never per paint event. This is the load-bearing contract of the whole redesign. |
| RT not linear (sRGB write) → gamma-shifted density | P3 | 16 | Force linear RT (`sRGB=false`); assert + log format; P6 golden verifies center value. |
| Readback wired per paint event (perf cliff) | P4 | 16 | Readback only from MarkDirty/tick path; review-grep `MouseDrag` for readback calls. |

All three high-risk items concentrate in P3/P4 (the GPU + rewire critical path) — implement P5 + P1 + P2 first to de-risk, then attack P3 with the readback-gating contract front-of-mind.

---

## Consolidated timeline

| Phase | Effort | Depends on | Notes |
|-------|--------|-----------|-------|
| Phase 5 — readback helper | S | — | Do first; unblocks P3. Parallel-safe. |
| Phase 1 — kill white plane | S | — | Parallel-safe. |
| Phase 2 — brush decal | M | — | Parallel-safe. |
| Phase 3 — GPU splat pipeline | L | P5 | Critical path. Owns the 3 high-risk items. |
| Phase 4 — rewire tool | M | P2, P3 | Critical path. |
| Phase 6 — EditMode tests | M | P3, P5 | Critical path tail. Zero-failure gate. |
| **Total** | **~2 S + 2 M + 1 L (+1 M tests)** | — | **Critical path: P5 → P3 → P4 → P6.** Recommended batch order: {P5, P1, P2} → P3 → P4 → P6. |

---

## Backwards compatibility

- **Additive for assets:** R8 PNG persistence contract (`DensityMapFactory.PersistPixels` / `CreateBlank` / importer settings) is unchanged. Existing density maps load and paint identically.
- **Breaking for Smooth mode:** GPU blur ≠ old CPU 3×3 neighbor-average — design-accepted, not a regression to fix.
- **Heatmap behavior change (intended):** overlay no longer renders a white fallback; it draws nothing unless the dedicated shader exists AND `OverlayVisible` is on. Flagged as the explicit goal of P1.
- **Rollback:** each phase is revertible in isolation — P1/P2/P5 are additive-or-deletion only; P3 is new files (delete to revert); P4 is the only phase that rewrites existing tool logic, so revert P4 alone restores the CPU painter (P3 files become dead but harmless). No cascading damage.

---

/t1k:cook plans/density-brush-terrain-style-plan.md
