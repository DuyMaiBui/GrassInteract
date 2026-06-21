# GPUGrass — Scene Setup Window + Multi-Terrain Bake + Hi-Z Occlusion

Brainstorm report · 2026-06-21 · module `Assets/GPUGrass/`

## Problem statement

GPUGrass today sets up **one** terrain via a menu item (`Tools ▸ GPUGrass ▸ Auto-Setup
Grass On Terrain`) that creates a **per-terrain** config + bake. Needs:

1. Bake **all** terrains in a scene (currently 1).
2. **Occlusion culling** (today: frustum + distance only).
3. An **editor window** to author **shared** grass properties once and apply to every terrain.
4. **Hide** per-terrain baked placement data; show only the shared editable grass-layer props.
5. Setup applies that **one** shared config to every terrain's controller.
6. (refinement) Move the **bake** + **optimize** tools INTO the window; **strip** the old menu item.

## Locked decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Occlusion approach | **Hi-Z GPU occlusion** (depth pyramid → chunk-AABB test in compute) |
| 2 | Terrain scope | **All active Terrains in the open scene** |
| 3 | Config model | **One scene-shared `GpuGrassConfig`** (editable) + **per-terrain `GpuGrassBakeData`** (hidden) |
| 4 | Existing per-terrain configs | Not deleted/consolidated (safe; window just stops creating new ones) |
| 5 | Entry point | **Single menu** `Tools ▸ GPUGrass ▸ Scene Grass Setup`; strip `Auto-Setup Grass On Terrain` |
| 6 | Hi-Z depth source | **Previous-frame `_CameraDepthTexture`, reprojected** (cheapest; mild popping on fast rotation) |

## Current architecture (verified)

- Config↔bake split already clean: **config = tunables (shareable)**, **bake = per-terrain placement arrays**. The "shared props + hidden baked data" ask maps directly onto this.
- `GpuGrassController` ([ExecuteAlways]) owns one `IGpuGrassRenderer`. `_Blades` is **per-material** → **multiple fields already coexist on GPU**; `_Interactors` is global/shared. No multi-field plumbing needed.
- Cull = chunk-level **frustum + distance** in `GrassCull.compute` (ChunkCull) → blade-level (BladeCull). **No occlusion.**
- `GpuGrassBaker.Bake(terrain, config, bake)` is world-space and **already per-terrain** — just needs calling once per terrain.
- Only production menu item: `Auto-Setup Grass On Terrain`. ("Build Demo Scene" lives in `Samples~`, uncompiled.)

## Design

### Part 1 — Scene Setup Window (low risk)

New `Editor/GpuGrassSceneWindow.cs` → the **only** menu item `Tools ▸ GPUGrass ▸ Scene Grass Setup`.

```
┌─ GPUGrass — Scene Setup ─────────────────┐
│ Shared Config:  [SceneGrassConfig ▾] (+) │  create-if-missing
│ ▼ Setup & Bake                           │
│   ▸ Grass Properties (embedded inspector)│  Editor.CreateEditor(config) → all fields free
│   Terrains in scene (3):                 │
│     ✓ Terrain_A   2,010 blades  Gpu      │  read-only status (bake DATA hidden)
│     ✓ Terrain_B   1,540 blades  Gpu      │
│     ✓ Terrain_C       0 blades  Disabled │
│   [ Setup & Bake All Terrains ]          │  absorbs old menu action
│ ▼ Optimize (Performance)                 │
│   occlusion ☑   adaptive density ☑       │  curated perf subset
│   LOD dists […]  cull 80m  tier [Auto▾]  │
│   [ Re-apply & Rebuild ] [Apply Mobile]  │
└──────────────────────────────────────────┘
```

- **Edit grass props once** = embedded `Editor.CreateEditor(sharedConfig).OnInspectorGUI()` → every `[Header]/[Tooltip]` field renders, zero maintenance.
- **Hide baked data** = `GpuGrassBakeData` arrays never shown; only per-terrain status row (name, blade count, resolved tier).
- **Apply one setting to all** = button loops `Terrain.activeTerrains`; per terrain: ensure `GpuGrassController` → assign **shared** config → ensure own bake asset → bake → `Rebuild()`.

**Refactor (SSOT):** split `GpuGrassAutoSetup.SetupOnTerrain(terrain)` →
`SetupOnTerrain(terrain, GpuGrassConfig sharedConfig)` (config injected, no longer self-created).
Keep the class as a static helper; **remove its `[MenuItem]`**. Window passes the one shared config to all terrains.

### Part 2 — Multi-Terrain Bake (low risk)

Window's loop calls existing `GpuGrassBaker.Bake` once per terrain; each terrain → its own
name-keyed `GpuGrassBakeData`. Each renderer culls its own world-space field. No baker logic change.

### Part 3 — Hi-Z GPU Occlusion (high risk; feature-flagged)

**Shared per-camera Hi-Z, built once — not per field.** New `Runtime/Render/GpuGrassHiZ.cs` + URP `ScriptableRendererFeature`:

1. **Depth pyramid pass** (after opaques): `_CameraDepthTexture` → R32F RT, mip chain via `HiZBuild.compute` (each mip = max-Z of 2×2 parent = conservative farthest). Built once/camera, shared by all fields.
2. **Reprojection:** grass cull runs in `beginCameraRendering`/`LateUpdate` → consumes **prev-frame** Hi-Z reprojected by prev→cur view-proj.
3. **ChunkCull addition** in `GrassCull.compute`: after frustum+distance, project chunk AABB → screen rect + chunk near-depth, sample Hi-Z mip covering the rect, skip if `chunkNear > hiZfar`. One new bind (Hi-Z tex + prev-VP matrix + screen size); **no new dispatch**.

**Mobile guards:** `enableOcclusionCulling` config flag (default ON for GPU tier; auto-OFF when no depth texture / GLES gap). Hi-Z RT capped (≈half-res). Clean fallback to today's frustum+distance cull when off.

## Risk & sequencing

| Part | Risk | Independently shippable |
|---|---|---|
| 1 Window + shared config + strip menu | Low | ✅ unblocks workflow |
| 2 Multi-terrain bake | Low | ✅ with Part 1 |
| 3 Hi-Z occlusion | **High** (new compute + renderer feature; live editor + GLES3 verify) | behind feature flag |

Ship 1+2 first; 3 lands flagged so it can't regress the working frustum-cull path.

## Evaluation (reuse / maintainability / testability)

- **Reuse:** leans entirely on the existing config/bake split, per-material `_Blades`, and per-terrain baker. Hi-Z is shared per-camera (one pyramid for N fields).
- **Maintainability:** embedded config inspector = no field duplication in the window. SSOT shared config removes per-terrain config drift.
- **Testability:** window loop + `SetupOnTerrain(terrain, config)` are pure editor logic (mockable Terrain, as `BakerTests` already does). Hi-Z math (AABB→screen-rect, mip selection) extractable as a pure function for EditMode tests, mirroring `LodThresholds`.

## Open sub-decision (defaulted)

"Optimize tool" → interpreted as the **perf-tuning surface** (occlusion/density/LOD/cull/tier) in the
window, since no standalone optimize menu exists. Correct if a different existing tool was meant.

## Next steps

1. `/t1k:plan` → phased plan (Phase A: window+shared config+strip menu; Phase B: multi-terrain bake; Phase C: Hi-Z occlusion flagged).
2. Phases A/B need only editor compile; Phase C needs the live Unity editor (M3 Pro) + GLES3 smoke.
