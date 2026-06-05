# Plan: Grass CPU-Bake Bend - Dumb Instanced Shader Refactor

Flip ALL grass motion (bend + wind) out of the GPU shader and into C#. The shader becomes a
DUMB instanced URP renderer: draw the blade mesh at the per-instance `Matrix4x4`, color by a
height gradient (`uv.y`). No trample RT, no per-vertex deform, no wind, no shader globals. This
deletes the entire shader-deform bug class (ShaderCache.db stale, R8 samples-0, sampler binding,
include-fold, magenta-poison, RenderGraph `matProps` drop) that cost ~6 prior sessions.

**Authoritative design:** `plans/reports/brainstorm-grass-cpu-bend-dumb-instance-20260602.md`
(all forks locked there via AskUserQuestion - there are NO open questions in this plan).

## Environment & verification model

- Unity 6, URP 17.3, **Mono (no DOTS/Burst)**.
- **NOT a git repo** -> there are **NO per-phase commits**. The verification gate for each phase is
  **in-editor** (compile clean via read_console, live render in Game + Scene view, edit + play)
  plus a final `unity-code-reviewer` pass. Revert for a phase means restoring the touched files
  from a manual pre-phase copy (see each phase Rollback note), not git checkout.
- Module root: `Assets/GrassInteract/` (`Runtime/`, `Editor/`, `Shaders/`, `Meshes/`, `Demo/`).
- Two asmdefs: `GrassInteract.asmdef` (Runtime), `GrassInteract.Editor.asmdef` (Editor).

## Locked design (no decisions to make)

1. **Bend = rigid lean about the blade base**, baked into the per-instance `Matrix4x4` (single
   rotation about base; NOT a per-vertex curve). Blade mesh pivot is already at y=0 (verified -
   `GrassBladeMeshBuilder.BLADE_HEIGHT`, verts run y=0..1), so **no base-offset bake is needed**.
2. **Wind = moved to C#** - per-blade sway in the same matrix. Escape hatch (DOCUMENT only, do
   NOT implement): a one-line `_Time` sway back in the dumb shader if mobile frame budget is tight;
   bend stays in C# regardless.
3. **Update strategy** = single per-frame pass over all blades; **early-out the bend math** for
   blades outside all interactor radii; `Vector2.MoveTowards` recovery toward upright when no
   interactor is in range. Wind term runs for ALL blades (perf ceiling - see escape hatch).
4. **LOD = global by camera distance** (whole field swaps to one mesh) under one field-wide bounds.
5. **Spatial chunks REMOVED** - flat instance list; only the API-mandated <=1023 render slabs remain
   (`InstanceBatchPool.MAX_INSTANCES_PER_BATCH = 1023`).
6. **Placement UNCHANGED** - density-map scatter via `GrassLayer` + `Editor/GrassPainterWindow.cs`;
   the seeded rng draw-order (localX, localZ, accept, yaw, scale) MUST stay byte-stable.

## Phases

- **Phase 0: Pre-delete reference sweep + baseline** - inventory all call-sites of the to-be-deleted
  types/globals; verify blade pivot at y=0; snapshot the current working render (tri/batch/blade
  counts) as a regression baseline. No code change. | Effort: S
- **Phase 1: Dumb shader rewrite** - rewrite `GrassInteractInstanced.shader` to a plain instanced
  URP shader (no deform include, no wind, no trample, no `_Grass*` globals; height-gradient color;
  forward+shadow+depth). Prove it compiles + renders STATIC instanced grass in Game+Scene / edit+play.
  Files owned: `Shaders/GrassInteractInstanced.shader`. | Effort: M
- **Phase 2: Flat scatter + single-LOD renderer** - `GrassScatter.cs` (replaces ChunkGrid -> flat
  `Matrix4x4[] baseMatrices` + `Vector3[] basePositions`, <=1023 slabs only, byte-stable seed);
  rework `GrassRenderer` to single global-LOD mesh + one field-wide bounds; wire `GrassInteractField`
  to render the flat set statically (drop shader-global binding). Files owned: `Runtime/GrassScatter.cs`
  (new), `Runtime/GrassRenderer.cs`, `Runtime/GrassInteractField.cs`, `Runtime/GrassFieldSpace.cs`,
  `Runtime/GrassLODConfig.cs`. | Effort: L
