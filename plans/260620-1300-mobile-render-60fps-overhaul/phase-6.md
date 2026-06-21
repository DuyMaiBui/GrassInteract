# Phase 6 — C extras / deferred insurance (M)

**Priority:** P3. **Effort:** M. **Mostly deferred / conditional.** Future-proofing + 1%-low stability.

## Objective

The "C extras" tier: thoroughness and min-spec margin, not steady-FPS for the current scene. Two of the three sub-items are **explicitly deferred or conditional** — included for completeness so the path exists, not because they gain frames today.

## Sub-items

### 6a — Shader-variant stripping + warmup collection (ACTIVE — 1% lows)

An `IPreprocessShaders` that strips never-used variants (cuts build size + first-use compile hitches) plus a `ShaderVariantCollection` warmed at load. Win is **frame-time STABILITY (1% lows)**, NOT steady FPS — first-use shader compile stalls are a stutter source on mobile, and warming the collection removes them.
- **Create:** `Assets/WorldPainter/Editor/Build/WorldPainterShaderStripper.cs` (`IPreprocessShaders`), and a `ShaderVariantCollection` asset + a runtime warmup call (e.g. from a bootstrap, `ShaderVariantCollection.WarmUp`).
- **Verify on device:** 1% low improves (fewer first-use compile hitches) on first traversal of grass + terrain; steady FPS unchanged.

### 6b — Prop billboard-impostor atlas (DEFERRED — 0 props today)

Octahedral billboard-impostor atlas + screen-size LOD for dense props, plus gating `InstancedPropEngine.SyncLiveMaterialStyle` behind `#if UNITY_EDITOR` (it does a per-frame `CopyPropertiesFromMaterial` ×3 — pure editor live-edit convenience, dead cost in a build).

**DEFER the impostor atlas until the prop layer is populated.** The props layer has **0 instances today → 0 fps to gain today.** Build it only when props ship; documented here so the path is known.

Two things ARE worth doing now (cheap, correct):
- **Gate `SyncLiveMaterialStyle` behind `#if UNITY_EDITOR`** (`InstancedPropEngine.cs` line ~890 / call site ~461) — removes a per-frame ×3 `CopyPropertiesFromMaterial` from device builds. ~0 fps now (props empty) but correct and free.
- **Resolve the dead `WorldPainterImpostorLod` scaffold:** it is fully implemented (`SelectLod`/`SelectLodBatch`/`IsImpostor`) but **not wired** to anything. Either wire it as the LOD1→LOD2 switch SSOT for props (when 6b lands) **or delete it** so it is not mistaken for live code. Do not leave dead scaffold.
- **Edit:** `Assets/WorldPainter/Runtime/Scatter/InstancedPropEngine.cs` (gate + impostor wiring), `Assets/WorldPainter/Runtime/Render/WorldPainterImpostorLod.cs` (wire as SSOT or delete), `Assets/WorldPainter/Shaders/ScatterInstanced.shader` (impostor sampling — deferred).

### 6c — Per-tile streamed grass bake (CONDITIONAL — only if world > streaming radius)

Extend Phase 4's `BakedGrassData` to per-tile buckets keyed by `tileCoordKeys` (the same per-tile keying `AuthoredInstancesData` already uses — `RegisterTileBucket`/`GetTileBucketRange`), so streamed worlds upload grass per-tile as tiles load rather than all at once.

**ONLY do this if the world exceeds the streaming radius.** For a **single grass field, Phase 4's whole-field bake suffices** — per-tile buckets add complexity for no benefit. Documented so the path exists for large worlds.
- **Edit (deferred):** `Assets/WorldPainter/Runtime/Scatter/BakedGrassData.cs` (per-tile buckets), the terrain streaming manager (upload-on-tile-load), reuse `tileCoordKeys`.

## File ownership (summary)

- **Active now:** `Assets/WorldPainter/Editor/Build/WorldPainterShaderStripper.cs` (new, 6a), `ShaderVariantCollection` asset + warmup (6a), `Assets/WorldPainter/Runtime/Scatter/InstancedPropEngine.cs` (`#if UNITY_EDITOR` gate, 6b), `Assets/WorldPainter/Runtime/Render/WorldPainterImpostorLod.cs` (wire-or-delete decision, 6b).
- **Deferred:** prop impostor atlas (6b — until props populate), per-tile streamed bake (6c — until world > streaming radius).

## Step-by-step tasks

1. 6a: author the shader stripper + variant collection + warmup. Verify 1% lows improve on device first traversal.
2. 6b cheap part: gate `SyncLiveMaterialStyle` behind `#if UNITY_EDITOR`; verify on device (no per-frame CopyProperties in build).
3. 6b scaffold: decide `WorldPainterImpostorLod` — wire as prop LOD1→LOD2 SSOT OR delete. No dead code left.
4. 6b atlas + 6c: DEFER. Document the trigger conditions (props populated / world > streaming radius). Do not implement against an empty prop layer or single field.

## On-device verification gate (PASS criteria)

- [ ] 6a: **1% low improves** on first traversal (fewer first-use shader-compile hitches); steady FPS unchanged. Measured on device via PerformanceConsole 1%-low.
- [ ] 6b gate: device build no longer runs `SyncLiveMaterialStyle` per frame (confirm via profiler / no `CopyPropertiesFromMaterial` cost); editor live-edit of prop material style still works.
- [ ] 6b scaffold: `WorldPainterImpostorLod` is either wired (with a test exercising the LOD switch) or deleted — no dead scaffold remains.
- [ ] Deferred items: explicitly recorded as not-implemented with their trigger condition; NOT shipped against an empty prop layer / single field.

## Risk note

Lowest-risk phase (mostly insurance/deferral). The only live change with on-device impact is 6a (variant warmup) — verify it does not *increase* load time disproportionately (warmup cost vs first-use-stall trade). The `#if UNITY_EDITOR` gate is a pure win. Deferring 6b/6c avoids spending effort on 0-fps-today work (props empty, single field) — that deferral IS the correct call per the brainstorm.
