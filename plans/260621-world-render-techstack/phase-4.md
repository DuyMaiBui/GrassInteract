# Phase 4 — Props Render Re-Tier + Grass Integration Into the Segment Window

**Effort:** M · **Blocks:** 6 · **Blocked by:** 1 (props), 3 (grass scope)

## Goal

Two sub-goals, both rendering-only:
- **Props:** finalize the non-compute prop render path so props draw on the GLES3.0 floor (Phase 1 gated the blank no-op; this phase ships the real fallback) and scope props to the active segment.
- **Grass:** integrate the KEEP-AS-IS grass renderer into the segment lifecycle — scope blades to the active streamed window and re-bind the interactor across segment/relaunch churn. NO grass re-architecture.

**Provisional default (confirm decision #3):** props on Low = **standard URP MeshRenderers per segment** (SRP-batched, guaranteed ES3.0, baked-GI). High/Mid may keep the GPU-indirect path behind `DeviceTierProbe`. Alt: `Graphics.RenderMeshInstanced` (compute-free) for dense repeated props.

## File Ownership (real paths)

Props — edit:
- `Assets/WorldPainter/Runtime/Scatter/InstancedPropEngine.cs` — non-GPU tier path: per the chosen default, either bake props as standard MeshRenderers (then this engine is High/Mid-only) or add a `Graphics.RenderMeshInstanced` branch (no compute). RenderMeshIndirect (508/510/512) stays gated behind `DeviceTierProbe.TryGpu` (wired in Phase 1).
- `Assets/WorldPainter/Runtime/Scatter/InstanceColliderPool.cs` / `InstanceBatchPool.cs` — ensure prop colliders/batches are scoped per segment and pooled (IObjectPoolManager) rather than global.
- `Assets/WorldPainter/Shaders/ScatterInstanced.shader` — confirm ES3.0-safe variant (target lowered in Phase 1); the standard-MeshRenderer path must use an ES3.0-safe URP material.

Grass — edit (integration only, NOT renderer internals):
- `Assets/WorldPainter/Runtime/Scatter/GrassRenderer.cs` — scope active blades to the current segment window bounds (set field bounds / active region from `SegmentStreamWindow`). Read-only of grass internals; only the active-region input changes.
- `Assets/WorldPainter/Runtime/Scatter/GrassInteractor.cs` — DO NOT redesign. Add a re-bind hook: the interactor self-registers in static `Active` via `OnEnable` (lines 46-50) and removes on `OnDisable`. Ensure the segment/relaunch lifecycle toggles enable or re-adds so `Active` reflects the live interactor after a window slide / reset (otherwise bend dies after the first reset).
- `Assets/WorldPainter/Runtime/Scatter/GrassScatter.cs` / `GrassGpuEngine.cs` / `GrassCpuEngine.cs` — NO changes to render logic. If grass density must follow tier, set per-tier blade-count presets at the call site (`GrassCpuEngine` ignores `SetScaleFactor`/`SetDensity` — feed it presets, do not modify it). This is a tier-input change, not a re-architecture.

Create:
- `Assets/WorldPainter/Runtime/Segment/SegmentGrassBinder.cs` — bridges `SegmentStreamWindow` → grass active region + interactor re-bind. Named-method subscriptions; dispose in `OnDestroy`. The single integration seam so the grass renderer stays untouched.

## Concrete Steps

1. Props: implement the chosen non-compute path; confirm props visible on GLES3.0 (Phase 1 left a log-loud placeholder).
2. Scope props to the active segment via pooling (no global prop set); evict prop colliders/batches when the segment evicts.
3. Grass: author `SegmentGrassBinder` to set the grass active region to the window bounds and re-bind the interactor on each window slide / relaunch.
4. Verify `GrassInteractor.Active` count stays correct (no duplicate/zombie interactors) across many window slides.
5. Per-tier grass blade-count presets fed at the call site (not in the engine).

## Verification

- **Compile:** `read_console` clean; `run_tests` EditMode green; existing `ScaleFactorTests` (grass scale factor) still green.
- **On-device (GLES3.0):** props visible (not blank) on Low; grass bends under the moving interactor; after a simulated relaunch/reset the bend STILL works (re-bind verified); grass not pink.
- **On-device (High):** GPU-indirect prop path still works behind the probe.
- Memory: prop instances scoped to window (evicted props release colliders/batches).

## Success Criteria

- Props render on all three tiers (non-compute on Low, optional GPU on High).
- Grass scoped to the active window (no horizon-wide blade explosion beyond resident segments).
- Interactor re-bind verified across window slides and a reset — bend survives relaunch.
- Zero changes to grass render internals (keep-as-is honored); all integration in `SegmentGrassBinder` + call-site presets.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Props still blank on ES3.0 if fallback path itself uses an unsupported feature | 3 | 5 | 15 | Use standard MeshRenderer (no compute) as default; verify on real device; RenderMeshInstanced only if confirmed ES3.0-safe |
| Interactor bend dies after relaunch (static Active not re-bound) | 3 | 3 | 9 | SegmentGrassBinder re-bind hook; assert Active count post-reset; named-method subscribe |
| Grass density preset wrong for tier (GrassCpuEngine ignores setters) | 3 | 2 | 6 | Feed blade-count presets at call site; validate active-window blade count on ES3.0 (10k-50k is a desktop number) |
| Modifying grass internals by accident | 2 | 4 | 8 | All integration confined to SegmentGrassBinder + call-site presets; code review gate: zero diffs in Grass*Engine render methods |

Score ≥15 mitigated before start: row 1 (standard MeshRenderer default + device verify).
