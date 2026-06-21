# Plan: GPUGrass Scene Window + Multi-Terrain Bake + Hi-Z Occlusion

Date: 2026-06-21 · Mode: --auto (single-agent) · Source design: `plans/reports/gpugrass-scene-window-multiterrain-occlusion-brainstorm.md`

Replace GPUGrass's single-terrain menu action with one Editor window that authors **shared** grass
properties once and bakes **every** Terrain in the open scene (each keeping its own hidden placement
bake), and add **Hi-Z GPU occlusion culling** (depth-pyramid → chunk-AABB test) behind a feature flag.
The bake tool and the optimize/perf tool both move into the window; the old menu item is stripped.

## Scope summary (all decisions locked — no open questions)

- **Occlusion:** full **Hi-Z GPU occlusion** — shared per-camera depth pyramid, prev-frame `_CameraDepthTexture` reprojected, tested per chunk AABB inside `GrassCull.compute` ChunkCull. Feature-flagged (`enableOcclusionCulling`), auto-OFF when no depth texture / GLES gap.
- **Terrain scope:** all active `Terrain` in the open scene(s).
- **Config model:** ONE scene-shared `GpuGrassConfig` (editable) assigned to every controller; each terrain keeps its OWN `GpuGrassBakeData` (hidden). Existing per-terrain configs are NOT deleted (window just stops creating new ones).
- **Entry point:** single menu `Tools ▸ GPUGrass ▸ Scene Grass Setup` (the window). **Strip** the `Tools ▸ GPUGrass ▸ Auto-Setup Grass On Terrain` `[MenuItem]`; its `SetupOnTerrain` logic survives as an injectable static helper.
- **Window contents:** *Setup & Bake* section (shared-config picker + embedded config inspector + per-terrain read-only status rows + "Setup & Bake All Terrains") and *Optimize (Performance)* section (occlusion/adaptive-density/LOD/cull/tier controls + "Re-apply & Rebuild" + "Apply Mobile Preset").
- **Hidden baked data:** `GpuGrassBakeData` arrays never shown in the window — only name / blade count / resolved tier per terrain.

## Components → phase + file ownership

| Component | Type | Phase | Owns |
|---|---|---|---|
| `GpuGrassAutoSetup.SetupOnTerrain(terrain, sharedConfig)` refactor + strip `[MenuItem]` | editor logic | P1 | `Editor/GpuGrassAutoSetup.cs` (modify) |
| `GpuGrassSceneSetup` (loop active terrains, one shared config, ensure per-terrain bakes) | editor logic | P1 | `Editor/GpuGrassSceneSetup.cs` (new) |
| EditMode tests for scene-setup loop | test | P1 | `Tests/SceneSetupTests.cs` (new) |
| `GpuGrassSceneWindow` (IMGUI window, embedded inspector, status list, Optimize section) | EditorWindow | P2 | `Editor/GpuGrassSceneWindow.cs` (new) |
| `enableOcclusionCulling` flag + accessor | config | P3 | `Runtime/GpuGrassConfig.cs` (modify) |
| `GpuGrassHiZ` (depth-pyramid buffer, prev-VP reproject state, shared per camera) | runtime | P3 | `Runtime/Render/GpuGrassHiZ.cs` (new) |
| `GpuGrassHiZFeature` (URP ScriptableRendererFeature → builds pyramid after opaques) | runtime | P3 | `Runtime/Render/GpuGrassHiZFeature.cs` (new) |
| `HiZBuild.compute` (max-Z mip-chain generation) | shader | P3 | `Shaders/HiZBuild.compute` (new) |
| ChunkCull occlusion test + new binds | shader | P3 | `Shaders/GrassCull.compute` (modify) |
| Hi-Z bind plumbing in renderer | runtime | P3 | `Runtime/Render/GpuGrassRenderer.cs` (modify) |
| Hi-Z AABB→screen-rect + mip-select pure math + EditMode test | runtime+test | P3 | `Runtime/Render/GpuGrassHiZ.cs`, `Tests/HiZMathTests.cs` (new) |

## Phases

