# Phase 7 - Device smoke test + tier selection (HIGH-RISK GLES3.1 hardware gate)

Effort: M. Depends on: Phase 5 (needs a runnable high tier). Blocks: the high-tier ship decision.
Goal: wire the runtime device probe + a debug force-CPU override into the facade, AND prove on REAL GLES3.1 Android hardware that RenderMeshIndirect + VS StructuredBuffer reads actually render (some devices report supportsIndirectArgumentBuffers=true but fail in practice). This is the R1 mitigation (score 16, the highest in the plan) - front-loaded, gated, non-bypassable for the high-tier ship claim.

## Scope - file ownership

MODIFIED:
- Assets/GrassInteract/Runtime/GrassInteractField.cs - in OnEnable, run the probe; select GrassGpuEngine (high) or GrassCpuEngine (low). Add a serialized debug enum/bool forceTier { Auto, ForceCpu, ForceGpu } (editor/development only path; Auto in release). Wire engine selection through the IGrassEngine seam (Phase 1).

NEW:
- Assets/GrassInteract/Runtime/GrassTierProbe.cs - static bool TryGpu(out string reason): the probe (supportsComputeShaders && supportsIndirectArgumentBuffers && maxComputeBufferInputsVertex > 0) PLUS an optional runtime self-test hook. Returns the chosen tier + a human reason for logging.
- (optional) a runtime self-test inside GrassGpuEngine.Build: render the indirect path into a 1x1 RenderTexture, read back the single pixel; if it is the clear color (nothing drew), the high tier is non-functional on this device -> the facade DEMOTES to GrassCpuEngine. This converts the silent-fail devices into a clean fallback rather than a black field.

UNCHANGED: both engines (consumed), GrassCull.compute, shaders, ChunkedBladeBuffer.

## Probe + self-test + demote flow

1. OnEnable: if forceTier==ForceCpu -> CPU. If ForceGpu -> GPU (skip probe, dev only). If Auto:
2. GrassTierProbe.TryGpu -> false (any flag missing) -> CPU tier, log the reason.
3. true -> build GrassGpuEngine. Build runs the 1x1 self-test draw. Pixel == known sentinel -> keep GPU. Pixel == clear -> Dispose GPU engine, build CPU engine, log "GPU indirect self-test failed on <device> -> CPU tier".
4. Expose the active tier (a public readonly property + a log line) so the verification gate + future QA can read which tier a device landed on.

## Verification gate (live-editor + REAL HARDWARE)

EDITOR (necessary, not sufficient):
1. set_active_instance GrassInteract FIRST.
2. Auto on the dev machine (desktop GL/Vulkan) -> selects GPU tier; a tier-readout (log/property) confirms "high".
3. Set forceTier=ForceCpu -> facade flips to the CPU tier; the field still renders (proves the seam swap + the fallback). Set ForceGpu -> back to high.
4. Self-test path: temporarily make the indirect draw a no-op -> the 1x1 self-test reads clear -> facade demotes to CPU cleanly (no black field, log line present). Restore.

HARDWARE (the actual R1 gate - REQUIRED before declaring the high tier shippable):
5. Build an Android player (GLES3.1 target, a real low/mid device that reports supportsIndirectArgumentBuffers=true). Deploy.
6. ON DEVICE: the grass field renders via the high tier (correct color + LODs + wind + interactor lean), OR the self-test demotes it to the CPU tier and the field still renders (no black/empty field, fallback log present). EITHER outcome passes; a black/empty field FAILS.
7. Capture a device screenshot + the tier-readout log as the evidence artifact.

Pass = editor steps 2-4 hold AND step 6 on at least one real GLES3.1 device shows render-or-clean-demote (never a black field). If a class of devices fails the self-test, they ride the CPU tier by design - that is a PASS for the tiered architecture, documented in the artifact.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| Device reports indirect=true but RenderMeshIndirect/VS-buffer fails -> black field | 4 | 4 | 16 | The 1x1 self-test detects the silent fail and demotes to CPU; step-6 hardware gate proves render-or-clean-demote. This IS the R1 mitigation, gated here. |
| No physical GLES3.1 test device available at gate time | 3 | 4 | 12 | Front-load device procurement; if truly unavailable, ship with the self-test demote as the safety net AND flag the high tier "unverified on hardware" in the artifact - do NOT claim high-tier ship without the device run. Escalate to the user if no device can be sourced. |
| Self-test 1x1 readback itself stalls / is slow on mobile | 2 | 2 | 4 | One-time at Build (not per frame); a single 1x1 pixel readback. Acceptable one-time cost. |
| Probe passes but compute group-size / buffer limits differ on device | 2 | 3 | 6 | 64-thread groups (Phase 3/4); buffer sizes within GLES3.1 mins; self-test exercises the real pipeline so a limit breach shows as a clear pixel -> demote. |

## Rollback

Set forceTier=ForceCpu (or hard-default the facade to CPU): the verified low tier renders everywhere, GPU code inert. Probe + self-test are additive; removing them leaves the CPU tier as the only path.
