# Mobile Render Optimization — WorldPainter 45→60 FPS on Adreno 730

**Date:** 2026-06-20 · **Type:** Brainstorm (verified) · **Scope chosen:** C — Full whole-pipeline overhaul · **Method:** 6-subsystem parallel analysis + adversarial verification (30 agents, ~2.4M tokens, workflow `woeehsl28`)

## Problem statement

WorldPainter demo runs ~45 FPS on an Adreno 730 (Snapdragon 8 Gen 1 class, mid-tier). **Target: hold 60 FPS on this exact chip** (mid-tier = the bar; weaker devices may run 30). GPU grass tier **confirmed green** (GPU indirect path, not CPU fallback) — so 45 FPS is true GPU steady-state. Editor profiling is misleading: editor runs **Ultra** (depth-texture ON), device ships **Medium** (depth OFF).

**Constraints (user-confirmed):** dynamic/adaptive quality acceptable (render-scale <1, density cuts, impostor swaps OK). Whole-pipeline overhaul authorized (bake pipeline, terrain bakes, ASTC, variant stripping in scope).

## The verified reframe (premise overturned)

Adversarial verification against the **serialized config** (not assumptions) overturned the working theory. Three "classic" suspects are **already neutralized** and must NOT be re-attempted:

- **Grass is solid-opaque** — `useAlphaclip:0` on grass + props (`WorldMap.asset:561/220`). The TBDR "alpha-test defeats hidden-surface-removal" penalty is **not occurring**. ⚠️ **Guardrail:** introducing alpha-blend / dithered coverage would disable ZWrite/HSR → **−5-15 fps**. Keep blades opaque.
- **Grass casts no shadows** — `ShadowCastingMode.Off`; ShadowCaster pass doesn't fire. No win available.
- **No depth prepass on Medium** — `RequireDepthTexture:0`, `DepthPriming:0`, `RendererFeatures:[]`. Grass DepthOnly pass is dormant on device. (Editor = Ultra = depth ON, hence misleading.)
- One finding was **wrong**: the per-blade `GetAlphamaps` alloc is in `TerrainSurfaceSampler` — **dead code**, never instantiated. Live path is `HeightmapSurfaceSampler` (splat weights = null).

**Decisive conclusion:** the frame is **fragment-bound**. On a fragment-bound TBDR frame, vertex/CPU/bandwidth micro-opts verified to **~0–0.5 fps each** (LOD vertex counts, UInt16 indices, trail-loop guards, draw-call elision, keyword stripping). **One lever dominates: render scale.**

## Ranked verified findings

| Pri | Finding | Verdict | Real gain | Effort | Files |
|---|---|---|---|---|---|
| **P0** | **Adaptive render-scale governor + FSR** — frame-time-driven dynamic resolution | confirmed | **+10–15 fps under load** (0.8×=0.64× fragments, all passes) | M | `PerformanceConsole.cs` (plumbing exists), `FrameRateBootstrap.cs`, `Medium.asset` |
| **P1** | **Build validator** (`IPreprocessBuildWithReport`) — assert Medium tier, HDR-off, MSAA-off, shadowmap≤1024, AlwaysIncludedShaders⊇IndirectGrass, **block `scatterForceTier=ForceCpu`** | confirmed | 0 direct; **insures vs −15-25 fps silent regression** (the original CPU-tier disaster) | M | new `Editor/`, `QualitySettings.asset`, `GrassCpuEngine.cs` |
| **P1** | Opaque-grass guardrail (do NOT add alpha-blend/dither) | confirmed | prevents −5-15 fps mistake | — | — |
| **P2** | **Adaptive grass density controller** — frame-time skip in `BladeCull` (shares P0 signal) | strong | **+3–6 fps on-demand headroom** | M | `GrassCull.compute`, governor |
| **P2** | **Grass scatter bake** → `BakedGrassData` blob; runtime Build = 3× `SetData` | confirmed | 0 steady fps; **−150-600ms startup hitch**; density swaps become buffer-reload not re-scatter | L | `ChunkedBladeBuffer.cs`, `GrassGpuEngine.cs`, `DensityPlacement.cs`, `AuthoredInstancesData.cs` (V3 precedent), `WorldMapBaker.cs` |
| **P2** | LOD2 far-field billboard density falloff (distance-ramped hash skip in `BladeCull`) | — | +2–4 fps far overdraw | M | `GrassCull.compute` |
| **P2** | Terrain normal bake (4 height taps → 1 RG normal fetch) | confirmed | +0.5–1.5 fps terrain-heavy | M | `TerrainPatch.shader`, `TerrainNormals.hlsl`, `WorldMapBaker.cs`, `TerrainTileAsset.cs` |
| **P2** | Terrain ASTC + **mips** on 512² RGBA32 alphamaps; layer-count shader variants | overstated→real | +0.5–1.5 fps combined | S–M | `TerrainPalette.hlsl`, `GpuTerrainEngine.cs`, `TerrainPaletteBinder.cs` |
| **P2** | Half-precision frag / strip unused interpolators (gate `normalize(i.normalWS)`) | overstated | +0.5–1.5 fps | M | `GrassInteractIndirect.shader` |
| **P2** | `SyncLiveMaterialStyle` → gate behind `#if UNITY_EDITOR` (per-frame `CopyPropertiesFromMaterial`×3) | confirmed | 0–0.4 fps now (CPU); matters once props populate | S | `InstancedPropEngine.cs` |
| **P2** | DepthOnly regression guard + shadow-distance tighten | confirmed | guard worth 4-8ms if a feature flips depth on | S | `Medium.asset`, validator |
| **P3** | Prop billboard-impostor atlas (octahedral) | confirmed | **0 today (0 props)**; ~1-3 fps once populated + enables cheap cull-distance push | L | `WorldPainterImpostorLod.cs` (dead scaffold), `InstancedPropEngine.cs`, `ScatterInstanced.shader` |
| **P3** | Shader-variant stripping + `ShaderVariantCollection` warmup | — | frame-time **stability** (1% lows), not steady fps | M | new `IPreprocessShaders` |
| **P3** | Per-tile streamed grass bake (reuse `tileCoordKeys`) | — | 0 fps; amortizes startup upload for large worlds | M | `TerrainStreamingManager`, `BakedGrassData` |

