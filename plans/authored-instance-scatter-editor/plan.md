---
plan: authored-instance-scatter-editor
created: 2026-06-04 18:48
owner: t1k-unity-developer (single agent, sequential phases)
brainstorm: plans/reports/brainstorm-authored-instance-editor-20260604.md
status: ready-to-cook
---

# Plan: Authored Per-Instance Scatter Editor

Replace procedural TargetInstances scatter with Terrain-Detail-tool-style authored placement: density map = placement mask, click/brush paints individual instances, click-pick enters per-instance focused-edit mode with transform/collider/renderer overrides, brush-edit ops batch-tweak inside radius. Engine keeps GPU RenderMeshIndirect path.

## Locked Assumptions (defaults; confirm-or-correct on first cook)

| # | Question | Locked default | Source |
|---|---|---|---|
| Q1 | targetInstances field removal | [Obsolete] + [FormerlySerializedAs(targetInstances)] for one release cycle; hard-delete in follow-up plan | User hint: demo asset serialized data may still carry the field |
| Q2 | Place-brush spacing source | Per-layer config field ScatterLayer.PlaceSpacing (default 0.5 m) | User hint: Detail-tool style suggests per-layer |
| Q3 | Renderer-override warning threshold | 10 percent of authored instances overridden -> inspector warning | Brainstorm Risks section |
| Q4 | Bake-to-Authored seed handling | One-shot freeze at current seed; re-invoke = overwrite with confirm dialog | User hint: one-shot freeze is the obvious answer |

If any of Q1-Q4 is wrong, correct before P5 (Q1, Q4) / P4 (Q3) / P1 (Q2) begins. They are isolated to those phases.

## Phases (sequential, no parallelism)

| # | Phase | Effort | Owns |
|---|---|---|---|
| P1 | Editor scaffolding (toolbar, sidecar SO, spatial hash, Place+Erase) | M | Editor toolbar, AuthoredInstancesData, InstancePickingService |
| P2 | Edit Single (pick, wireframe, gizmo, focused inspector w/ overrides) | M | InstanceSelectionOverlay, override UI |
| P3 | Edit Brush (rot/scale/pos/align-normal ops with falloff) | S | Brush-edit ops, op selector |
| P4 | Engine integration (skip-path, override-mask bit, group-by-material draw split) | L | GrassScatter, ChunkedInstanceBuffer, MeshScatterEngine |
| P5 | Migration (Bake-to-Authored menu) + targetInstances deprecation | S | ScatterBakeToAuthored, demo asset migration |

**Critical path:** P1 -> P2 -> P3 -> P4 -> P5. No phase parallelizable: single live Unity editor, byte-stable verification gates between phases, shared working tree (no git in this repo).

## File Ownership

| File | P1 | P2 | P3 | P4 | P5 |
|---|---|---|---|---|---|
| Runtime/AuthoredInstancesData.cs (NEW) | CREATE | extend | extend | read | -- |
| Runtime/ScatterLayer.cs | edit (flag + ref + PlaceSpacing) | -- | -- | -- | edit (deprecate field, Validate accepts authored) |
| Runtime/GrassScatter.cs | -- | -- | -- | edit (skip-path) | -- |
| Runtime/ChunkedInstanceBuffer.cs | -- | -- | -- | edit (override-mask bit) | -- |
| Runtime/MeshScatterEngine.cs | -- | -- | -- | edit (group-by-material draw split) | -- |
| Editor/TerrainScatterConfigEditor.cs | edit (5-mode toolbar) | edit (focused panel) | edit (op selector) | -- | -- |
| Editor/ScatterBrush.cs | edit (Place->append; Erase->remove) | -- | edit (brush-edit ops) | -- | -- |
| Editor/InstancePickingService.cs (NEW) | CREATE (spatial hash) | extend (ray-vs-sphere) | read | -- | -- |
| Editor/InstanceSelectionOverlay.cs (NEW) | -- | CREATE | -- | -- | -- |
| Editor/ScatterBakeToAuthored.cs (NEW) | -- | -- | -- | -- | CREATE |
| Editor/ScatterInstanceCullHarness.cs (existing) | gate | gate | gate | gate (still PASS) | gate |
| Editor/ChunkInstanceLayoutVerify.cs (NEW) | -- | -- | -- | CREATE | gate |

No two phases mutate the same file in overlapping turns.

## Dependencies

