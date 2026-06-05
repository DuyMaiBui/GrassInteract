# Plan: GrassInteract - GPU-Driven Indirect Rendering for Mobile

Generated 2026-06-03 1058. Source: plans/reports/brainstorm-grass-gpu-driven-indirect-mobile-20260603.md (LOCKED design).
Project: GrassInteract. Unity 6, URP 17.3, Mono - NO DOTS/Burst. git: false.

## Goal

Scale interactive grass from the current ~50k CPU-driven ceiling to 100k-250k blades on mobile by moving culling + transform + deform onto the GPU (compute cull -> RenderMeshIndirect x 3 LODs, deform in the vertex shader), while keeping the existing CPU path verbatim as a GLES3.0 fallback tier behind a new IGrassEngine seam.

## Architecture (one facade, two tiers)

GrassInteractField stays the only public component. At OnEnable it probes the device and selects an engine:

    bool gpuOk = SystemInfo.supportsComputeShaders
              && SystemInfo.supportsIndirectArgumentBuffers
              && SystemInfo.maxComputeBufferInputsVertex > 0;   // VS StructuredBuffer reads

- High tier (gpuOk, not force-CPU) -> GrassGpuEngine: bake once -> per frame Compute A (chunk cull) -> Compute B (blade cull + LOD bucket) -> CopyCount -> RenderMeshIndirect x 3 LODs; deform in VS.
- Low tier (else / debug force-CPU) -> GrassCpuEngine wrapping the existing GrassBendSimulator + GrassRenderer, byte-for-byte unchanged. ~50k ceiling.

## Phase index

| Phase | Name | Scope (owned files) | Effort |
|---|---|---|---|
| 1 | IGrassEngine seam + GrassCpuEngine | extract CPU path behind interface; facade delegates. Pure refactor, zero behavior change. | M |
| 2 | ChunkedBladeBuffer baker | grid-sort blades -> BladeInstance[] + per-chunk AABB + chunk->range table; upload to GraphicsBuffers. Edit-bakeable. | M |
| 3 | Compute A - chunk cull kernel | AABB vs frustum+distance, Append visible chunk IDs + write DispatchIndirect args for B. Isolated harness FIRST. | M |
| 4 | Compute B - blade cull + LOD bucket | per-blade frustum + distance->LOD, per-LOD AppendBuffers, CopyCount->indirect draw args. | M |
| 5 | Indirect shader + VS GPU deform | RenderMeshIndirect shader variant; VS reads BladeInstance via visible-index buffer, rebuilds transform, wind + lean-away. 3 LOD meshes. | L |
| 6 | GPU interactor upload | GrassInteractor.Active -> per-frame StructuredBuffer of Interactor (<=16). | S |
| 7 | Device smoke test + tier selection | wire probe + debug force-CPU; GATE: real GLES3.1 Android hardware smoke (highest risk). | M |
| 8 | Edit-mode render parity | re-prove beginCameraRendering Scene-view discipline for the indirect path. | S |
| 9 | unity-code-reviewer gate | 0 Critical/High before done. | S |

Critical path: 1 -> 2 -> 3 -> 4 -> 5 -> 7. Phases 6 and 8 are parallel-safe after their dependency lands (6 after 5; 8 after 5+7). Phase 9 is terminal.

## Feasibility

- Reuse check: REUSE as-is - GrassScatter/GrassScatterResult (placement + base TRS + base positions), GrassLayer (density paint), GrassPainterWindow, GrassInteractor (registry + Active), GrassFieldSpace, InstanceBatchPool, GrassBendSimulator + GrassRenderer (become the low tier verbatim). GrassLODConfig gains fields only (chunkSize, lodCount), no removals. NEW: IGrassEngine, GrassCpuEngine, GrassGpuEngine, GrassCull.compute, ChunkedBladeBuffer, indirect shader variant, GrassInteractorBuffer upload.
- Complexity: complex - GPU-driven indirect with append/CopyCount/counter-reset discipline + a real-hardware GLES gate. De-risked by phasing: Phase 1 isolates the seam (no behavior change), Phase 3 isolates the append/CopyCount primitives in a harness before the full pipeline, Phase 7 front-loads the hardware gate.
- allowUnsafeCode: false in GrassInteract.asmdef - keep it. GraphicsBuffer.SetData(T[]) with blittable structs needs no unsafe.

