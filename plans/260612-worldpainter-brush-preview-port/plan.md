# Plan: Port MegaWorld brush-preview technique into WorldPainter + full square brush

Date: 2026-06-12 17:13
Mode: --auto (default)
Source of truth: `plans/reports/260612-worldpainter-brush-preview-port.md` (design APPROVED, decisions LOCKED)

## Goal

Replace WorldPainter's editor sculpt-brush cursor (tessellated disc mesh + `BrushDecal`
shader + procedural disc texture + `Graphics.DrawMeshNow`) with the MegaWorld pure-Handles
technique (outline ring double-stroke + faint convex-poly fill, conformed to terrain via the
existing CPU `HeightFn`), AND add a **full** square brush whose GPU stamp mask matches the
preview shape (not preview-only).

Locked decisions (do NOT re-open — see report §"Decisions"):
- Preview = pure Handles. Ring = `Handles.DrawAAPolyLine` drawn twice (black ~8px then color
  ~4px), `Handles.zTest = Always`. Fill = `Handles.DrawAAConvexPolygon` over the same conformed
  perimeter at low alpha, `zTest = Always`.
- REPLACE `TerrainBrushPreview` entirely. DELETE `Shaders/BrushDecal.shader` + `.meta`; drop the
  procedural disc texture, the tessellated disc mesh, and the `Graphics.DrawMeshNow` path.
- Shapes: Circle + Square. Square is a FULL brush — GPU stamp mask matches preview.
- Conform perimeter Y via WorldPainter's existing `HeightFn` (CPU heightmap sampler), NOT
  `Physics.Raycast` (GPU/CDLOD terrain has no collider).
- Square distance metric = Chebyshev `max(|du.x|,|du.y|)` before the falloff-LUT sample; circle
  stays Euclidean `length()`. Same falloff curve, square iso-contours.

## Phases

- **Phase 1: Brush shape SSOT** — `BrushShape` enum + `shape` field on `BrushSettings`; Circle/Square
  UI toggle in `WorldPainterBrushDock`. Owns: `WorldPainterState.cs`, `WorldPainterBrushDock.cs`.
  Effort: **S**
- **Phase 2: Square stamp CPU/GPU parity** — `_BrushShape` uniform in `BrushMask.hlsl` (Chebyshev
  branch) + declare in `TerrainBrush.compute` + bind every dispatch in `Kernels.cs` AND
  `WorldPainterBiomeStamp.cs`; CPU Chebyshev mirror + square parity tests in
  `TerrainBrushMathTests.cs`. Owns: `BrushMask.hlsl`, `TerrainBrush.compute`,
  `WorldPainterSculptTool.Kernels.cs`, `WorldPainterBiomeStamp.cs`, `TerrainBrushMathTests.cs`.
  Effort: **M**
- **Phase 3: Preview rewrite** — `TerrainBrushPreview.cs` → Handles ring + convex-poly fill,
  circle + square, conform via `HeightFn`, keep lift/freshness/finite guards; update the `Set`
  call site to pass `brush.shape`. Owns: `TerrainBrushPreview.cs`, `WorldPainterSculptTool.cs`
  (call site only). Effort: **M**
- **Phase 4: Cleanup** — delete `BrushDecal.shader` + `.meta`; grep-prove zero dangling
  references; final compile + full `TerrainBrushMathTests` run. Owns: `Shaders/BrushDecal.shader`
  (+`.meta`) deletion. Effort: **S**

## Feasibility

- **Reuse check:** Reuses existing `HeightFn` (`s_heightFn` / `SampleActivePainterHeight`), the
  existing `BrushSettings` SSOT, the existing `BrushMask.hlsl` weight path, and the existing
  `TerrainPaintTargetResolver` AABB tile/undo resolution (square half-extent = radius → same AABB
  as circle, no resolver/undo change). NEW code: `BrushShape` enum, `_BrushShape` uniform +
  Chebyshev branch, Handles render path. No new shader, no new texture, no new mesh.
- **Complexity:** Moderate. The CPU↔GPU parity invariant (Phase 2) is the one true-risk surface;
  it is isolated in its own phase with a test gate so a parity failure cannot be masked by the
  Phase 3 visual change.

## Dependencies

```
Phase 1 (SSOT)  ──►  Phase 2 (parity)  ──►  Phase 3 (preview)  ──►  Phase 4 (cleanup)
```

