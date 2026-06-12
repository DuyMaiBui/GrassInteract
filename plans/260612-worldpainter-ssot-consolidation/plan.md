# Plan: WorldPainter SSOT Consolidation

**Created:** 2026-06-12 11:32
**Design source:** `plans/reports/260612-worldpainter-ssot-consolidation-design.md` (approved, all decisions settled)
**Branch base:** `plan/gpu-terrain-cdlod`
**Execution model:** phased, structured for **parallel `/t1k:cook` subagent fan-out** — each phase declares non-overlapping file-ownership groups; sequential dependencies between groups are explicit.

> All design decisions are SETTLED. This plan contains no open questions. Signed `Vector2Int` tile keys, `AddObjectToAsset` nesting, and the 1-texel shared-edge seam convention are used verbatim from the design.

---

## Goal (one line)

Make `WorldPainter` the sole renderer (terrain + scatter) reading one self-contained `WorldMapAsset` container (nested sub-assets), author tiles in the current scene with no scene switch, paint tile-agnostically in world space, and bake per-tile `TerrainTileAsset`s for runtime streaming — deleting `GpuTerrainRenderer`, `ScatterField`, `GpuTerrainScatterGround`, and the validation-scene builder.

---

## Critical sequencing reality

Two pieces are **foundational** — almost everything else depends on them:

1. **`WorldMapAsset` container type** (Phase 1) — the new SSOT. Scatter absorption, layer allocation, brush, palette, bake, and factory all read/write it.
2. **Scatter absorption into `WorldPainter.Scatter.cs`** (Phase 2) — must land before `ScatterField.cs` and `GpuTerrainScatterGround.cs` can be deleted.

The **deletes** (Phase 3) of `GpuTerrainRenderer` / `ScatterField` / `GpuTerrainScatterGround` / `TerrainValidationSceneBuilder` can only happen **after** their logic is absorbed (Phase 2) and their authoring-side consumers are repointed. Sequence accordingly — never delete before the seam is repointed.

---

## Phases

| Phase | Name | Scope | Effort | Depends on |
|-------|------|-------|--------|-----------|
| 1 | `WorldMapAsset` container (data SSOT) | New SO + nested sub-asset lifecycle + lookup API + per-tile channels/buckets | L | — |
| 2 | Scatter absorption + WorldPainter reads container | `WorldPainter.Scatter.cs` partial; WorldPainter reads tiles+layers from `WorldMapAsset` | L | P1 |
| 3 | Deletes + repoint seams | Remove dup renderers/bridge/validation-scene; repoint sculpt-tool + migration | M | P2 |
| 4 | In-scene tile creation | `WorldMapAssetFactory` + `WorldPainterNeighborGrowOverlay` ghost quads | M | P1, P3 |
| 5 | Layers (palette + per-tile channel alloc) | Activate allocates R8 channels on all tiles; 3-section palette + previews | L | P1, P2 |
| 6 | Tile-agnostic world-space brush + seam sync | Height/Splat/Density world-space; cross-tile seam sync; brush thumbnail strip + import | L | P1, P5 |
| 7 | Dual-mode prop placement | Scatter(brush)+Transform(gizmo) toggle; per-layer anchor; inspector preview + gizmos | L | P1, P5 |
| 8 | Bake → per-tile `TerrainTileAsset` + streaming | Bake step emits standalone tiles; `TerrainStreamingManager` streams baked tiles | M | P1, P2 |
| 9 | Fresh demo scene via Unity MCP + validation | Build new scene with MCP; validate terrain+grass+props render under WorldPainter alone | M | P1–P8 |

---

## Parallel fan-out map (which groups run concurrently)

```
WAVE A  (sequential foundation — no fan-out)
  P1  WorldMapAsset container  ─────────────┐
                                            │
WAVE B  (sequential foundation)             │
  P2  Scatter absorption  ◄─────────────────┘
                                            │
WAVE C  (P3 must precede P4's factory wiring; can overlap P5/P7/P8 prep)
  P3  Deletes + repoint  ◄──────────────────┘

WAVE D  (PARALLEL FAN-OUT — 4 concurrent cook subagents, non-overlapping files)
  ├─ P4  Factory + ghost-quad overlay        (Editor/Import + new overlay file)
  ├─ P5  Layer palette + channel alloc        (Editor/WorldPainter palette+card files + P1 alloc API)
  ├─ P7  Prop placement dual-mode             (Editor/WorldPainter prop files + Runtime prop buckets)
  └─ P8  Bake + streaming                      (Runtime/Terrain bake+streaming files)

WAVE E  (depends on P5 landing)
  P6  World-space brush + seam sync           (Editor/Brush files; needs palette active-layer API from P5)

WAVE F  (final, single — depends on all)
  P9  Fresh demo scene via MCP + validation
```

**Concurrency cap:** WAVE D fans out **4 subagents** (P4, P5, P7, P8). They share zero `.cs` files (ownership table below). P6 is held to WAVE E because it consumes the active-paint-layer API introduced by P5. P9 is terminal.

### Why these are safe to parallelize

- **P4** owns `Editor/Import/*` + one new overlay file. **P5** owns palette/card files under `Editor/WorldPainter/`. **P7** owns prop card/emitter files + `Runtime/Scatter/Instance*`. **P8** owns `Runtime/Terrain/TerrainStreaming*` + a new bake file. No file appears in two groups.
- The only shared seam is `WorldMapAsset` (P1, frozen before WAVE D) and `WorldPainter.Scatter.cs` (P2, frozen). Both are read-only consumers in WAVE D except where each group adds an isolated method to its own partial file.