## Dependencies (cross-phase)

- Phase 1 blocks all others (the seam is the integration point). Blocked by: nothing.
- Phase 2 blocked by 1. Blocks 3,4,5 (they consume the baked buffers).
- Phase 3 blocked by 2. Blocks 4 (B DispatchIndirect args come from A).
- Phase 4 blocked by 3. Blocks 5 (indirect draw args + per-LOD visible-index buffers).
- Phase 5 blocked by 4. Blocks 6, 8 (deform + the live indirect draw to verify against).
- Phase 6 blocked by 5. Parallel-safe with 8.
- Phase 7 blocked by 5 (needs a runnable high tier); SHOULD run before declaring high tier shippable. Blocks the high-tier ship decision.
- Phase 8 blocked by 5 (and ideally 7 for the final tier). Parallel-safe with 6.
- Phase 9 blocked by all of 1-8.

## Cross-phase Risk Assessment (MANDATORY)

| # | Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|---|------|:---:|:---:|:---:|------------|
| R1 | A device reports supportsIndirectArgumentBuffers=true but RenderMeshIndirect/VS-StructuredBuffer fails on real GLES3.1 hardware -> high tier renders nothing on those units | 4 | 4 | 16 | Phase 7 is a hardware GATE, front-loaded. Tier probe + a runtime self-test draw (render to a 1x1 RT, read back a known pixel) that demotes to CPU on failure. Debug force-CPU override proves the fallback path independently. See Phase 7. |
| R2 | Append-buffer counter not reset / CopyCount ordering wrong -> stale or doubled visible counts, flicker or over/under-draw | 4 | 3 | 12 | Phase 3 isolates Append/CopyCount/counter-reset in a tiny harness kernel BEFORE the full pipeline. SetCounterValue(0) each frame before the cull dispatch; CopyCount only after the dispatch completes. Documented counter-reset discipline in Phase 3 + 4. |
| R3 | DispatchIndirect args (A->B) mis-sized or not zeroed each frame -> B dispatches 0 or garbage groups | 3 | 3 | 9 | A writes B [threadGroupsX,1,1] args explicitly each frame; args buffer created with Target.IndirectArguments; zero-init before A. Verified in the Phase 3 harness. |
| R4 | Phase 1 refactor silently changes the CPU render (regression) | 2 | 4 | 8 | Phase 1 is a pure extract - GrassCpuEngine calls the SAME GrassBendSimulator/GrassRenderer instances with identical args. Gate: side-by-side Scene + Game view screenshot parity vs pre-refactor; tri count identical via UnityStats.triangles. |
| R5 | Edit-mode Scene view renders nothing for the indirect path (the CPU path hard-won beginCameraRendering lesson) | 3 | 3 | 9 | Phase 8 re-proves the discipline: drive RenderMeshIndirect from beginCameraRendering per-camera in edit mode (NOT EditorApplication.update), null camera in play mode. Live Scene-view evidence required. |
| R6 | GLES mobile bandwidth blow-up from per-frame buffer traffic | 2 | 3 | 6 | BladeInstance uploaded ONCE (~5MB@250k). Per-frame: only <=16 interactors + visible-index uint buffers (GPU-resident, never read back). No GPU->CPU readback in the steady state. GraphicsBuffer target flags audited per phase. |
| R7 | LOD2 billboard-or-skip math wrong -> far blades pop or vanish | 2 | 2 | 4 | LOD2 defaults to camera-facing billboard; skip is a config toggle (lodCount field). Distance bucketing in Compute B uses the same LodMaxDistances thresholds the CPU path already uses. |
| R8 | Two live Unity instances -> verification runs against the wrong editor | 3 | 2 | 6 | EVERY MCP verification step calls set_active_instance GrassInteract FIRST. rendering_stats is broken -> use UnityStats.triangles via execute_code in Play mode. Noted in every phase gate. |

R1 score 16 >= 15 = HIGH RISK -> its mitigation (Phase 7 hardware gate + runtime self-test demote-to-CPU) is mandatory and front-loaded; the high tier is NOT declared shippable until R1 gate passes on real hardware.

## Backwards compatibility

- Additive + behavior-preserving. GrassInteractField public surface is unchanged (same serialized fields + Rebuild() / RebuildFromMenu()). Placement/paint/interactor workflows untouched.
- GrassLODConfig change is additive only (new chunkSize + lodCount serialized fields with sane defaults) - existing assets deserialize unchanged.
- Low tier = the exact current CPU path. A device that fails the probe (or force-CPU) gets identical behavior to today. No migration step for existing scenes.

