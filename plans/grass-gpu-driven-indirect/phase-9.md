# Phase 9 - unity-code-reviewer gate (0 Critical / 0 High)

Effort: S. Depends on: ALL of Phase 1-8. Terminal phase - the ship gate.
Goal: a full code review of the new + modified surface by the unity-code-reviewer agent; zero Critical and zero High findings before the feature is declared done. Per development-principles: zero failures before done.

## Scope - review surface (all files this plan created or changed)

NEW:
- Runtime/IGrassEngine.cs, Runtime/GrassCpuEngine.cs, Runtime/GrassGpuEngine.cs
- Runtime/ChunkedBladeBuffer.cs, Runtime/GrassInteractorBuffer.cs, Runtime/GrassTierProbe.cs
- Shaders/GrassCull.compute, Shaders/GrassInteractIndirect.shader
- Editor/GrassCullHarness.cs

MODIFIED:
- Runtime/GrassInteractField.cs, Runtime/GrassLODConfig.cs

UNCHANGED but reviewed for correct reuse (no edits expected):
- GrassBendSimulator, GrassRenderer, GrassScatter, GrassInteractor, GrassLayer, GrassFieldSpace, InstanceBatchPool, GrassInteractInstanced.shader.

## Review checklist (the reviewer must confirm)

- Code conventions (.claude/rules/code-conventions-unity.md): camelCase private fields NO underscore, this. prefix everywhere, [SerializeField] private, UPPER_SNAKE_CASE constants, #nullable enable in every new file. Mirrors existing GrassInteract style.
- No magic numbers: chunkSize, lodCount, MAX_INTERACTORS, LOD thresholds, lean constants (DEG_PER_METRE 55, MAX_LEAN_DEGREES 80) are named constants or config fields - not inline literals.
- GraphicsBuffer lifecycle: every buffer (BladeInstance, ChunkAabb, ChunkRange, visibleChunks, dispatchArgsB, 3x visibleLod, 3x args, interactors) is created in Build and released in Dispose. No leak across rebuild/domain-reload. SetCounterValue(0) discipline present for every Append+Counter buffer each frame.
- Errors over silent fallbacks: probe/self-test demote logs a clear reason (not a silent black field). Overflow (>16 interactors) warns. No empty catch.
- SSOT / no duplication: GPU deform reuses the SAME wind + lean math + constants as GrassBendSimulator (no divergent second copy of the formula beyond the required HLSL port; the port is documented as the GPU mirror).
- Indirect-args + counter-reset ordering correct (R2/R3): cull dispatch before CopyCount before draw, on one command buffer.
- The CPU tier is byte-for-byte unchanged (low-tier zero-rewrite-risk claim holds).
- Mobile: no per-frame GC, no per-frame GPU readback in the steady state, buffers sized for 250k.

## Verification gate (live-editor evidence)

1. set_active_instance GrassInteract FIRST.
2. read_console after a clean compile -> zero errors, zero warnings introduced by this feature.
3. Run the unity-code-reviewer agent over the review surface above.
4. Triage findings: every Critical and every High is fixed in-place (re-run the relevant phase gate after each fix). Medium/Low are either fixed or explicitly accepted with a one-line justification in the review record.
5. Re-run the Phase 1 (CPU parity), Phase 5 (high-tier render), Phase 7 (tier select + hardware) gates after any fix that touches their surface, to confirm no regression.

Pass = unity-code-reviewer reports 0 Critical, 0 High; console clean; the CPU-parity + high-tier-render + tier-select gates still pass.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| A fix for a review finding regresses an earlier phase gate | 2 | 3 | 6 | Re-run the touched phase gate after each fix (step 5); do not close Phase 9 until all earlier gates are green again. |
| Reviewer flags the GPU-mirror of the deform math as duplication | 2 | 2 | 4 | Document in the HLSL + the engine that the VS port intentionally mirrors GrassBendSimulator (the CPU cannot run in the VS); cite the locked design. Acceptable, not a true SSOT violation. |
| Buffer-leak finding (rebuild churns GraphicsBuffers) | 2 | 3 | 6 | Verify Dispose releases every buffer; rebuild N times in edit mode and watch GPU memory not climb (live evidence). |

## Rollback

Code-review fixes are surgical; if a fix destabilizes a tier, force the CPU tier (Phase 7 override) to ship the verified low tier while the high-tier fix is iterated. No new files here - this phase only edits to satisfy findings.

---

## Definition of done (whole plan)

All 9 phase gates pass. High tier renders 100k-250k blades GPU-driven with 3-LOD indirect draw, GPU wind + lean-away, ~0 main-thread grass cost, and render-or-clean-demote on real GLES3.1 hardware. Low tier unchanged on GLES3.0. Same GrassInteractField public interface; placement/paint/interactor workflows unchanged. Scene view renders both tiers. unity-code-reviewer: 0 Critical, 0 High.