### File-ownership conflict guard

`WorldPainter.cs`, `WorldPainter.Data.cs`, `WorldPainter.Render.cs`, `WorldPainter.Scatter.cs` are **edited only in P1/P2** and then **frozen** for WAVE D. If a WAVE-D group needs a new WorldPainter hook, it adds a **new partial file** (e.g. `WorldPainter.Bake.cs`) it solely owns — never edits the frozen partials. This is the hard rule that makes the fan-out race-free (see `parallel-teammate-git-index-race.md`).

---

## Critical path

```
P1 (L) → P2 (L) → P3 (M) → P5 (L) → P6 (L) → P9 (M)
```

P4, P7, P8 are **off the critical path** — they run concurrently inside WAVE D and finish before/with P5, so they do not extend wall-clock. Critical-path effort ≈ L+L+M+L+L+M.

---

## Risk Assessment (MANDATORY)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Orphan sub-assets from undisciplined remove (no `RemoveObjectFromAsset`+`DestroyImmediate`) | 4 | 4 | **16** | P1 ships a single `RemoveTile`/`RemoveLayer` API that is the ONLY remove path; EditMode test asserts asset DB has zero orphans after add→remove cycle. **Mitigation mandated before P1 done.** |
| Cross-tile seam desync (shared edge row/col not identical) | 4 | 4 | **16** | P6 reuses `TerrainWorldGrid` 1-texel shared-edge convention; EditMode test paints a stroke spanning two tiles and asserts the shared edge texels are byte-identical. **Mandated before P6 done.** |
| Deleting `ScatterField`/`GpuTerrainScatterGround` before Scatter absorption compiles → broken project | 3 | 5 | 15 | Hard phase ordering: P3 gated behind P2 green compile. Pre-delete reference grep in P3; never delete with live references. |
| WAVE-D subagents edit a shared WorldPainter partial → git index race / merge clobber | 3 | 4 | 12 | New-partial-file rule (each group owns a fresh `WorldPainter.*.cs`); frozen-partial guard; pathspec commits per `parallel-teammate-git-index-race.md`. |
| `AddObjectToAsset` net-new (no prior use) → wrong save/dirty/refresh ordering, silent data loss | 3 | 4 | 12 | P1 spike: smallest add→save→reload round-trip test first; assert sub-asset persists across `AssetDatabase.SaveAssets`+reimport before building the full API. |
| Single-asset multi-MB → slow editor / coarse git diffs | 3 | 2 | 6 | Accepted per design; bake (P8) is the runtime escape hatch. Note in docs; no action unless it bites. |
| MCP timeout mistaken for editor death during long bakes/reloads | 3 | 2 | 6 | Poll DLL mtime / read_console; never kill the editor (`unity-forbidden-operations.md`). |

**High-risk (≥15):** rows 1, 2, 3 — each has a mandated mitigation gating its phase's done state.

---

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| P1 WorldMapAsset container | L | Foundation; blocks everything. AddObjectToAsset spike first. |
| P2 Scatter absorption | L | Blocked by P1. Frees P3 deletes. |
| P3 Deletes + repoint | M | Blocked by P2 green compile. |
| P4 Factory + ghost quads | M | WAVE D — parallel. Blocked by P1+P3. |
| P5 Layer palette + alloc | L | WAVE D — parallel. Blocked by P1+P2. |
| P6 World-space brush + seam | L | WAVE E. Blocked by P5 (active-layer API). |
| P7 Prop placement | L | WAVE D — parallel. Blocked by P1+P5 layer defs. |
| P8 Bake + streaming | M | WAVE D — parallel. Blocked by P1+P2. |
| P9 Demo scene + validation | M | Terminal. Blocked by P1–P8. |
| **Total** | **~6L + 3M** | **Critical path: P1→P2→P3→P5→P6→P9** (P4/P7/P8 absorbed into WAVE D wall-clock). |

---

## Unity verification protocol (applies to every phase)

- **Compile = `read_console` (all errors) + `run_tests` (all failures) in ONE pass.** Never stop at the first error.
- **`refresh_unity` no-ops on asmdef-only edits** — touch a `.cs` in the assembly or `refresh_unity(force, all)` to force recompile.
- **Compile-gate via DLL mtime poll:** background-poll `Library/ScriptAssemblies/WorldPainter*.dll` mtime rather than burning a subagent's budget idling on `refresh_unity`.
- **MCP timeout ≠ bridge disconnect** — diagnose (process alive? workers busy?) before any escalation. **Never kill/restart the editor** (`unity-forbidden-operations.md`).
- **EditMode tests** live in `Assets/WorldPainter/Tests/Editor/`; each phase adds/extends the named tests in its phase card.

---

## Cook handoff

```
/t1k:cook plans/260612-worldpainter-ssot-consolidation/plan.md --parallel
```

Run WAVE A→B→C sequentially (P1→P2→P3), then fan out WAVE D (P4+P5+P7+P8 concurrent), then WAVE E (P6), then WAVE F (P9). Each subagent owns only its phase's file globs; new WorldPainter hooks go in a new owned partial, never the frozen P1/P2 partials.