- **Phase 1 — Shared-config multi-terrain setup core (no UI)** — refactor `SetupOnTerrain` to take an injected shared config, add `GpuGrassSceneSetup` that loops `Terrain.activeTerrains` against one shared config + per-terrain bakes, strip the old `[MenuItem]`. EditMode-tested. | Effort: M
- **Phase 2 — Scene Setup Window (UI)** — `GpuGrassSceneWindow` single menu item: shared-config picker, embedded config inspector (edit-once), per-terrain status rows (bake data hidden), "Setup & Bake All Terrains" (calls P1), Optimize section. | Effort: M
- **Phase 3 — Hi-Z GPU occlusion (feature-flagged)** — `GpuGrassHiZ` + URP renderer feature + `HiZBuild.compute` mip gen + ChunkCull occlusion test + config flag + renderer plumbing. Pure Hi-Z math EditMode-tested; live GLES3 smoke. | Effort: L

## Feasibility

- **Reuse check:** leans on the EXISTING config↔bake split (config shareable, bake per-terrain), per-material `_Blades` (multiple fields already coexist on GPU — verified in `GpuGrassRenderer`), and `GpuGrassBaker.Bake` (already per-terrain, world-space). No new placement/render core for P1/P2.
- **3-pass library discovery (Phase 3 occlusion):** no Hi-Z / occlusion code exists in GPUGrass today (cull = frustum+distance only, confirmed in `GrassCull.compute` ChunkCull + `GpuGrassRenderer.Submit`). Building new is justified; encode the Hi-Z pattern back into the module README on completion.
- **Complexity:** P1/P2 low (editor logic + IMGUI on existing seams); P3 high (new compute pass + URP renderer feature + prev-frame reprojection + GLES3 verification).

## Dependencies