**Already-done / wrong / noise (do NOT spend effort):** grass-shadowcaster-cull (already Off), grass-depthonly-deform (no prepass), GetAlphamaps alloc (dead code — wrong), UInt16 index / trail-loop / baserotation / LOD0-crossover-tuning (all ~0 fps on fragment-bound frame).

## Approaches evaluated

- **A — Render-scale first (minimal):** P0 + FSR + P1 validator + measure. Likely clears 60 by itself. Lowest effort; relies on dynamic-resolution softening.
- **B — Adaptive duo + quality bundle:** A + adaptive density + scatter bake + native-res quality bundle. Two independent adaptive knobs + higher native ceiling → governor sits at 1.0× more often.
- **C — Full overhaul (CHOSEN):** B + prop impostor atlas + per-tile streamed bake + variant stripping/warmup + screen-size LOD. Most thorough; prop items are 0-fps-today future-proofing (props empty), included for completeness + min-spec margin.

## Recommended solution (C, honestly sequenced)

The 60fps **goal** is won by the **render-scale governor (P0)**; the rest is stability, regression-insurance, native-res quality, and future-proofing. Sequence so value front-loads and risk is gated:

1. **Measure-first de-risk (30 min):** on-device PerformanceConsole `Scale`-button test (0.7×) to confirm the ~80% fragment fraction the P0 headline assumes. Cheap; proves the lever before building it.
2. **P0 — Render-scale governor + FSR:** PI controller off smoothed `avgMs` (reuse PerformanceConsole ring buffer), hysteresis + cooldown, floor 0.65 / ceil 1.0, FSR upscale filter (set `FsrOverrideSharpness`). Drive via the existing reflection setter (resolves to active `Medium.asset`).
3. **P1 — Build validator:** lock the mobile render config; block `ForceCpu` and tier/HDR/MSAA/shadowmap/AlwaysIncludedShaders regressions. Prevents re-shipping the original CPU-tier 20fps bug.
4. **P2 — Adaptive grass density controller:** second frame-time knob in `BladeCull` (stable-hash skip + dynamic cull distance), shares the governor's signal so resolution rarely has to swing far. Keep a density floor.
5. **P2 — Grass scatter bake → `BakedGrassData`:** mirrors `AuthoredInstancesData` V3 blob; runtime Build collapses to 3× `SetData`. Kills startup hitch AND makes #4's density swaps a buffer-reload (no main-thread re-scatter spike).
6. **P2 — Native-res quality bundle:** LOD2 far-field density falloff + terrain normal bake + ASTC+mips alphamaps + terrain layer-count variants + frag interpolator strip. Collectively raises the native-res ceiling so the governor idles at 1.0× more often.
7. **C extras (deferred / insurance):** shader-variant stripping + warmup collection (1% lows); prop billboard-impostor atlas + screen-size LOD + `SyncLiveMaterialStyle` editor-gate (**deferred until prop layer is populated — 0 props today**); per-tile streamed grass bake (only if world exceeds streaming radius).

## Risks & considerations

- **P0 headline is assumption-dependent** (~80% fragment fraction). Step 1 measurement de-risks; if fragment fraction is lower, governor still helps but the quality bundle (#6) carries more weight.
- **Render-scale + UI:** verify UI/text renders at native (URP overlay-after-upscale) so HUD stays crisp.
- **Editor vs device divergence:** editor = Ultra (depth ON); always validate fps claims on-device, never in-editor.
- **Scatter bake (L effort, med risk):** touches buffer-sizing + chunk layout; keep `ValidatePartition` as a CI gate; preserve the live editor scatter path for fast iteration (branch on `BakedGrass != null`).
- **ASTC alphamap:** the bigger real win is **enabling mips** (currently `mipChain:false` → full-res sampling at all distances = cache thrash), as much as the ASTC compression itself. Palette albedo array is RGBA32-in-code → needs binder rewrite, not an import flag (not S-effort).
- **Prop work is 0-fps-today** — props layer has 0 instances. Build the path, but don't expect frame gains until populated.

## Success metrics

- **Primary:** sustained ≥60 FPS on Adreno 730 across representative camera framings (grass-heavy ground-level + terrain-heavy vista), governor idling near 1.0× in light views.
- **Stability:** 1% low ≥55 FPS; zero multi-frame hitches on grass field load / density swap (scatter-bake win).
- **Regression gate:** build validator fails the build on any tier/HDR/MSAA/ForceCpu violation.
- **Quality:** no visible softening in light scenes; under-load softening preferred over frame drops.

## Next step

Hand Approach **C** (sequenced above) to `/t1k:plan` for a phased implementation plan with file ownership and per-phase verification. Measure-first (step 1) should be **Phase 0** so the P0 lever is hardware-proven before the governor is built.