## Rollback plan (per phase, no git)

Project is not a git repo - rollback = file-state revert, not git revert. Each phase new files are additive; the facade keeps a single integration switch:
- Phases 2-6: the new GPU files are inert until the facade selects the high tier. Setting the debug force-CPU override (Phase 7 wires it; until then the facade can hard-default to CPU) reverts to the verified low tier instantly without deleting code.
- Phase 1: revert = restore the pre-refactor GrassInteractField (the only file whose behavior changed); the extracted GrassCpuEngine is dead until referenced.
- Each phase file lists its exact owned files so a revert deletes/restores a known set.

## Test matrix (one measurable gate per phase)

| Phase | Pass/fail gate (live-editor evidence, NOT should-work) |
|---|---|
| 1 | Scene+Game view screenshots identical to pre-refactor; UnityStats.triangles equal pre/post; console clean. |
| 2 | Edit-mode bake produces BladeInstance count == GrassScatterResult.TotalCount; chunk AABBs union covers field bounds; chunk ranges partition [0,total) with no gaps/overlaps (an editor assert dumps the table). |
| 3 | Harness kernel: known N input chunks, frustum culls expected M; readback of the append count == M; DispatchIndirect args == ceil(M/groupSize); counter resets to 0 across two consecutive frames. |
| 4 | Per-LOD visible counts sum == total visible blades from a known camera; moving the camera changes counts monotonically; CopyCount-fed draw args readback matches. |
| 5 | High tier renders the field in Play; UnityStats.triangles scales with visible LOD distribution; wind animates + a moving GrassInteractor visibly leans blades GPU-side; main-thread grass cost ~0 (profiler). |
| 6 | Moving an interactor in Play leans the correct footprint; <=16-interactor cap enforced (17th logged + dropped, no NRE). |
| 7 | Probe selects high tier on a capable editor; force-CPU override flips to low tier (verified by a tier-readout). HARDWARE: a real GLES3.1 Android build renders the high tier OR the runtime self-test demotes it to CPU cleanly (no black/empty field). |
| 8 | Scene view (edit mode) renders the indirect high tier with correct colors (not black, not empty) from a fresh domain reload. |
| 9 | unity-code-reviewer report: 0 Critical, 0 High. |

## Timeline

| Phase | Effort | Notes / dep |
|---|---|---|
| 1 | M | blocks everything; de-risks the rest |
| 2 | M | after 1 |
| 3 | M | after 2; harness-first |
| 4 | M | after 3 |
| 5 | L | after 4; largest (shader + VS deform + 3 LODs) |
| 6 | S | after 5; parallel with 8 |
| 7 | M | after 5; HIGH-RISK hardware gate (R1=16) - front-load mitigation |
| 8 | S | after 5(+7); parallel with 6 |
| 9 | S | terminal |
| Total | ~5L-equivalent | Critical path: 1->2->3->4->5->7->9. Phase 5 + Phase 7 are the schedule risks. |

## Conventions (MANDATORY - apply every phase)

- C#: .claude/rules/code-conventions-unity.md - camelCase private fields (NO underscore), this. prefix always, [SerializeField] private, UPPER_SNAKE_CASE constants, #nullable enable. Mirror the existing GrassInteract files style exactly.
- Skills to consult: t1k-unity-base-code-conventions, t1k-unity-base-game-patterns (mobile-optimization ref), t1k-unity-base-mcp-skill (verification gates).
- GraphicsBuffer flags audited per phase: Structured (BladeInstance, ChunkAABB, interactors, visible-index), Append (per-LOD visible-index, visible-chunk), IndirectArguments (DispatchIndirect + RenderMeshIndirect args), Counter discipline (SetCounterValue(0) before each cull dispatch).
- GLES3.1 VS-StructuredBuffer support is probed, not assumed (Phase 7 R1).
- MCP verification: set_active_instance GrassInteract FIRST every time; rendering_stats is BROKEN -> UnityEditor.UnityStats.triangles via execute_code in Play mode.
- git: false -> per-phase gates are file-state + live-editor evidence, not commits.

---

## Cook handoff

/t1k:cook plans/grass-gpu-driven-indirect/plan.md
