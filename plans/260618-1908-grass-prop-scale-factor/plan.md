# Plan: Per-Layer Render-Time Scale Factor (no repaint)

**Date:** 2026-06-18 19:08
**Approved design:** `plans/reports/2026-06-18-grass-prop-scale-factor-brainstorm.md`
**Cook handoff:** `/t1k:cook plans/260618-1908-grass-prop-scale-factor`

Add a per-layer uniform `scaleFactor` (`[Range(0.1f, 5f)]`, default `1f`) to `GrassLayer` & `PropLayer`
that scales scattered grass/props at RENDER time — instant in the editor, NO re-scatter. Prop colliders
rescale too; grass has none.

## Mechanism (verified by direct read)
GPU buffers store NORMALIZED [0,1] scale. Vertex shaders decode
`worldScale = (packed/65535) * _ScaleMax2` where `_ScaleMax2` is a **per-layer, per-material** uniform
re-applied EVERY frame in `Submit` via `SetLodFloat`. A SEPARATE `_ScaleFactor` uniform multiplied AFTER
decode → instant scaling, no re-quantize, no rebuild:
`worldScale = (packed/65535) * _ScaleMax2 * _ScaleFactor` (`_ScaleFactor` default `1.0`).

Two CPU fields gate frustum behavior and BOTH are consumed per-frame, so `SetScaleFactor` only mutates
them in place and the per-frame paths pick them up:
- `MakeRenderParams` rebuilds `drawBounds` from `this.worldBounds` every Submit (GrassGpuEngine L722,
  InstancedPropEngine L956).
- The cull compute reads `bladeCullMargin` every Submit (`SetComputeFloatParam ... "bladeCullMargin"`,
  GrassGpuEngine L925; prop engine records it per-frame in `RecordFrameCommands`).

### ⚠ Correction to the brief — shader site count is HIGHER than "4 total"
Both shaders have 3 passes (UniversalForward / ShadowCaster / DepthOnly), each with its own `_ScaleMax2`
CBUFFER declaration AND decode. Verified exact sites:

| Shader | `_ScaleMax2` declarations (add `_ScaleFactor` beside each) | decode `* _ScaleMax2` sites (append `* _ScaleFactor`) |
|---|---|---|
| `GrassInteractIndirect.shader` | L184, L665, L903 | L301, L731, L938 |
| `ScatterInstanced.shader` | L189, L627, L771 | L300, **L390**, L656, L793 |

`ScatterInstanced` UniversalForward has TWO decode sites (L300 + L390) under one declaration (L189).
Total: **6 declarations + 7 decode appends = 13 shader edits** (brief said 4). Implementer MUST re-grep
`* *_ScaleMax2` and `_ScaleMax2 *;` in each shader before editing — do not trust these line numbers blind
(they drift with every prior edit). The grep commands are in Phase 2 success criteria.

---

## Default decisions (subagent could not run AskUserQuestion — orchestrator: confirm or override)
The brief left three points unresolved. Lowest-risk KISS/SSOT defaults chosen; flagged for confirmation:

1. **CPU grass engine (`GrassCpuEngine`) — DEFAULT: no-op + one-time warn.** The CPU fallback bakes scale
   into CPU mesh slabs (no render-time `_ScaleFactor` uniform), so it cannot scale instantly. `SetScaleFactor`
   logs a one-time `Debug.LogWarning` ("scaleFactor is a GPU render feature; CPU fallback ignores it") and
   returns. The serialized field still exists for a future baked-in CPU path. Rationale: CPU path is a rare
   capability fallback; folding the factor into a CPU re-bake would BE a repaint (the rejected approach B).
2. **Persistence across rebuilds — DEFAULT: serialized field is SSOT (no separate live snapshot).** The
   editor slider writes the serialized `scaleFactor` field immediately (`ApplyModifiedProperties`) AND calls
   the live `SetScaleFactor`. When any rebuild-triggering edit (scaleRange, density) recreates the engine,
   `Build()` reads `layer.ScaleFactor` and applies it, so the factor naturally survives. No dual state.
3. **Prop collider rescale timing — DEFAULT: on slider drag-release (debounced).** Render scale updates
   live every tick; collider local scales update once on `EndChangeCheck` with `hotControl == 0` /
   MouseUp — mirrors the brief's note and the existing `WorldPainterPropTransformEdit` deferred-rebuild
   pattern. Avoids per-tick transform churn across all pooled colliders.

