# Phase 1 — ES3.0 Render-Floor Unblock + Device-Tier Probe + On-Hardware Proof

**Effort:** M · **Blocks:** 2, 3, 4, 6 · **Blocked by:** none · **Priority:** HIGHEST (silent ship-blocker)

## Goal

Make the world render SOMETHING on the device floor (GLES3.0, no-compute) and prove it on real hardware. Today terrain and props both call `Graphics.RenderMeshIndirect` unconditionally; on ES3.0 that no-ops silently → blank world, no error. This phase establishes a probe-driven 3-tier capability gate and a non-compute render path so the build is no longer green-but-blank. This is a vertical proof slice, not the final renderer — Phase 2 builds the real segment renderer on top of the unblocked floor.

## File Ownership (real paths)

Read / diagnose:
- `Assets/WorldPainter/Runtime/Terrain/GpuTerrainEngine.cs` — RenderMeshIndirect at lines 504, 534 (unconditional, no fallback).
- `Assets/WorldPainter/Runtime/Scatter/InstancedPropEngine.cs` — RenderMeshIndirect at lines 508, 510, 512 (unconditional, no fallback).
- `Assets/WorldPainter/Runtime/Scatter/GrassGpuEngine.cs` — the ONLY gated path; copy this pattern.
- `Assets/WorldPainter/Runtime/Scatter/GrassTierProbe.cs` — existing probe (compute + indirect-args + VS-buffers). The model to generalize.
- `Assets/WorldPainter/Shaders/GrassInteractInstanced.shader` — CPU/Low-tier grass shader: `#pragma target 4.5` (lines 42,121,216), `Fallback Off` (278).
- `Assets/WorldPainter/Shaders/ScatterInstanced.shader` — `#pragma target 4.5` (106,611,761), `Fallback Off` (839).
- `Assets/WorldPainter/Shaders/TerrainPatch.shader` — `#pragma target 4.5` (54).

Create:
- `Assets/WorldPainter/Runtime/Render/DeviceTierProbe.cs` — generalizes `GrassTierProbe` into a 3-tier enum (`High`/`Mid`/`Low`) using `SystemInfo.supportsComputeShaders`, `supportsIndirectArgumentsBuffer`, `graphicsDeviceType` (Vulkan/Metal vs GLES3.1 vs GLES3.0). Pure, side-effect-free, single `Debug.Log` reason string (mirror GrassTierProbe API).
- `Assets/WorldPainter/Runtime/Render/RenderFloorSelfTest.cs` — DEV-only on-screen + log assertion that a known terrain+prop mesh actually drew this frame (counts active MeshRenderers in the test window). Explicitly documents that it is the on-device substitute for the un-catchable RenderMeshIndirect no-op.

Edit:
- `Assets/WorldPainter/Runtime/Scatter/InstancedPropEngine.cs` — gate the three `RenderMeshIndirect` calls behind `DeviceTierProbe.TryGpu`. On non-GPU tiers, take the non-compute path (Phase 4 finalizes the path choice; Phase 1 wires the gate so Low no longer no-ops to blank — minimal fallback can be "draw nothing but log loudly" until Phase 4, since the GOAL of P1 is to prove the gate fires, not to ship final props).

Throwaway test harness (NOT deliverable, mark clearly):
- A scratch scene with a hand-built straight test segment (1 terrain mesh + a few props + grass) and a slow flythrough camera. Used only to eyeball the render floor. Deleted after Phase 6.

## Concrete Steps

1. Generalize the probe: author `DeviceTierProbe` returning `RenderTier { High, Mid, Low }`. High = compute + indirect + Vulkan/Metal; Mid = GLES3.1 (compute present); Low = GLES3.0 (no compute). Reuse the exact `SystemInfo` checks from `GrassTierProbe.TryGpu`.
2. Gate `InstancedPropEngine` indirect draws behind the probe (mirror `GrassGpuEngine`'s gating). Confirm no unconditional RenderMeshIndirect remains in the runtime prop path.
3. Audit `GpuTerrainEngine` — Phase 2 deletes it from the runtime path, but in Phase 1 confirm it is never instantiated on Low (gate or disable) so the test segment's terrain does not depend on it.
4. Shader target: on a real ES3.0 device, attempt to compile `GrassInteractInstanced.shader`, `ScatterInstanced.shader`, `TerrainPatch.shader`. If they fail/go pink, lower `#pragma target 4.5` → `3.5` for the non-compute variants and re-validate. Keep `target 4.5` only on the genuinely compute/indirect variants gated to High/Mid. Record actual device result.
5. Build `RenderFloorSelfTest`: assert the test segment's terrain mesh + at least one prop drew this frame; surface a visible FAIL banner in DEV builds.

## Verification

- **Compile:** `read_console` clean after edits (no Burst/shader errors); `run_tests` EditMode green (existing `GpuTerrainEngineUvBindingTests`, `ScaleFactorTests` etc must still pass).
- **Forced-GLES3.0 editor run:** set Editor Graphics API to OpenGLES3 (or launch with `-force-glcore` equivalent / GLES3.0 emulation) and confirm the test segment terrain + props are VISIBLE, not blank. NOTE: emulator is acceptable for the gate-fires check but NOT for the shader-target check.
- **Real device (MANDATORY):** deploy to a physical GLES3.0 (no-compute) Android device. Confirm terrain visible, props visible, grass not pink. **This cannot be replaced by SelfTest** — RenderMeshIndirect no-ops without throwing, so only a human-or-camera-observed render proves it.
- `DeviceTierProbe` logs `Low` on the ES3.0 device and `High` on a Vulkan device.

## Success Criteria

- On a real GLES3.0 device: test-segment terrain renders, props render (or log-loud placeholder), grass renders without pink.
- No unconditional `RenderMeshIndirect` remains in any runtime render path reachable on Low.
- `DeviceTierProbe` returns correct tier on at least one device per tier.
- `RenderFloorSelfTest` reports PASS on High and on the gated Low path.

## Risk Assessment

| Risk | L | I | Score | Mitigation |
|---|---|---|---|---|
| Blank-render not caught by automated tests (no throw) | 5 | 5 | 25 | Mandatory real-device or forced-GLES3.0 visual check before sign-off; RenderFloorSelfTest as on-device tripwire |
| Grass/scatter/terrain shaders fail to compile on true ES3.0 (target 4.5 + Fallback Off) | 4 | 5 | 20 | Lower target to 3.5 for non-compute variants; validate on physical device; gate target-4.5 variants to High/Mid only |
| No physical ES3.0 device available to team | 3 | 5 | 15 | Procure/borrow a GLES3.0 device OR forced-GLES3.0 desktop GL fallback as interim; do NOT sign off Phase 1 on emulator-only |
| Probe mis-tiers a device (e.g. GLES3.1 reporting no compute) | 2 | 3 | 6 | Log reason string per GrassTierProbe pattern; test across ≥3 devices |

Scores ≥15 mitigated before sign-off: rows 1, 2, 3.
