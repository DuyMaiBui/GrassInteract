# Plan: WorldPainter — Unified Assembly Merge, Rename & SOLID Refactor

**Branch:** `plan/gpu-terrain-cdlod` · **Engine:** Unity 6 · custom GPU CDLOD terrain + GPU/CPU grass-prop scatter (NOT Unity Terrain, NOT DOTS)
**Plan dir:** `plans/260611-2319-worldpainter-merge/` — **NEW** dir. Why new (not a continuation of `260611-1845-worldpainter-unified-authoring/`): that plan is the *feature build* (P1–P6, COMPLETE, committed+pushed). This is the destructive *merge / rename / refactor* that follows it — a distinct deliverable with its own phase set and risk profile.
**Predecessor handoff:** `plans/260611-1845-worldpainter-unified-authoring/HANDOFF.md` — 301 EditMode tests green, working tree clean.
**Conventions:** `.claude/rules/code-conventions-unity.md` (`this.` mandatory, camelCase private fields, PascalCase public, `UPPER_SNAKE_CASE` consts, ≤200 lines/file → split partials) · `.claude/rules/library-quality-mandate-unity.md` (generic role names, one root namespace per package, `.asmdef` per concern).
**Verify-once discipline:** `.claude/rules/ai-velocity-batch-compile-unity.md` — per-phase gate = `read_console` (ALL errors) + `run_tests` (ALL 301) in ONE pass; never per-edit. Namespace/asmdef rename triggers a full recompile + long domain reload — budget for it; MCP timeout ≠ disconnect.
**Editor-tool safety:** `.claude/rules/unity-forbidden-operations.md` — never kill/restart Unity, never `Reimport All`. **Unity MCP pin:** ALWAYS `set_active_instance("GrassInteract@de203215")` (port 6402), verify path ends `/GrassInteract/Assets`, RE-PIN after every domain reload (two-instance trap — see predecessor handoff §Session Notes).
**.meta/GUID safety:** every file move uses `git mv` (or Unity MCP move) — NEVER plain delete+recreate, which loses the `.meta` GUID and breaks scene/prefab/asset references.

---

## 0. ESCALATED DESIGN DECISIONS — confirm with user before the DELETE phases

Scouting proved the brief's locked decision #2 ("DELETE `ScatterField`, `GrassScatter` MonoBehaviours; KEEP only SSOT scatter types") **collides with code reality**. AskUserQuestion is unavailable to this planning subagent, so these are escalated to the orchestrator with an **evidence-backed recommended default**. The plan is built around the recommended defaults; if the user overrides, the affected phases (P6, P7) change scope.