---

## Phase 1 — Data fields + adapters | Effort: S
**Owns:** `Runtime/Surface/GrassLayer.cs`, `Runtime/Surface/PropLayer.cs`,
`Runtime/Surface/GrassTileScatterLayer.cs`, `Runtime/Surface/PropLayerScatterLayer.cs`,
`Runtime/Scatter/ScatterLayer.cs`.

1. `GrassLayer.cs` — add after `scaleRange` (~L43):
   `[Tooltip("Render-time uniform scale multiplier — resizes the whole layer's scatter live, no re-scatter.")]`
   `[SerializeField, Range(0.1f, 5f)] private float scaleFactor = 1f;`
   Accessor near `ScaleRange` (~L90): `public float ScaleFactor => this.scaleFactor;`
2. `PropLayer.cs` — add the same field + accessor near the scale-override block (~L67-71 / ~L133).
3. `ScatterLayer.cs` — add a virtual/abstract `public virtual float ScaleFactor => 1f;` so engines read it
   uniformly through the adapter (mirrors `ScaleRange`). (Verify `ScatterLayer` declares `ScaleRange` as
   abstract/virtual and match that modifier — locate before editing.)
4. `GrassTileScatterLayer.cs` — override (~L56, beside `ScaleRange`):
   `public override float ScaleFactor => this.layer.ScaleFactor;`
5. `PropLayerScatterLayer.cs` — override (~L58, beside `ScaleRange`):
   `public override float ScaleFactor => this.layer.ScaleFactor;`

**Conventions:** camelCase field, `this.` prefix, `[SerializeField] private`, no underscore prefix.

**Success criteria:**
- `grep -n "ScaleFactor" Runtime/Surface/{GrassLayer,PropLayer,GrassTileScatterLayer,PropLayerScatterLayer}.cs`
  shows field + accessor/override in all four.
- Compiles clean (see Phase 6 compile signal). Default `1f` ⇒ no behavior change yet.

**Rollback:** delete the field + accessor lines; additive only, no migration.

## Phase 2 — Shaders | Effort: S
**Owns:** `Shaders/GrassInteractIndirect.shader`, `Shaders/ScatterInstanced.shader`.

1. For EACH `_ScaleMax2` declaration (table above), add an adjacent `float _ScaleFactor;` in the SAME
   CBUFFER/declaration block (one per pass — all 6 blocks). Missing a pass = that pass renders at the old
   size (shadow/depth desync → wrong self-shadowing & depth-test silhouette).
2. For EACH decode site, append `* _ScaleFactor` to the scale expression:
   - `... / 65535.0 * _ScaleMax2` → `... / 65535.0 * _ScaleMax2 * _ScaleFactor`
   - In the compact lines (`sxz=...*_ScaleMax2, sy2=sxz`), multiply `sxz` only — `sy2=sxz` inherits.
3. Update the `ScatterInstanced.shader` header comment (L23, L42) noting `_ScaleFactor` exists (default 1).

**Success criteria (run after edits, MUST match):**
- `grep -c "_ScaleFactor;" Shaders/GrassInteractIndirect.shader` == `3`;
  `grep -c "* _ScaleFactor" Shaders/GrassInteractIndirect.shader` == `3`.
- `grep -c "_ScaleFactor;" Shaders/ScatterInstanced.shader` == `3`;
  `grep -c "_ScaleFactor" Shaders/ScatterInstanced.shader` (decode + decl) == `7`.
- No shader-compile errors in `read_console` after `refresh_unity(force, all)`.

**Rollback:** revert both shader files; uniform unbound defaults to 0 in HLSL — so DO NOT ship the shader
edit without Phase 3 binding it (a declared-but-unbound `_ScaleFactor` reads 0 → everything vanishes).
Sequence Phase 2 and Phase 3 to land together (or default `_ScaleFactor` via `[PerRendererData]`? — no;
bind it). See Risk R1.

## Phase 3 — Engine uniform push + SetScaleFactor + bounds/margin | Effort: M
**Owns:** `Runtime/Scatter/IGrassEngine.cs`, `Runtime/Scatter/GrassGpuEngine.cs`,
`Runtime/Scatter/InstancedPropEngine.cs`, `Runtime/Scatter/GrassCpuEngine.cs`,
`Runtime/WorldPainter.Scatter.cs` (`ScatterPreBuiltEngineWrapper`).