- **Phase 3: GrassBendSimulator (the heart)** - `GrassBendSimulator.cs`: owns base arrays + per-blade
  `bendState`/wind `phase`; per-frame pass writes `renderMatrices` slabs (zero per-frame GC); wind
  first (all blades), then interactor lean + `MoveTowards` recovery. `GrassInteractor` static registry.
  Verify a moving interactor leans blades AWAY + they recover - readable in C# (no GPU readback).
  Files owned: `Runtime/GrassBendSimulator.cs` (new), `Runtime/GrassInteractor.cs`,
  `Runtime/GrassInteractField.cs`. | Effort: L
- **Phase 4: Delete trample path + demo rebuild + README** - delete `GrassTrampleMap.cs`,
  `GrassChunk.cs`, `ChunkGrid.cs`, `GrassInteractDeform.hlsl`, `TrampleUpdate.shader` (+ `.meta`);
  remove GrassTrampleMap from the demo builder + rebuild the demo scene (no hand-YAML); README update
  describing the C# bend architecture + the wind-in-shader escape hatch. Files owned: the 5 deletes,
  `Editor/GrassInteractDemoBuilder.cs`, `Demo/GrassInteractDemo.unity` (via rebuild), `README.md`.
  | Effort: M
- **Phase 5: Review + final live verify** - `unity-code-reviewer` pass (0 Critical/High gate) +
  final live verification of all 5 success criteria. No new feature code. | Effort: S

## Feasibility

- **Reuse check:** `InstanceBatchPool` (1023-slab pooling, zero per-frame GC) is REUSED for both base
  and render slabs - no new pool. `GrassFieldSpace.WorldToUv` is REUSED for density placement. The
  blade mesh + `GrassBladeMeshBuilder` are REUSED unchanged. NEW code: `GrassScatter.cs`,
  `GrassBendSimulator.cs`. REWORK: renderer, field, interactor, config, shader, demo builder.
- **Complexity:** moderate. The hard part is the per-frame matrix-rebuild pass (Phase 3) and keeping
  placement byte-stable (Phase 2). Both are isolated and independently verifiable.

## Dependencies (critical path)

```
Phase 0 --> Phase 1 --> Phase 2 --> Phase 3 --> Phase 4 --> Phase 5
(sweep)    (shader)    (scatter)   (simulator)  (delete)    (review)
```

- **Phase 0** blocks all (the sweep gates the safe deletes in Phase 4; the baseline gates regression
  checks in Phases 2/3).
- **Phase 1** is independent of the C# motion path - it proves the render pipeline alone. Doing it
  first de-risks the visible-render gate (the historically expensive failure mode) before motion exists.
- **Phase 2** depends on Phase 1 (needs the dumb shader/material to render the flat set).
- **Phase 3** depends on Phase 2 (consumes baseMatrices/basePositions + writes renderMatrices).
- **Phase 4** depends on Phase 3 (new path proven working BEFORE deleting the old path).
- **Phase 5** depends on all.
- **No two phases modify the same file concurrently.** `GrassInteractField.cs` is touched in Phase 2
  AND Phase 3 - sequenced (2 before 3), never parallel. `GrassRenderer.cs` only in Phase 2.

## Timeline

| Phase | Effort | Notes / blocker |
|-------|--------|-----------------|
| Phase 0 - sweep + baseline | S | Blocks all; pure read/inventory, no code |
| Phase 1 - dumb shader | M | Blocked by 0; independent of motion path (de-risk render) |
| Phase 2 - flat scatter + renderer | L | Blocked by 1; byte-stable seed is the gate |
| Phase 3 - bend simulator | L | Blocked by 2; the heart; C#-readable verify |
| Phase 4 - delete + demo + README | M | Blocked by 3; never delete before replacement is live |
| Phase 5 - review + final verify | S | Blocked by all; 0 Critical/High gate |
| **Total** | **~M+L+L** | **Critical path: 0 -> 1 -> 2 -> 3 -> 4 -> 5 (fully serial)** |

The entire plan is a single serial chain - no parallelizable phase (each consumes the prior phase
output). Effort weight concentrates in Phases 2 and 3.