| # | Decision | Evidence | Recommended default |
|---|---|---|---|
| **E1** | **Is `ScatterField` deletable legacy or live KEEP runtime?** | `ScatterField` is the live grass/scatter engine host: builds `IGrassEngine`(`GrassGpuEngine`/`GrassCpuEngine`) + `InstancedPropEngine` per layer from `TerrainScatterConfig`/`InstanceScatterLayer`/`DensityScatterLayer`/`ScatterLod`. `WorldPainter.cs DriveScatterField()` drives a co-located `ScatterField` for P3a/P4 grass+prop rendering. `GpuTerrainScatterGround`/`WorldPainterMigration`/`WorldPainterScatterLayerCard` also reference it. The "single scene consumer" evidence checked *scene* refs only — it missed ~30 *code* refs from KEPT WorldPainter runtime. | **KEEP-rehome** `ScatterField` + its `IGrassEngine`/prop-engine cluster as runtime WorldPainter renders through. Delete ONLY the truly-dead *authoring* (`ScatterGizmos`, `ScatterBrushLibrary`, `ScatterFieldEditorTick`, etc.) per HR#1/locked-#2, NOT the runtime engine host. |
| **E2** | **`GrassScatter` "MonoBehaviour" — delete?** | No `GrassScatter : MonoBehaviour` exists. `GrassScatter` is a `static class` CPU scatter builder + `class GrassScatterResult`, consumed by the **frozen** `ChunkedInstanceBuffer` (KEEP) + `ChunkedInstanceBufferTests`. The brief mis-identified it. | **KEEP-rehome** the static builder (deleting it breaks frozen `ChunkedInstanceBuffer` + its test). Nothing named `GrassScatter` is a deletable MonoBehaviour. |
| **E3** | **`DensityPaintGPU` delete vs its 8 stamp-math tests** | `DensityPaintGPU` is a DELETE target, but `DensityBrushMathTests` calls `DensityPaintGPU.ComputeStampPositions` (8 cases) in the SAME file that tests the KEEP `WorldPainterDensityEncoder`. Stamp-position math is behavior now owned by WorldPainter's spacing-stamp path. | **Port-then-delete:** before deleting `DensityPaintGPU`, confirm `ComputeStampPositions` math lives in the WorldPainter stamping path (or move it there), re-point the 8 tests at the WP equivalent, THEN delete. No coverage lost. |
| **E4** | **3 straddle tests of DELETE targets** (`TerrainSculptUndoTests`→`TerrainSculptUndo`, `TerrainSculptRtWritebackTests`→`TerrainSculptRtWriteback`, `TerrainBrushPreviewTests`→`TerrainBrushPreview.CreateUnitDisc`) | Each directly instantiates a DELETE-target class. WorldPainter has parallel equivalents (`WorldPainterUndo`, `WorldPainterDensityEncoder`, brush-disc geometry). | **Per-test migrate:** if WorldPainter has the equivalent behavior, re-point the test; only drop a test whose exact behavior is genuinely gone. Decision documented per-test in P7. **No silent coverage drop** (development-principles §Pre-Delete Reference Check + Test Pass Gate). |

> Additional confirmed fact: a SECOND demo scene exists — `GpuTerrain/Demo/TerrainValidation.unity` (the `GpuTerrainRenderer` 2-tile validation scene, built by `TerrainValidationSceneBuilder` menu). The brief authorized deleting ONLY `GrassInteract/Demo/GrassInteractDemo.unity`. **TerrainValidation.unity is KEPT** (rehomed) unless the user says otherwise — flag E5 if they want it gone.

---

## 1. File classification summary

183 `.cs` files total (110 GpuTerrain + 73 GrassInteract) + 18 non-`.cs` assets (shaders/compute/uss/uxml).