- **Blocks:** future per-instance-LOD work (out of scope), animated-instance work.
- **Blocked by:** nothing. Brainstorm is design-approved.
- **External constraints:** Unity MCP bridge live; editor remains user-owned (never kill per unity-forbidden-operations.md).

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| P4 byte-layout change breaks ScatterInstanceCullHarness | 4 | 5 | **20** | NEW ChunkInstanceLayoutVerify harness asserts byte-stability when overrideMask=0; per-bit unit test in P4 |
| Group-by-material draw split regresses single-material perf | 3 | 4 | 12 | Fast-path: all instances share layer-default material -> skip grouping (one RenderMeshIndirect, unchanged) |
| Sidecar byte blob 5 MB undo causes Editor stall at 100k inst | 2 | 3 | 6 | Brainstorm-accepted (desktop-fine); defer per-cell delta undo until profiling demands |
| Density map dual-SSOT confusion (mask vs count-multiplier) | 3 | 3 | 9 | Inspector tooltip + ScatterLayer.PlaceSpacing naming; one-line doc on HasAuthoredInstances |
| Demo asset migration loses procedural look | 3 | 4 | 12 | P5 bake reuses GrassScatter.Build once -> identical visual; screenshot diff gate in P5 |
| FormerlySerializedAs chain on targetInstances breaks | 1 | 3 | 3 | Exact-match string; Validate-on-load in P5 |
| Spatial hash invalidates on every authored edit | 2 | 3 | 6 | Rebuild hash only on stroke-end (mouse-up), not per-stamp; InstancePickingService.Invalidate API |
| Editor-only code leaks into runtime asmdef | 2 | 4 | 8 | All NEW editor files under Editor/; P1 gate verifies asmdef compile in both Editor + Player |
| Live Unity editor stalls during phase verification | 3 | 2 | 6 | Follow unity-forbidden-operations.md diagnosis tree; wait, never kill |
| Place-brush spacing too dense -> OOM during stroke | 2 | 4 | 8 | Hard cap MAX_INSTANCES_PER_STAMP=10000; warn + truncate above |

**Highest risk = P4 byte-layout (score 20).** Mitigation mandatory: ChunkInstanceLayoutVerify harness MUST be the FIRST task of P4, asserting overrideMask=0 produces byte-identical output to procedural baseline captured at P4-start.

## Timeline

| Phase | Effort | Notes |
|---|---|---|
| P1 | M (1-2 days) | Sidecar SO + spatial hash + Place/Erase brush rewire |
| P2 | M (1-2 days) | Picking + overlay + focused inspector UI |
| P3 | S (~1 day) | Op selector + falloff math on existing brush walk |
| P4 | L (~2 days) | Engine skip-path + schema bit + draw-split + NEW byte-stability harness |
| P5 | S (~0.5 day) | Bake menu + Validate update + demo migration + deprecation |
| **Total** | **~6-7 days** | Critical path: all phases serial |

## Verification Gate (every phase)

After each phase ships:

1. mcp__UnityMCP__read_console: clean compile (zero errors, zero NEW warnings beyond pre-existing).
2. ScatterInstanceCullHarness: PASS (existing harness; gates GPU render path intact).
3. ChunkInstanceLayoutVerify: PASS (from P4 onward; byte-stability gate).
4. Screenshot of demo scene saved to plans/authored-instance-scatter-editor/screenshots/phase-N.png via Unity MCP screenshot_editor.
5. Phase exit report in plans/authored-instance-scatter-editor/phase-N-report.md explicitly stating: what compiles, what harnesses ran, what visual was verified.

Gate failure = phase does not ship; loop back to the phase-N teammate with the failure evidence per agent-completion-discipline.md.

## Success Metrics (from brainstorm)

| Metric | Target | Verified in |
|---|---|---|
| Place-brush throughput | >= 5000 inst/sec stamp | P1 |
| Single-instance pick latency | < 16 ms @ 100k inst | P2 |
| Brush-edit (rotate 1000 inst) | < 50 ms / stroke | P3 |
| Sidecar size | <= 6 MB @ 100k inst | P1 (storage) + P5 (demo migration) |
| Undo step memory | <= 6 MB / stroke @ 100k inst | P1, P3 |
| Demo migrates cleanly | Visual parity | P5 screenshot diff |
| GPU render path unchanged | ScatterInstanceCullHarness PASS | every phase |

## Execution Constraints (read before cook)

- **Single-agent sequential**: t1k-unity-developer per phase. NO --team. Shared working tree (no git) + single live Unity editor + byte-stable gates make parallelism unsafe.
- **No Unity restarts**: never kill, pkill -f Unity, or File/Quit; never Assets/Reimport All. Use refresh_unity(mode=force, scope=scripts) after script edits. Per unity-forbidden-operations.md.
- **Editor-only code in GrassInteract.Editor.asmdef**; runtime code in main asmdef. P1 verifies asmdef boundary.
- **Undo discipline**: per-stroke Undo.RegisterCompleteObjectUndo(sidecar, ...) on mouse-down; per-gizmo-drag Undo.RecordObject on drag-end. Called out in P1 and P2.
- **Mono path, not DOTS**. Pool rules do not apply (editor-only authoring code).
- **Code conventions**: camelCase private fields (no underscore), mandatory this. prefix per code-conventions-unity.md.

## Out of Scope (deferred)

- Per-instance LOD override.
- Per-cell delta undo at >500k instances.
- Animated / streaming instance overrides.
- Multi-layer brush.
- targetInstances hard-deletion (cycle 2, separate plan).

## Handoff

/t1k:cook plans/authored-instance-scatter-editor/