- P1 blocks P2 (window's bake button calls `GpuGrassSceneSetup`).
- P3 is independent of P1/P2 at the code level (touches renderer/compute/config), BUT its on/off toggle surfaces in the P2 window's Optimize section — so P3's `enableOcclusionCulling` accessor should exist before P2's Optimize section binds it, OR P2 guards the toggle behind a `#if` / null-safe reflection-free check. Chosen: **P2 references the flag directly; land the one-line config field in P1** (see Phase 1 task 4) so P2 + P3 both compile against it.
- Critical path: P1 → P2 (UI) and P1 → P3 (config flag) → P3 (rest). P3's heavy GPU work can proceed in parallel with P2 once the flag lands.

## Guards (mandatory — confirmed in design)

1. **SSOT shared config** — `GpuGrassSceneSetup` assigns the SAME `GpuGrassConfig` instance to every controller; it must NOT clone per terrain. Per-terrain uniqueness lives only in the bake asset.
2. **Per-terrain bake isolation** — each terrain gets/keeps its own name-keyed `GpuGrassBakeData`; the loop must never share one bake across terrains (would cross-place blades).
3. **Occlusion fail-open** — when the Hi-Z texture is unavailable (no depth prepass, GLES feature gap, first frame), ChunkCull MUST behave exactly as today (frustum+distance only). Never cull a chunk on a missing/garbage Hi-Z sample.
4. **Conservative Hi-Z** — mip reduction uses **max-Z (farthest)**; chunk is culled only when its NEAR depth is strictly beyond the Hi-Z FAR sample. No false occlusion of visible grass.
5. **Strip, don't break** — removing the `[MenuItem]` must keep `SetupOnTerrain` callable; existing scenes with per-terrain configs keep working (controller still reads whatever config it holds).

## Risk Assessment

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|---|---|---|---|
| Hi-Z prev-frame reprojection wrong → visible grass popped out (false occlusion) | 4 | 5 | 20 | Guard 4 (conservative max-Z) + Guard 3 (fail-open). Add a debug "disable occlusion" toggle (the config flag). Smoke on hilly terrain: orbit camera, confirm no grass blinks in/out at frustum-visible chunks. HIGH — gates P3 sign-off. |
| URP renderer-feature depth access differs across URP version / GLES3 (no `_CameraDepthTexture`) | 4 | 4 | 16 | Auto-detect depth-texture availability at build; disable occlusion + log once when absent (Guard 3). Cap Hi-Z RT to half-res. Verify on the actual GLES3 mobile tier, not just editor. HIGH — gates P3. |
| Compute-shader edits unverifiable in batch (need live editor) | 4 | 2 | 8 | Extract Hi-Z AABB→screen-rect + mip-select as pure C# (EditMode-tested, mirrors `LodThresholds`); the compute-side test is an in-editor Play-mode smoke (draw-call count + visual). |
| Window's embedded `Editor.CreateEditor` leaks / stale when config swapped | 3 | 2 | 6 | Cache the inner editor; `DestroyImmediate` it on config change + `OnDisable`. Standard IMGUI pattern. |
| Stripping `[MenuItem]` breaks the demo builder (`Samples~`) which may call it | 2 | 3 | 6 | `Samples~` is uncompiled; if its builder calls `SetupOnTerrain`, update the call to the new signature (pass a created config) — verify by grep before strip. |
| Per-terrain bake mixed up across terrains in the loop | 2 | 5 | 10 | Guard 2 + EditMode test asserting N terrains → N distinct bake assets with disjoint positions. |
| `refresh_unity` no-op on asmdef-only edits masks errors | 3 | 2 | 6 | No new asmdef expected (files land in existing GPUGrass / GPUGrass.Editor / GPUGrass.Tests). If any asmdef changes, touch a `.cs` to force recompile. |

**High-risk items (score ≥ 15):** Hi-Z reprojection false occlusion (20), URP/GLES3 depth access (16). Both gate Phase 3 sign-off; both mitigated by the fail-open flag + conservative max-Z + on-device smoke.

## Timeline

| Phase | Effort | Notes |
|---|---|---|
| P1 Shared-config multi-terrain core | M | Start here. Editor logic, EditMode-testable. Lands the `enableOcclusionCulling` config field too. |
| P2 Scene Setup Window | M | Blocked by P1. IMGUI on existing seams; manual editor verify. |
| P3 Hi-Z occlusion | L | Config flag from P1; rest can parallel P2. Highest risk; needs live editor + GLES3 smoke. |
| **Total** | **~L overall** | Critical path: P1 → P2 (ship workflow) ; P1 → P3 (occlusion, flagged follow-up). |

P1+P2 are independently shippable (the working multi-terrain workflow). P3 lands behind the flag so it cannot regress the proven frustum-cull path.

## Unity verification realities

- **P1 (pure editor logic):** EditMode tests via Test Runner (`run_tests`) on synthetic terrains, exactly as `BakerTests` already does (synthetic `TerrainData`). Fully automatable.
- **P2 (IMGUI window):** not unit-testable — open the window, confirm: discovers all terrains, edits the shared config once, "Setup & Bake All" populates every controller with the SAME config + distinct bakes, status rows show counts, bake data not shown. `read_console` clean.
- **P3 (compute + renderer feature):** Hi-Z math → EditMode tests. Occlusion behavior → Play-mode smoke on a hilly multi-terrain scene: confirm grass behind hills stops drawing (draw-call / instance-count drop via profiler) AND no visible grass pops out under camera orbit. Toggle the flag off → identical to today.
- **MCP/editor gotchas:** `refresh_unity` may no-op on asmdef-only edits (touch a `.cs`); MCP timeout ≠ bridge disconnect (Unity busy compiling — wait, never kill/restart the editor). TWO editors register to UnityMCP on this machine — `set_active_instance("GrassInteract@…")` before any MCP work (see module memory).

## Out of scope (v1) — future work

- Consolidating/deleting existing per-terrain `*_GpuGrassConfig` assets (explicitly declined).
- Terrain-group / tile-streaming as a single field (chose per-active-terrain).
- Dedicated terrain-only depth prepass (chose prev-frame `_CameraDepthTexture` reprojection).
- Multi-LOD blade-mesh builder, per-tier presets, ShaderVariantCollection prewarm (existing Pass 3 backlog, unrelated).

---

Cook handoff: `/t1k:cook plans/260621-gpugrass-scene-window-multiterrain-occlusion/plan.md`
