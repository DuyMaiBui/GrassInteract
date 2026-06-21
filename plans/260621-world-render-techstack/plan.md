# Plan: World-Rendering Tech Stack (Mobile, Broad Device Matrix)

**Created:** 2026-06-21 13:18
**Scope:** WORLD RENDERING ONLY — huge linear-corridor world with massive grass, Unity 6 / URP, optimized for mobile down to OpenGL ES 3.0 (no-compute) Android.
**Source of truth:** `plans/reports/260621-launch-racer-techstack.md` (§2 track repr/renderer, §3 keep/cut, §5 device tiers, §6 gaps, §7 phase order, §8 risks) + `plans/reports/260620-worldpainter-custom-vs-builtin-terrain.md` (§4 reconciliation: linear corridor → 1-D streaming).
**Grass renderer:** KEEP AS-IS throughout. This plan only integrates it with the streamed segment window (scope + re-bind). No grass re-architecture.

---

## In Scope

- ES3.0 render-floor unblock (the silent ship-blocker) + 3-tier device probe.
- World/track representation: 1-D baked-segment model + segment asset schema + standard-mesh renderer.
- Cut the runtime CDLOD render path from the shipping build (terrain + props).
- 1-D segment streaming window (sliding window, hysteresis, budgeted instantiate, async Addressables, seam-snap at bake).
- Props rendering re-tier (InstancedPropEngine → standard/instanced, probe-gated GPU optional).
- Grass integration into the segment lifecycle (scope-to-window + re-bind only — keep-as-is).
- WorldPainter editor bake pipeline repurpose (editor-only, emits segment assets; keep ISurfaceSampler family; keep editor assemblies out of runtime build).
- Device-tier scaling for rendering (URP assets + grass density + segment draw distance + prop density).

## Out of Scope (NO phases)

Kart/vehicle physics · gameplay loop/FSM · launch input · camera rig · economy/upgrades · save system · HUD/shop/UI · audio · multiplayer. A flythrough camera needed to test rendering is a throwaway test harness, not a deliverable.

---

## Hard Constraint Driving Everything

`ProjectSettings.asset:559 openGLRequireES31: 0` → **GLES3.0 (no-compute) is a real shipping target.** `Graphics.RenderMeshIndirect` requires compute → it silently no-ops on ES3.0 (never throws). Today both the terrain renderer AND the prop renderer call it unconditionally, so terrain AND props render BLANK on the device floor, and the failure ships green from any compute-capable dev device. **SelfTest cannot catch this** (no throw to catch). Every phase's verification MUST include a real ES3.0 device or a forced-GLES3.0 graphics-API editor run.

---

## Architecture Summary

```
EDITOR (bake-time, never in runtime build)          RUNTIME (mobile build)
─────────────────────────────────────────          ──────────────────────────────────
WorldPainter sculpt/paint/scatter            ──▶    SegmentAsset (Addressable):
  ↳ WorldMapAsset / ISurfaceSampler                   • ribbon terrain Mesh + Material
  ↳ Segment baker (samples centerline)                • baked MeshCollider
      emits per-segment:                              • baked props (standard/instanced)
        - ribbon mesh (seam-snapped verts)            • grass-density map (R8) + scatter cfg
        - collider                                    • baked GI (lightmap refs)
        - props                                       • shared-edge seam metadata
        - grass-density map
        - baked GI                              ──▶  SegmentStreamWindow (1-D):
                                                        • distance-along-track key
DeviceTierProbe (SystemInfo)  ──────────────────▶      • sliding window N segments
  High: Vulkan/Metal + compute                          • hysteresis, per-frame budget
  Mid:  GLES3.1                                          • async Addressables load-ahead
  Low:  GLES3.0 no-compute                               • IObjectPoolManager for props
                                                          • binds grass to active window
                                                       Grass renderer (KEEP AS-IS)
                                                          • GrassTierProbe → GPU/CPU engine
                                                          • re-bound per segment lifecycle
```

Runtime renders standard SRP-batched URP MeshRenderers (terrain + props on Low). The only WorldPainter pieces surviving into the shipped binary: interactive grass (tier-gated) + the 1-D-reduced streaming pattern + ISurfaceSampler family. Everything CDLOD/compute-terrain becomes editor-only bake tooling or is deleted from the runtime asmdef.

---

## Phase Index

| # | Phase | Effort | Blocks | Blocked by |
|---|---|---|---|---|
| 1 | ES3.0 render-floor unblock + device-tier probe + on-hardware proof | M | 2,3,4,6 | — |
| 2 | 1-D baked-segment model + segment asset schema + standard-mesh renderer; cut runtime CDLOD render path | L | 3,5 | 1 |
| 3 | 1-D segment streaming window (sliding window, hysteresis, budgeted instantiate, async load, seam-snap) | L | 6 | 1,2 |
| 4 | Props render re-tier + grass integration into segment window | M | 6 | 1 (props), 3 (grass scope) |
| 5 | WorldPainter editor bake pipeline repurpose (editor-only; emits segment assets) | L | 3 (assets) | 2 (schema) |
| 6 | Device tiers + performance budgets + on-device validation | M | — | 1,2,3,4 |