- Phase 2 blocked by Phase 1 (needs `BrushShape` enum + `shape` field to read `(int)brush.shape`).
- Phase 3 blocked by Phase 1 (reads `brush.shape` at the `Set` call site) and ordered after
  Phase 2 so a parity regression is caught before the visual rewrite (the report mandates keeping
  parity isolated from the preview change).
- Phase 4 blocked by Phase 3 (only after the Handles rewrite removes the last `BrushDecal`
  reference can the shader be deleted with zero dangling refs).
- All phases are sequential. File ownership is disjoint per phase except `WorldPainterSculptTool.cs`
  is touched only at the `Set` call site in Phase 3 — no concurrent edits.

## Backwards compatibility

Additive + behaviour-preserving for the existing circle path. `shape` defaults to
`BrushShape.Circle`, so a serialized `BrushSettings` with no `shape` deserializes to Circle and
behaves exactly as today. `_BrushShape` defaults to 0 (Circle) when unset, and Phase 2 sets it on
EVERY dispatch (sculpt + biome) so it can never leak a stale Square across paths. No migration
needed. The `TerrainBrushPreview.Set` signature gains one trailing `shape` arg — the single caller
is updated in the same phase.

## Risk Assessment (MANDATORY)

| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| CPU/GPU square-distance parity drift (`BrushMask.hlsl` Chebyshev vs CPU test) | 3 | 4 | 12 | Phase 2 adds CPU Chebyshev mirror + square parity tests; gate = `TerrainBrushMathTests` green for circle AND square BEFORE Phase 3 starts. |
| `_BrushShape` leaks a stale Square across dispatch paths (biome shares `brushCompute`) | 3 | 3 | 9 | Set `SetInt("_BrushShape", …)` on EVERY dispatch in BOTH `Kernels.cs` (`BindAndDispatch`) and `WorldPainterBiomeStamp.Stamp` — never rely on prior state. |
| Deleting `BrushDecal.shader` leaves a dangling reference | 2 | 3 | 6 | Phase 3 removes the last referencer (`TerrainBrushPreview` rewrite) FIRST; Phase 4 greps `BrushDecal`/`WorldPainter/BrushDecal` across `Assets/` to prove zero hits before delete; final compile confirms. |
| Convex-poly fill flat between perimeter points on bumpy/large brush | 2 | 2 | 4 | Accepted tradeoff (report §Design A): ring conforms point-by-point, fill is faint, `zTest=Always` ⇒ no clipping. Adaptive segment count (16–128) keeps perimeter dense. |
| Square preview ring corners not matching the GPU-affected square region | 2 | 3 | 6 | Square ring sampled along 4 OBB edges with half-extent = radius (same value `WorldBrushToTileUV` feeds the mask); Phase 3 verify = cursor square == affected region. |
| Invalid hover point (fallback-plane pick) feeds NaN/huge verts into Handles | 2 | 2 | 4 | Preserve existing finite/`MAX_HIT_SQR`/`brushRadius>0` guards from the old impl; skip draw on failure. |

**No risk scores ≥ 15** — no pre-phase mandated mitigation block; the parity gate (score 12) is
nonetheless enforced as a hard verify gate before Phase 3.

## Timeline

| Phase | Effort | Notes |
|-------|--------|-------|
| Phase 1: Brush shape SSOT | S | No dependency; enum + field + UI toggle. |
| Phase 2: Square stamp CPU/GPU parity | M | Blocked by Phase 1. Hard gate: `TerrainBrushMathTests` green (circle + square). |
| Phase 3: Preview rewrite | M | Blocked by Phase 1 + Phase 2 (parity isolated first). |
| Phase 4: Cleanup | S | Blocked by Phase 3. Delete shader + grep + final compile/test. |
| **Total** | **~M+** | Critical path: Phase 1 → Phase 2 → Phase 3 → Phase 4 (fully sequential). |

## Verification strategy (per phase)

1. **Phase 1** — compiles clean; Circle/Square toggle visible in brush dock and round-trips
   `brush.shape`.
2. **Phase 2** — `TerrainBrushMathTests` green for BOTH circle and square (CPU↔GPU parity
   invariant); compute compiles with no shader errors in console.
3. **Phase 3** — compiles clean; circle cursor conforms to terrain (no float/clip) at varied zoom
   and size; square cursor matches the square edited region.
4. **Phase 4** — `grep -rn "BrushDecal"` across `Assets/` returns zero hits; final compile clean;
   full `TerrainBrushMathTests` run green.

---

## Cook handoff

`/t1k:cook plans/260612-worldpainter-brush-preview-port/plan.md`