| Class | Count (.cs) | Meaning |
|---|---|---|
| **KEEP-rehome** | **~138** | Move into `WorldPainter`/`WorldPainter.Editor`/`WorldPainter.Tests`, preserve `.meta`/GUID, rename namespace. Includes all frozen SSOT, the CDLOD/streaming/render runtime, the `ScatterField`+`IGrassEngine` grass/prop cluster (E1), the static `GrassScatter` builder (E2), ALL WorldPainter P1–P6 editor code, and the surviving tests. |
| **DELETE** | **~28** | Old superseded authoring + truly-dead legacy authoring (per HR#1 + locked-#2). Enumerated in §3.2. Each gets a pre-delete ref check. |
| **MERGE/CONSOLIDATE** | **~8 pairs** | Transitional parallel classes: `WorldPainter*` (keep) vs `TerrainSculpt*`/`GpuTerrainRenderer*` (delete) — consolidate once old path is gone (§3.3). Plus oversized-file splits (§3.4). |

Non-`.cs`: KEEP-rehome the terrain shaders/compute/hlsl + WorldPainter `.uss`; DELETE the ScatterStudio `.uss`/`.uxml` + `DensityPaintBrush.shader` (belong to deleted window).

Full per-file table: `phase-1.md` (the inventory deliverable).

---

## 2. Target architecture

### 2.1 Target asmdefs (3, down from 6)

| New asmdef | name / rootNamespace | references | platforms | replaces |
|---|---|---|---|---|
| `Assets/WorldPainter/WorldPainter.asmdef` | `WorldPainter` / `WorldPainter` | (none) | all | `GrassInteract` + `GpuTerrain` runtime (merged — the old linear chain `GrassInteract ← GpuTerrain` collapses into one assembly, so the cross-assembly reference disappears) |
| `Assets/WorldPainter/Editor/WorldPainter.Editor.asmdef` | `WorldPainter.Editor` / `WorldPainter.Editor` | `WorldPainter` | Editor | `GrassInteract.Editor` + `GpuTerrain.Editor` |
| `Assets/WorldPainter/Tests/Editor/WorldPainter.Tests.asmdef` | `WorldPainter.Tests` / `WorldPainter.Tests` | `WorldPainter`, `WorldPainter.Editor`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Unity.Collections` + precompiled `nunit.framework.dll`, `overrideReferences:true`, `autoReferenced:false`, defineConstraint `UNITY_INCLUDE_TESTS` | Editor | `GrassInteract.EditorTests` + `GpuTerrain.EditorTests` (merged — the test cross-refs collapse to intra-assembly) |

Merging the two runtime assemblies into one means the previous `GpuTerrain → GrassInteract` reference and the test cross-refs become intra-assembly — simpler, no chain.

### 2.2 Feature-folder layout (target tree)

This is a tech/library project, not the `_Game/<Game>` TheOne layout — so the `_Game/` convention from `t1k-unity-base-project-structure` does not apply literally, but its **Runtime/Editor/Tests split + per-feature subfolders** principle does. Target:

```
Assets/WorldPainter/
├── WorldPainter.asmdef
├── Runtime/
│   ├── Terrain/        TerrainTileAsset, TerrainWorldGrid, TerrainHeightFormat, Cdlod*, GpuTerrainEngine,
│   │                   TerrainStreamingManager, TerrainResidencyRing, TerrainTileLoader, TerrainTileResidencySet,
│   │                   TerrainTileGpuResources, TerrainNodeBuffer, TerrainPatchMesh, TerrainLayerSet, TerrainCollider*,
│   │                   TerrainShadingConfig, TerrainStreamingConfig, HeightmapSurfaceSampler, TerrainHeightSampleCpu, TerrainSurfaceSampler
│   ├── Render/         GpuTerrainRenderer (consolidated → WorldPainter.Render), WorldPainterImpostorLod
│   ├── Scatter/        ScatterField, ScatterLayer/DensityScatterLayer/InstanceScatterLayer, ScatterLod,
│   │                   Scatter*Config, GrassScatter(+Result), IGrassEngine, GrassGpuEngine/GrassCpuEngine,
│   │                   GrassRenderer, GrassBendSimulator, GrassFieldSpace, ChunkedBladeBuffer/ChunkedInstanceBuffer,
│   │                   InstancedPropEngine, InstanceBatchPool/ColliderPool, Instance*Placement, DensityPlacement,
│   │                   AuthoredInstancesData, ISurfaceSampler, RaycastSurfaceSampler, GrassInteractor*, GrassTrail*, BrushStamp
│   ├── Biome/          BiomePreset
│   ├── WorldPainter.cs / WorldPainter.Data.cs / WorldPainter.Render.cs  (the component, runtime-only)
│   └── AssemblyInfo.cs
├── Shaders/            TerrainBrush.compute, BrushMask.hlsl, TerrainNodeCull.compute, Terrain*.hlsl/.shader,
│                       BrushDecal.shader, GrassCull.compute, Grass*.shader, ScatterInstanced.shader
├── Editor/
│   ├── WorldPainter.Editor.asmdef
│   ├── WorldPainter/   (all P1–P6 authoring UI: LayerStack, FilterChips, BrushDock, Biome*, Scatter/Splat/Prop cards,
│   │                    LodPreview/BandRuler, MiniMap, PerfBadge, SceneInput/Overlay, CoachMarks, PresetSlots, etc.)
│   ├── Brush/          WorldPainterSculptTool(+Stroke split), WorldPainterStroke, WorldPainterUndo, WorldPainterState,
│   │                    WorldPainterDensityEncoder, BrushFalloffLut, TerrainPaintTargetResolver, TileRtCache,
│   │                    TerrainSculptRtWriteback (consolidated)
│   ├── Inspector/      WorldPainterInspector, TerrainTileAssetEditor
│   ├── Import/         TerrainTileImporter, TerrainValidationSceneBuilder
│   ├── Migration/      WorldPainterMigration
│   ├── Resources/      WorldPainter.uss, WorldPainterLight.uss
│   └── AssemblyInfo.cs (GpuTerrainEditorAssemblyInfo merged)
├── Tests/Editor/       WorldPainter.Tests.asmdef + all surviving tests (frozen-data/math + owner + brush)
└── Demo/               TerrainValidation.unity (KEPT — E5 if user wants gone) + its tiles/mats/layerset
```

> The leaf folder names use generic-role tokens per the Unity naming charter. No file *renames* are required by this plan beyond the namespace rewrite + the oversized-file partial splits — type names already conform (`WorldPainter*` is the generic feature role).

### 2.3 Frozen SSOT (preserve behavior — do NOT touch logic)
`TerrainTileAsset`, `TerrainWorldGrid`, `TerrainHeightFormat`, `CdlodQuadtree`, `CdlodNode`, `ScatterLod`, `InstanceScatterLayer`, `DensityScatterLayer`, `ScatterLayer`, `AuthoredInstancesData`, `ChunkedInstanceBuffer`, `GpuTerrainEngine`, `HeightmapSurfaceSampler : ISurfaceSampler` seam. Moving/namespace-renaming is allowed (updates all refs); **compute kernel signatures in `TerrainBrush.compute` stay stable** — `TerrainBrushMathTests` (the behavior contract) must not change.

---

## 3. File classification detail

### 3.1 KEEP-rehome (move + namespace rewrite, preserve GUID)
All Runtime files of both assemblies EXCEPT the delete list below; all `WorldPainter*` editor files; all surviving tests. See `phase-1.md` for the exhaustive table. Notable KEEPs that the brief implied were deletable: `ScatterField`, `GrassScatter`(static), `TerrainScatterConfig`, the `IGrassEngine` cluster (E1/E2), `TerrainValidation.unity` (E5).

### 3.2 DELETE (per HR#1 + locked-#2 — each with pre-delete ref check)

**Old superseded terrain authoring (HR#1):**
`GpuTerrainRendererEditor.cs`, `GpuTerrainRendererEditor.Sculpt.cs`, `TerrainSculptTool.cs`, `TerrainSculptTool.Stroke.cs`, `TerrainBrushStroke.cs`, `TerrainBrushPreview.cs`, `TerrainSculptState.cs`, `TerrainSculptConfig.cs`, `TerrainSculptUndo.cs`, `WorldPainterSculptTool.Density.cs` (if `DensityPaintGPU` fold-in superseded it — verify).

**Legacy scatter authoring (locked-#2 — runtime engine host KEPT per E1, only authoring deleted):**
`ScatterStudioWindow.cs`, `ScatterStudio/*` (AnchorPreviewPanel, BrushLibraryView, DensityPaintGPU, DensityPaintPanel, InstanceGhostPreview, InstancePanel, LayerPanelView, LayerRailView, LodDistanceBar, ScatterBrushPreview), `ScatterGizmos.cs`, `ScatterBrushLibrary.cs`, `ScatterBrushLibraryProvider.cs`, `ScatterDensityOverlay.cs`, `ScatterFieldEditorTick.cs`, `ScatterFieldLookup.cs`, `ScatterRebuildScheduler.cs`, `ScatterAuthoringState.cs`, `DensityPaintTool.cs`, `DensityMapFactory.cs`, `InstancePlacementTool.cs`, `TerrainScatterConfigEditor.cs` (if superseded by WorldPainter inspector — verify it isn't the only editor for a KEPT config).

**Demo (locked-#2):** `GrassInteract/Demo/GrassInteractDemo.unity` + its meta. (Demo assets `DensityMap.*`, `GrassInteractDemo.mat`, `TerrainScatterConfig.asset`, `GrassInteractDemoEffector.cs` — delete if scene-only; `TerrainScatterConfig.asset` ref-check: KEEP if WorldPainter migration/runtime reads it.)

**Non-.cs:** `ScatterStudio.uss`, `ScatterStudio.uxml`, `ScatterStudioLight.uss`, `DensityPaintBrush.shader` (deleted-window assets).

> **MANDATORY per deletion target:** grep every caller across runtime+editor+tests+scenes (`development-principles.md §Pre-Delete Reference Check`), update/remove refs, run tests, THEN `git rm` (carries `.meta`).

### 3.3 MERGE/CONSOLIDATE (transitional parallel classes)
Once 3.2 deletes land, the surviving `WorldPainter*` classes are the sole path — consolidate any duplicated helpers:
- `GpuTerrainRenderer` (runtime) ↔ `WorldPainter.Render.cs` ("mirrors GpuTerrainRenderer exactly"). Keep ONE multi-tile submit path. **Caution:** `GpuTerrainRenderer` is referenced by the KEPT `TerrainValidation.unity` scene + `TerrainTileAssetEditor` + `TerrainValidationSceneBuilder` — if consolidating into `WorldPainter.Render`, either keep `GpuTerrainRenderer` as the scene-facing component OR migrate the scene. Decide in P8 (ref-check first).
- `TerrainSculptRtWriteback` (KEEP — still referenced by `WorldPainterDensityEncoder` + `WorldPainterSculptTool`) — keep, but verify no dead duplication vs WorldPainter writeback.
- `TerrainPaintTargetResolver`, `TileRtCache`, `BrushFalloffLut` — shared KEEP utilities; confirm single-owner after old tool deletion.

### 3.4 Oversized-file splits (≤200-line guideline)
| File | Lines | Action |
|---|---|---|
| `WorldPainterSculptTool.Stroke.cs` | **349** (handoff said 211 — actually 349) | Split by responsibility: stroke-path interpolation / per-stamp dispatch / payload-kernel selection. |
| `WorldPainterUndo.cs` | 269 | Split: snapshot ring vs Unity-Undo integration. |
| `WorldPainter/WorldPainterLayerStackView.Mutations.cs` | 256 | Already a partial; split mutation ops by layer-type if still >200. |
| `WorldPainterSculptTool.cs` | 247 | Split tool lifecycle vs event handling. |
| `WorldPainter/WorldPainterLayerStackView.cs` | 246 | Split view-build vs data-binding. |
| `WorldPainter/WorldPainterBrushDock.cs` | 243 | Split controls-build vs callbacks. |
| `WorldPainter/WorldPainterLodPreviewPanel.cs` | 231 | Split preview-render vs UI. |
| `WorldPainterMigration.cs` | 230 | Split scan vs write. |
| `WorldPainter/WorldPainterBiomePaletteView.cs` | 222 | Split card-build vs interaction. |
| `WorldPainterInspector.cs` / `WorldPainterLodBandRuler.cs` | 212 | Borderline — split only if a clean responsibility seam exists. |

(`TerrainSculptRtWriteback` 298, `TerrainBrushPreview` 289, `TerrainValidationSceneBuilder` 253 — first two are DELETE/borderline; `TerrainValidationSceneBuilder` split only if kept and a seam exists.)

---

## 4. Phase index

Ordered to keep the project **compiling at every phase boundary**. The hard constraint: a half-renamed assembly does not compile, so **asmdef merge + namespace rewrite of an assembly's files must be ONE atomic phase** per assembly-move. Strategy: move files first under the OLD namespaces (compiles because asmdefs still reference correctly), then flip asmdef+namespace as atomic units.

| Phase | Name | Effort | Blocked by | Compiles at boundary? |
|---|---|---|---|---|
| **P1** | Inventory + dependency map (the per-file KEEP/DELETE/MERGE table, every cross-ref) — DELIVERABLE ONLY, no code | **M** | — | n/a (no code change) |
| **P2** | Target asmdef + feature-folder design lock + reference-check pass (grep every delete target's callers, record blast radius) | **S** | P1 | n/a |
| **P3** | Create `Assets/WorldPainter/` + 3 new asmdefs; `git mv` ALL runtime KEEP files into `WorldPainter/Runtime/<feature>/` — **keep old namespaces for now**, new single `WorldPainter` asmdef replaces the 2 old runtime asmdefs. ATOMIC. | **L** | P2 | YES (intra-assembly refs unchanged; cross-asm ref now internal) |
| **P4** | `git mv` editor KEEP files into `WorldPainter/Editor/<feature>/`; new `WorldPainter.Editor` asmdef. ATOMIC. | **M** | P3 | YES |
| **P5** | `git mv` test KEEP files into `WorldPainter/Tests/Editor/`; new `WorldPainter.Tests` asmdef. **GATE: 301 green.** | **M** | P4 | YES |
| **P6** | DELETE old terrain authoring (3.2 group 1) — per-target ref check, migrate/keep the 3 straddle tests (E4) + density tests (E3). **GATE: green (count may drop only by genuinely-obsolete tests, documented).** | **M** | P5 | YES |
| **P7** | DELETE legacy scatter authoring (3.2 group 2) + `GrassInteractDemo.unity` (locked-#2) — per-target ref check; sever the 4 dead-authoring refs while KEEPING `ScatterField` runtime (E1). **GATE: 301-minus-documented green.** | **M** | P6 | YES |
| **P8** | MERGE/CONSOLIDATE parallel classes (3.3) + oversized-file splits (3.4). SOLID/dedup. **GATE: green.** | **L** | P7 | YES |
| **P9** | Namespace rewrite `GpuTerrain`/`GrassInteract` → `WorldPainter` across all surviving files + asmdef `rootNamespace`. ATOMIC, single batch, verify ONCE (full recompile + long reload). **GATE: 301 green.** | **L** | P8 | YES (atomic) |
| **P10** | Final full-suite verify + cleanup (stray metas, folder metas, packages-lock resolve) + commit/push. **FINAL GATE: 301 green, build-isolation check (authoring absent from player build).** | **S** | P9 | YES |

**Critical path:** P1→P2→P3→P4→P5→P6→P7→P8→P9→P10 (mostly linear — file moves and renames cannot safely parallelize on a shared working tree; per `parallel-teammate-git-index-race.md` + the Unity single-worktree compile-gate, these serialize).
**Atomic-phase callouts:** P3, P4, P9 each touch a whole assembly's compile surface — must land as one batch with ONE verify, not incremental.

Phase detail: `phase-1.md` … `phase-10.md`.

---

## 5. Risk Assessment (L×I, ≥15 = high → mitigate before phase starts)

| # | Risk | L | I | Score | Mitigation | Phase |
|---|---|---|---|---|---|---|
| R1 | **Deleting `ScatterField` (per literal brief) guts WorldPainter grass/prop rendering** | 5 | 5 | **25** | **E1 escalation** — reclassify `ScatterField`+`IGrassEngine` cluster as KEEP-rehome. Do NOT delete until user confirms. Plan default = KEEP. | P0/P7 |
| R2 | **`.meta`/GUID loss on file move breaks scene/prefab/asset references** | 4 | 5 | **20** | `git mv` only (carries `.meta`); never delete+recreate. After each move phase, open Unity + `read_console` for missing-GUID warnings + run tests. `TileA_0_0.asset`/`ValidationLayerSet.asset` GUID refs verified intact. | P3,P4,P5 |
| R3 | **Half-renamed assembly fails to compile (namespace rewrite mid-flight)** | 4 | 5 | **20** | Namespace rewrite is its OWN atomic phase (P9), executed as a single batch with ONE verify — never partial. Moves (P3–P5) keep OLD namespaces so they compile independently of the rename. | P9 |
| R4 | **Silent test-coverage drop** (straddle tests of deleted classes E3/E4 dropped, not migrated) | 4 | 4 | **16** | Per-test migrate decision documented in P6/P7; only drop a test whose exact behavior is provably gone. `development-principles §Test Pass Gate` — count delta must be itemized, never silent. | P6,P7 |
| R5 | **Frozen-SSOT behavior regressed by a move/rename touching logic** | 3 | 5 | **15** | Moves carry files verbatim (namespace line only at P9). `TerrainBrushMathTests` + the 216 data/math tests are the contract — green before/after every phase. Any logic edit to a frozen type = STOP-and-ask. | all |
| R6 | `GpuTerrainRenderer`↔`WorldPainter.Render` consolidation breaks the KEPT `TerrainValidation.unity` scene (scene references the runtime component) | 3 | 4 | 12 | P8 ref-checks the scene before consolidating; keep `GpuTerrainRenderer` as scene-facing OR migrate scene with verified GUIDs. | P8 |
| R7 | Stale `packages-lock.json` silently drops test assemblies from discovery (89 vs 301) → false "all green" | 3 | 4 | 12 | Resolve packages before trusting any gate; assert the run reports the FULL 301 count, not a truncated set (predecessor handoff lesson). | every gate |
| R8 | Two-Unity-instance MCP trap routes compile/test to the wrong editor → phantom errors / 0-test runs | 3 | 3 | 9 | ALWAYS `set_active_instance("GrassInteract@de203215")`, verify path ends `/GrassInteract/Assets`, re-pin after every reload. | every gate |

**R1 (25) and R2/R3 (20) are high-risk → mitigated BEFORE their phases start** (R1 by the E1 escalation answer; R2 by git-mv discipline; R3 by the atomic-rename phase design).

---

## 6. Timeline

| Phase | Effort | Notes / blocker |
|---|---|---|
| P1 inventory map | M | deliverable only — no compile |
| P2 design + ref-check | S | blocked by P1 |
| P3 runtime move + asmdef | L | atomic; blocked by P2; long reload |
| P4 editor move + asmdef | M | atomic; blocked by P3 |
| P5 test move + asmdef + GATE | M | blocked by P4 |
| P6 delete old terrain authoring | M | blocked by P5; straddle-test migrate |
| P7 delete legacy scatter auth + demo | M | blocked by P6; E1 KEEP discipline |
| P8 consolidate + split | L | blocked by P7; SOLID/dedup |
| P9 namespace rename | L | atomic; blocked by P8; full recompile |
| P10 final verify + commit | S | blocked by P9 |
| **Total** | **≈ 3L + 4M + 2S ≈ 5L-equivalent** | Critical path = all phases linear (shared worktree + Unity compile-gate serialize moves/renames) |

---

## 7. Success criteria (objective, reproducible)

- [ ] **SC1** — Exactly 3 asmdefs exist (`WorldPainter`, `WorldPainter.Editor`, `WorldPainter.Tests`); the 6 old asmdefs are gone. (`find Assets -name '*.asmdef'`)
- [ ] **SC2** — Zero `namespace GpuTerrain`/`namespace GrassInteract` and zero `using GpuTerrain`/`using GrassInteract` remain. (`grep -rn`)
- [ ] **SC3** — `run_tests` reports the FULL expected count green (301 minus any itemized, user-confirmed obsolete straddle tests) — never a truncated discovery.
- [ ] **SC4** — All DELETE targets in §3.2 are gone; each had a recorded pre-delete ref check; no dangling reference compiles. (`read_console` clean)
- [ ] **SC5** — No file exceeds 200 lines without a documented responsibility justification (§3.4 splits done).
- [ ] **SC6** — A player build target compiles with zero authoring symbols pulled in (Editor asmdef `includePlatforms:[Editor]` verified; grep authoring types absent from runtime asm).
- [ ] **SC7** — `git log --stat` shows file moves as renames (R≈100%), proving `.meta` GUIDs preserved.

---

## 8. Rollback

Each phase is one commit. Any phase that fails its gate is reverted with `git revert <sha>` (or `git reset --hard` to the prior phase commit, working tree being the only consumer) without cascading — moves and renames are mechanical and self-contained per phase. P9 (rename) is the only phase that touches every file; if it fails, revert the single rename commit and the tree returns to the P8 state (compiling, green).

---

## 9. Cook handoff

`/t1k:cook plans/260611-2319-worldpainter-merge/plan.md --phase 1`

> **Before P6/P7 (the destructive phases), the cook MUST surface escalations E1–E5 (§0) to the user via `AskUserQuestion` and proceed on the recommended defaults only if confirmed.** The planning subagent could not ask directly.