Critical path: 1 → 2 → 3 → 6. Phase 5 (bake pipeline) runs in parallel with 3/4 once the Phase 2 schema is fixed. Phase 4-props depends only on Phase 1; Phase 4-grass depends on Phase 3's window.

---

## Decisions Requiring Confirmation (surfaced to orchestrator)

AskUserQuestion is unavailable in this subagent context. The plan is written against report-aligned **provisional defaults** (flagged inline in each phase). The orchestrator should confirm these 4 before Phase 2/3/5 start — each changes phase structure:

1. **Centerline source** — `com.unity.splines` is NOT installed (verified absent from `Packages/manifest.json` + `packages-lock.json`). Default in plan: **add com.unity.splines (editor-only)**. Alt: chain WorldMapAsset tiles (no new package). Affects Phase 5.
2. **Segment mesh type** — Default: **baked ribbon mesh**. Alt: per-segment built-in Terrain. Affects Phase 2 + 5.
3. **Props render path on Low** — Default: **standard MeshRenderers** (simplest, guaranteed ES3.0). Alt: RenderMeshInstanced / probe-gated GPU. Affects Phase 4.
4. **Segment length + window size** — Default: **~100m segment, 5-window (2 ahead / 1 current / 2 behind)**, ~500m resident. Affects Phase 3 memory math.

---

## Cross-Phase Risk Table

| Risk | L | I | Score | Mitigation | Owner phase |
|---|---|---|---|---|---|
| GLES3.0 silent blank-render broader than terrain (terrain @504/534 + props @508/510/512 both unconditional RenderMeshIndirect) — ships green, blank to users | 5 | 5 | 25 | Phase 1 FIRST. Replace/probe-gate both paths; verify on real ES3.0 device or forced-GLES3.0 editor run, NOT SelfTest | 1 |
| Grass shaders `#pragma target 4.5` / `Fallback Off` (incl. CPU-tier `GrassInteractInstanced.shader:42/121/216` + `:278 Fallback Off`) → pink/missing grass on true ES3.0 even on CPU tier | 4 | 5 | 20 | Lower/validate shader target on real ES3.0 hardware; never rely on emulator. Confirm CPU-tier shader compiles | 1 |
| Runtime asmdef ships dead CDLOD/compute code into mobile binary (`WorldPainter.asmdef` includePlatforms=[], autoReferenced=true → all terrain/compute code compiles in) | 4 | 4 | 16 | Phase 5: split runtime asmdef; move CDLOD/compute-terrain to editor-only or delete; shader-stripper for compute shaders | 5 |
| Segment-boundary seams (CDLOD skirt tool is being cut) → visible cracks at every join | 4 | 4 | 16 | Seam-snap shared edge verts (pos+normal+UV) at BAKE time; bake-time validation test asserting edge-vert equality | 3,5 |
| GrassInteractor self-registers OnEnable into static `Active` (`GrassInteractor.cs:46-50`) → bend dies / leaks across segment churn if not re-bound | 3 | 3 | 9 | Re-bind interactor on segment/interactor lifecycle; named-method subscribe; verify Active list count across window slides | 4 |
| Streaming thrash at boundaries / per-frame instantiate spikes | 3 | 3 | 9 | Hysteresis (keep 1-2 behind); per-frame instantiate + Addressables-load budget; pool props via IObjectPoolManager | 3 |
| Low URP asset HDR on (report §5) → wasted bandwidth on the weakest devices | 3 | 2 | 6 | Phase 6: fix Low URP asset HDR off; MobileRenderConfigValidator already asserts HDR off for active asset | 6 |

Scores ≥15 (rows 1-4) must be mitigated before their owner phase begins.

---

## Timeline

| Phase | Effort | Notes |
|---|---|---|
| 1 ES3.0 unblock + probe | M | Highest priority; gates everything; needs real device |
| 2 Segment model + cut CDLOD render | L | Largest deletion + new schema |
| 3 1-D streaming window | L | Core runtime system |
| 4 Props re-tier + grass integration | M | Props depends on P1; grass on P3 |
| 5 Bake pipeline repurpose | L | Parallel with P3/4 after P2 schema |
| 6 Device tiers + budgets + validation | M | Closes the loop on real hardware |
| **Total** | **~3L + 3M** | Critical path: 1 → 2 → 3 → 6 |

---

## Repo Conventions (enforced in every phase)

- C#: private `camelCase` (no underscore), `this.` prefix mandatory, `[SerializeField] private`, `UPPER_SNAKE_CASE` consts, `#nullable enable`, files ≤200 lines, guard clauses.
- DI: VContainer only (NOT Zenject). UniTask for async (Addressables load-ahead). R3 for reactive. Dispose subscriptions in `OnDestroy`.
- Mono runtime spawning of pooled segment props/instances: `TheOne.Pooling.IObjectPoolManager` (`Load`/`Spawn<T>`/`Recycle`/`Unload`), `nameof(View)` as pool key (per `mono-pool-spawn-unity.md`). Editor-only bake scaffolding may instantiate freely.
- DOTS NOT used here.
- No magic numbers — streaming/budget constants in a config class (pattern: `TerrainStreamingConfig.cs`).