## Risk Assessment (plan-level; per-phase tables in each phase file)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Dumb shader still hits a ShaderCache/variant bug (magenta-poison) | 2 | 5 | 10 | Phase 1 isolates the shader FIRST with zero motion; verify compile + render before any C# motion exists. Keep Fallback Off + multi_compile_instancing only. |
| Placement drifts (seed draw-order changes) -> different field vs baseline | 3 | 4 | 12 | Phase 2 preserves the EXACT rng draw sequence (localX, localZ, accept, yaw, scale) from ChunkGrid; verify first-N matrices byte-equal vs Phase 0 baseline. |
| Per-frame matrix rebuild blows frame budget at 50k blades | 3 | 3 | 9 | Early-out bend math for out-of-range blades; skip recovery for upright blades; reuse persistent slabs (zero GC). Document 50k soft ceiling + wind-in-shader escape hatch. |
| Deleting old path before new path proven -> no working fallback | 2 | 5 | 10 | Phase 4 delete is gated AFTER Phase 3 verifies the new path live. Pre-phase file copies enable manual revert (no git). |
| Dangling reference to a deleted type breaks compile | 3 | 3 | 9 | Phase 0 sweep enumerates ALL referencing files; Phase 4 updates/deletes every one before the delete; read_console 0-errors gate. |
| Rigid lean reads visibly stiffer than per-vertex curve | 3 | 2 | 6 | Locked-known tradeoff. Optional scale-squash on heavy bend to fake compression; tune in Phase 3, not a blocker. |
| One field-wide bounds = no off-screen frustum culling | 2 | 2 | 4 | Accepted for a contained field; documented in renderer + README. |

No risk scores >= 15. The two highest (placement drift = 12, shader bug = 10) both have an
early-phase isolation + explicit verify gate as mitigation.

## Backwards compatibility

Internal refactor of a self-contained module with one demo; no external consumers. The change is
**breaking** for any saved scene referencing the deleted `GrassTrampleMap` component - handled by
rebuilding the demo scene in Phase 4 (the only such scene). `GrassLODConfig` gains a `recoveryRate`
float and repurposes wind/bend fields from shader-globals to C#-consumed tunables (same serialized
fields, same asset - additive). The `GrassInteractor` public surface
(`WorldPosition`/`Radius`/`Strength`) is preserved; only its registration target changes.

## Success criteria (final gate - verified in Phase 5)

1. Dumb shader compiles; immune to the ShaderCache/sampler/magenta bug class (no trample globals).
2. Grass renders in Game AND Scene view, edit + play (current working state preserved).
3. A moving `GrassInteractor` visibly leans blades away; they recover after it passes - verifiable
   by reading `bendState`/`renderMatrices` in C# (no GPU readback).
4. ~20k blades within demo frame budget; 50k documented as soft ceiling with the wind escape hatch.
5. `unity-code-reviewer`: 0 Critical/High before done.

## Completion (cook 2026-06-02) — ✅ ALL PHASES DONE + LIVE-VERIFIED

All 6 phases executed against live editor `GrassInteract@de203215`. Not a git repo — no commits; per-phase `_backup/` copies were the safety net.

- **Phase 0** — sweep + baseline captured (`baseline.md`): 2074 demo blades, translation/scale checksums, pivot-at-y=0 confirmed.
- **Phase 1** — dumb shader rewrite. Verified: console clean, **20401 tris** in play, material bound to `GrassInteract/InstancedGrass` (`isErrorShader=False`) — no magenta.
- **Phase 2** — `GrassScatter` (flat) + single-LOD `GrassRenderer`. **Byte-stable placement matched baseline exactly** (2074 / tx 895.935021 / ty 0 / tz -792.635855 / scaleSum 2067.991800).
- **Phase 3** — `GrassBendSimulator` (the heart). Criterion-3 proven in C# (no GPU readback): isolated blade bendState **0.5250 → 0.0000** (lean away then decay); registry hygiene confirmed.
- **Phase 4** — deleted `GrassTrampleMap`/`GrassChunk`/`ChunkGrid`/`GrassInteractDeform.hlsl`/`TrampleUpdate.shader`; rebuilt demo (0 stale GameObjects, 0 missing scripts, field wired); README rewritten (escape hatch + 50k ceiling). Sweep → **0 hits**.
- **Phase 5** — `unity-code-reviewer`: **GATE PASS (0 Critical / 0 High)**. 3 Medium findings cleared: M1 `flatten` wired as a base-anchored Y-squash (verified Y-scale 0.9153 → 0.5263 under trample); M2 `chunkSize`/`MAX_CHUNKS` vestige removed; M3 chunk-AABB doc comments reworded.

**Final live verification (play):** 20209 tris / 13 batches; the orbiting effector bends 33 blades (maxBend 0.5868) in real time, all read from the live simulator's `bendState` — zero GPU readback. All 5 success criteria met.