1. `IGrassEngine.cs` — add `void SetScaleFactor(float factor);` (all 3 implementers + the wrapper must
   implement it — pre-delete/add reference check: 4 implementers confirmed).
2. **GrassGpuEngine.cs**:
   - Add `private static readonly int ID_ScaleFactor = Shader.PropertyToID("_ScaleFactor");` beside
     `ID_ScaleMax2` (L37).
   - Add fields: `private float scaleFactor = 1f;` and snapshot the BASE margin/bounds at Build so repeated
     `SetScaleFactor` calls don't compound: `private float baseBladeCullMargin;` `private Bounds baseWorldBounds;`.
   - In `Build` (after L262 margin compute, after L214 worldBounds): set
     `this.baseBladeCullMargin = this.bladeCullMargin;` `this.baseWorldBounds = this.worldBounds;`
     then read `this.scaleFactor = Mathf.Clamp(layer.ScaleFactor, 0.1f, 5f);` and call the SAME apply body
     used by `SetScaleFactor` (so a rebuild re-applies the serialized factor — Default decision #2).
   - In `Build` per-material uniform block (beside L373 `SetLodFloat(ID_ScaleMax2, ...)`):
     `this.SetLodFloat(ID_ScaleFactor, this.scaleFactor);` (PER-MATERIAL — never `SetGlobal`; mirror the
     `_ScaleMax2` per-material comment, invariant `grass-per-tile-gpu-buffer-binding`).
   - In `Submit` per-frame re-apply (beside L511 `SetLodFloat(ID_ScaleMax2, ...)`):
     `this.SetLodFloat(ID_ScaleFactor, this.scaleFactor);`.
   - Add method:
     ```
     public void SetScaleFactor(float factor)
     {
         this.scaleFactor = Mathf.Clamp(factor, 0.1f, 5f);
         this.SetLodFloat(ID_ScaleFactor, this.scaleFactor);                 // re-push uniform this frame
         this.bladeCullMargin = this.baseBladeCullMargin * this.scaleFactor; // margin in lockstep
         this.worldBounds = ScaleBoundsExtents(this.baseWorldBounds, this.scaleFactor); // bounds in lockstep
     }
     ```
     `ScaleBoundsExtents` multiplies extents about the CENTER (keep center, scale size) — PAINTING space;
     `MakeRenderParams` already maps painting→world per-frame, so DO NOT pre-map here (invariant
     `scatter-renderparams-worldbounds-space`).
3. **InstancedPropEngine.cs**: same pattern —
   - `ID_ScaleFactor` beside `ID_ScaleMax2` (L28); `scaleFactor`, `baseBladeCullMargin`, `baseWorldBounds`
     fields; snapshot base after L188 (worldBounds) and L240-241 (margin); read+apply `layer.ScaleFactor`
     in Build; per-material push beside L321 (Build) and L419 (Submit).
   - `SetScaleFactor` mirrors GrassGpuEngine PLUS triggers the deferred collider rescale (Phase 4).
   - NOTE the prop margin also includes `tiltSweep` (L237-241); multiply the WHOLE base margin by factor
     (tilt headroom scales with the prop too) — snapshot `baseBladeCullMargin` AFTER the tiltSweep term so
     the lockstep is structural.
4. **GrassCpuEngine.cs**: `SetScaleFactor` = one-time warn + return (Default decision #1). Use a
   `private bool warnedScaleFactor;` guard so it logs once per engine.
5. **WorldPainter.Scatter.cs** `ScatterPreBuiltEngineWrapper`: forward `SetScaleFactor(f) => this.inner.SetScaleFactor(f);`.

**Success criteria:**
- Compiles clean; all 4 implementers define `SetScaleFactor`.
- Manual: in a built GPU scene, calling `SetScaleFactor(2f)` on a layer's engines doubles rendered size with
  NO rebuild, NO console error, and no frustum-edge pop when panning the camera (margin+bounds verified by
  Phase 6 math test).

**Rollback:** revert the 5 files; interface method removal requires reverting all implementers together.

## Phase 4 — Prop collider rescale | Effort: S
**Owns:** `Runtime/Scatter/InstancedPropEngine.cs` (collider path only — coordinate with Phase 3's edits
to the same file; SEQUENCE Phase 4 after Phase 3, same owner, no concurrent edit).

1. `BuildColliderRuntime` (L531) currently sets `scales[i] = rec.scale * rec.colliderScale;` (L598).
   Multiply by the live factor: `scales[i] = rec.scale * rec.colliderScale * this.scaleFactor;` so a layer
   built while factor != 1 gets correctly-sized colliders immediately.
2. `SetScaleFactor` collider path (debounced — Default decision #3): provide
   `public void ApplyColliderScale()` on the engine that walks the live `InstanceColliderPool` hosts and
   sets each host's local scale to `baseScale_i * this.scaleFactor`. The EDITOR (Phase 5) calls this once on
   slider drag-release, NOT every tick. Verify `InstanceColliderPool` exposes per-host base scale or the
   authored records to recompute from (locate; if not, add a minimal accessor — owns
   `Runtime/Scatter/InstanceColliderPool.cs` if needed, list it explicitly).
3. Colliders only exist in Play mode (`BuildColliderRuntime` early-returns when `!Application.isPlaying`,
   L534) — the editor live-slider collider rescale is therefore a NO-OP in edit mode (document this; grass
   has no colliders and props are edited in edit mode, so the live collider rescale matters only if the user
   has entered Play mode with the inspector open). Confirm with orchestrator whether edit-mode prop colliders
   are ever generated; if never, Phase 4 step 2 may reduce to step 1 only (build-time multiply). See Risk R4.

**Success criteria:**
- A prop layer with `generateColliders` true, built at `scaleFactor = 2`, produces colliders at 2× local
  scale (Phase 6 unit test asserts `rec.scale * colliderScale * factor`).
- No per-tick collider churn during a slider drag (manual: profiler shows collider transform writes only on
  release).

**Rollback:** revert the collider-scale multiply; build-time multiply is one line.

## Phase 5 — Editor live slider | Effort: M
**Owns:** `Editor/Inspector/GrassLayerEditor.cs`, `Editor/Inspector/PropLayerEditor.cs`,
`Runtime/WorldPainter.SurfaceLayers.cs` (new live-engine resolver).

1. `WorldPainter.SurfaceLayers.cs` — add resolvers mirroring `TryGetPropEngine` (L236):
   - `internal void SetGrassLayerScaleFactor(GrassLayer layer, float f)` — iterate `surfaceAdapters`
     (one adapter PER TILE), and for each whose `SourceLayer == layer`, call
     `(this.surfaceEngines[i]).SetScaleFactor(f)`. (Grass has MULTIPLE engines per layer — one per tile;
     this is the key difference from props.)
   - `internal void SetPropLayerScaleFactor(PropLayer layer, float f)` — find the single matching prop
     engine (reuse `TryGetPropEngine`) and call `SetScaleFactor(f)`.
   Both are `internal` (Editor asmdef references Runtime). If an `IGrassEngine` `surfaceEngines[i]` is a
   `ScatterPreBuiltEngineWrapper` or `GrassCpuEngine`, the call still type-checks (interface method).
2. `GrassLayerEditor.cs`:
   - Add `SerializedProperty? propScaleFactor;` + `FindProperty("scaleFactor")` in `OnEnable`.
   - In `DrawPlacement` (after `propScaleRange` PropertyField, L141), draw `propScaleFactor` in its OWN
     change-check block so its edits route to the LIVE path, NOT the rebuild scheduler:
     ```
     EditorGUI.BeginChangeCheck();
     EditorGUILayout.PropertyField(this.propScaleFactor!);
     if (EditorGUI.EndChangeCheck())
     {
         this.serializedObject.ApplyModifiedProperties();              // SSOT write (Default #2)
         float f = this.propScaleFactor!.floatValue;
         foreach painter referencing this layer: painter.SetGrassLayerScaleFactor(grass, f);  // live, no repaint
         // explicitly DO NOT call WorldPainterRebuildScheduler.MarkGrassDirty
     }
     ```
     CRITICAL: the outer `EndChangeCheck` (L75) currently routes ALL changes to `MarkGrassDirty`. The
     scaleFactor field must be EXCLUDED from that path — handle it in its own inner block BEFORE the outer
     check, or guard the outer `MarkGrassDirty` so a scaleFactor-only change does not trigger a rebuild.
     Locate the painter-enumeration helper (mirror `RebuildOnAllPainters` which finds painters referencing
     the layer's map) and reuse it to reach each painter for the live call.
3. `PropLayerEditor.cs`: same — add slider in `DrawPlacement` (after the scale-override block, ~L193),
   own change-check → `SetPropLayerScaleFactor`, exclude from the outer `MarkPropDirty` (L116-117 / L125).
   On the slider's drag-RELEASE (`Event.current.type == MouseUp` or `hotControl == 0`), additionally call
   the engine's `ApplyColliderScale()` (Phase 4) once.

**Success criteria:**
- Dragging the grass/prop `scaleFactor` slider in the WorldPainter card resizes the scatter in the Scene
  view IMMEDIATELY with NO rebuild flicker and NO full re-scatter (verify: no `MarkGrassDirty`/`MarkPropDirty`
  fires for a scaleFactor-only change — add a temporary `Debug.Log` in the scheduler during manual test).
- Editing `scaleRange` STILL rebuilds (unchanged), and the live scaleFactor survives that rebuild (Default #2).

**Rollback:** revert the 3 files; the field still serializes and applies at Build time even without the
editor slider (graceful — just no live preview).

## Phase 6 — EditMode tests + verification | Effort: M
**Owns:** `Tests/Editor/ScaleFactorTests.cs` (NEW).

Tests (NUnit EditMode, `WorldPainter.Tests` asmdef — pattern: `ChunkedInstanceBufferTests.cs`,
`ScatterLodCullTests.cs`):
1. **Clamp:** `GrassLayer`/`PropLayer` `scaleFactor` set below 0.1 / above 5 via SerializedObject clamps to
   [0.1, 5] (Range attribute) — or assert `SetScaleFactor(10f)` stores `5f` and `SetScaleFactor(0f)` stores
   `0.1f` (the engine `Mathf.Clamp`).
2. **Margin lockstep:** after `SetScaleFactor(2f)`, `bladeCullMargin == baseBladeCullMargin * 2`
   (expose base/margin via `internal` test accessor or assert through a pure helper — prefer extracting the
   margin+bounds math into a static `ScaleFactorMath` helper class so it is testable WITHOUT a built GPU
   engine, since `execute_code` is unusable here and a GPU engine needs a live device).
3. **Bounds lockstep:** `ScaleBoundsExtents(b, 2f).extents == b.extents * 2` and `.center == b.center`.
4. **Default no-op (regression guard):** `SetScaleFactor(1f)` leaves margin == base, bounds == base, and the
   pushed uniform == 1 (decode unchanged) — proves the feature is inert at default.
5. **Collider scale:** the collider-scale formula yields `rec.scale * rec.colliderScale * factor` (pure
   function test; do not require Play mode).

**Verification protocol (project-specific gotchas — MANDATORY):**
- `execute_code` is UNUSABLE in this env — verify ONLY via EditMode test + `run_tests`.
- `run_tests` discovery can silently DROP the `WorldPainter.Tests` assembly (reports total:0). Do NOT trust
  a green `run_tests` alone — the compile signal is **fresh clean `Library/ScriptAssemblies/*.dll` mtime +
  0 errors in `read_console`**.
- New `.cs` file (`ScaleFactorTests.cs`) needs `refresh_unity(force, all)` — `scope=scripts` will NOT import
  a brand-new file (phantom CS0246 on a sibling type otherwise).
- Shader edits need `refresh_unity(force, all)` then `read_console` for shader-compile errors.

**Success criteria:** all 5 tests pass (or, if discovery drops the assembly, DLL mtime fresh + 0 console
errors + the test file compiled into `WorldPainter.Tests.dll`); zero new console errors after a forced
refresh.

---

## Feasibility
- **Reuse check:** REUSE — per-material `_ScaleMax2`/`SetLodFloat` discipline, per-frame `Submit` re-apply,
  live-engine resolver (`TryGetPropEngine` + `WorldPainterPropTransformEdit.ResolvePropEngine`), deferred-on-
  release pattern. NEW — one `_ScaleFactor` uniform, one `SetScaleFactor` per engine, one editor slider per
  layer type, two resolver methods, one math helper, one test file.
- **Complexity:** moderate — multi-file with GPU-correctness invariants; each individual change is small.

## Dependencies
- **Blocked by → blocks:** 1 → {2,3,5}; 2 → 3 (uniform must exist before binding); 3 → {4,5} (`SetScaleFactor`
  is the live entry point); 4 sequences AFTER 3 (same file); 6 verifies all.
- **Parallel-safe:** none truly concurrent (1 gates all; 3+4 share `InstancedPropEngine.cs`). Sequence
  1 → 2 → 3 → 4 → 5 → 6. Per AI-velocity, batch-implement 1-5 blind, then ONE forced refresh + run_tests.
- **Critical path:** 1 → 2 → 3 → 4 → 5 → 6 (entire chain).
- **File-ownership conflict:** `InstancedPropEngine.cs` touched by Phase 3 AND Phase 4 — single owner,
  sequenced, never concurrent.

## Risk Assessment
| Risk | Likelihood | Impact | Score | Mitigation |
|------|-----------|--------|-------|------------|
| R1 — `_ScaleFactor` declared in shader (Phase 2) but not bound (Phase 3) → reads 0 → ALL scatter vanishes | 4 | 5 | **20** | HIGH. Land Phase 2 + Phase 3 in the SAME batch; never refresh Unity with Phase 2 alone. Phase 3 binds per-material in BOTH Build and Submit. Verify a default-1 layer still renders before merge. |
| R2 — missed a shader decode/declaration site (3 passes, 13 edits, brief said 4) → shadow/depth size desync | 4 | 3 | **12** | Grep-count gate in Phase 2 success criteria (exact expected counts). Re-grep before AND after edits. |
| R3 — `SetScaleFactor` compounds margin/bounds on repeated calls (multiply-in-place) → runaway cull bounds | 3 | 3 | 9 | Snapshot `baseBladeCullMargin`/`baseWorldBounds` at Build; always compute from BASE × factor, never from current. Phase 6 test #4 (default no-op) + #2/#3 (lockstep) guard this. |
| R4 — collider rescale path wrong because prop colliders only exist in Play mode | 3 | 2 | 6 | Build-time multiply (Phase 4 step 1) is the SSOT; live `ApplyColliderScale` is a Play-mode-only refinement. Confirm edit-mode collider policy with orchestrator (Phase 4 step 3). |
| R5 — editor scaleFactor change leaks into the rebuild scheduler → becomes a repaint (defeats the feature) | 3 | 4 | 12 | Own change-check block per slider; EXCLUDE scaleFactor from the outer `MarkGrassDirty`/`MarkPropDirty`. Manual test: temporary Debug.Log in scheduler confirms zero rebuild on scaleFactor-only edit. |
| R6 — `worldBounds` mapped to world twice (pre-map in SetScaleFactor + MakeRenderParams per-frame) → wrong cull | 2 | 4 | 8 | Keep `worldBounds` in PAINTING space; `MakeRenderParams` does the per-frame painting→world map. `ScaleBoundsExtents` only scales extents about center, no space change. |
| R7 — `run_tests` silently drops `WorldPainter.Tests` (total:0) → false green | 4 | 3 | 12 | Compile signal = fresh DLL mtime + 0 console errors, NOT run_tests pass alone (project memory). Forced refresh for the new test file. |
| R8 — interface change to `IGrassEngine` misses an implementer → CS0535 | 2 | 2 | 4 | 4 implementers enumerated (GpuEngine, CpuEngine, InstancedPropEngine, ScatterPreBuiltEngineWrapper); add to all in Phase 3. |

**High-risk (≥15): R1** — mandatory mitigation BEFORE Phase 2 ships: Phase 2 and Phase 3 land together; a
default-1 layer MUST still render after the forced refresh, verified before proceeding.

## Timeline
| Phase | Effort | Notes |
|-------|--------|-------|
| 1 — Data fields + adapters | S | No deps; additive. |
| 2 — Shaders | S | 13 edits across 6 blocks; blocks 3. Must ship WITH 3 (R1). |
| 3 — Engine push + SetScaleFactor + bounds | M | Core. 5 files incl. interface. Snapshot base (R3). |
| 4 — Prop collider rescale | S | After 3 (same file). Build-time multiply + Play-mode refinement (R4). |
| 5 — Editor live slider | M | Resolver + 2 editors; exclude from rebuild scheduler (R5). |
| 6 — Tests + verification | M | New test file → forced refresh. Trust DLL mtime, not run_tests (R7). |
| **Total** | **M (≈ 1 focused session)** | Critical path: 1→2→3→4→5→6 (no parallelism — 1 gates all, 3+4 share a file). |

## Cook handoff
`/t1k:cook plans/260618-1908-grass-prop-scale-factor`
