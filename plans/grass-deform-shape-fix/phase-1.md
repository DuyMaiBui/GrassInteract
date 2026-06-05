# Phase 1 - De-risk Spike: Multi-Channel RT In-Shader Read (GATE)

Route: GATE (decides Route B vs Route A) | Effort: S | Blocks: Phases 2-5

## Objective

Prove that an ARGBHalf (or RGHalf 2-channel) RenderTexture SAMPLES correctly in the grass shader on THIS
URP/GPU path BEFORE any Route-B code lands. A documented gotcha in GrassTrampleMap.CreateRT records that R8
render textures sample as ZERO on this path even though SystemInfo.SupportsRenderTextureFormat reports true
(that flag is render-TARGET support, not sample-correctness) - which is exactly why RHalf was chosen for the
current scalar map. We must NOT assume a multi-channel half format is safe; we must measure it in-shader.

## File ownership

- Scratch only: a throwaway debug shader + a throwaway editor/MonoBehaviour driver placed under
  Assets/GrassInteract/_Spike/ (or a clearly-temporary folder). NO edits to any key file in this phase.
- Deliverable at phase end: the scratch assets are DELETED; only the recorded verdict survives (written into
  this file and relayed to the cook).

## Concrete steps

1. Pick the candidate format. Default candidate = RGHalf (2 channels: pushDir.xy is all Route B strictly needs
   for direction; magnitude can ride a 3rd channel OR be encoded as length(dir)). If RGHalf passes, prefer it
   (smaller than ARGBHalf). If a 3rd channel is wanted for an unclamped magnitude, test ARGBHalf instead. Test
   the format you intend to ship in Phase 3.
2. Create a small RenderTexture in the candidate format and write a KNOWN non-trivial pattern into it via a
   Graphics.Blit (e.g. R = 0.7, G = 0.4 at a known texel) - mirror how TrampleUpdate.shader will write.
3. CPU readback sanity (necessary but NOT sufficient): ReadPixels / AsyncGPUReadback to confirm the RT holds
   the written values on the CPU side. This only proves the write path; it does NOT prove in-shader sampling
   (the R8 bug passed CPU readback yet sampled zero in-shader).
4. IN-SHADER read proof (the load-bearing step): bind the RT as a global and have a REAL rendered fragment or a
   grass blade SAMPLE_TEXTURE2D it with sampler_LinearClamp, then surface the sampled value where it can be
   read back - e.g. write the sampled rg into emissive/base color on a debug quad and screenshot it, OR add a
   temporary debug output to the grass vert/frag that encodes the sampled channels into vertex color, capture
   via MCP screenshot, and confirm the on-screen color matches the written pattern (non-zero).
5. Record the verdict in this file: PASS (format X samples non-zero in-shader) or FAIL (samples zero -> Route A).
6. Delete all scratch assets (shader, driver, debug quad, _Spike folder). Confirm the scene/project is clean.

## Decision gate (explicit)

- PASS -> proceed with ROUTE B (Phases 2, 3, 4-B, 5). Record the exact RenderTextureFormat chosen so Phase 3
  uses the verified one.
- FAIL -> fall back to ROUTE A. Phases 2/3/5 become no-ops (scalar RHalf map is kept). Phase 4 implements the
  Route-A scalar straight-down-flatten + clamped-lean variant (see phase-4.md). Phases 6/7/8 proceed unchanged.
  This fallback is pre-authorized by the plan; do NOT ask the user - just record the spike result and route.

## Success criteria

- A REAL in-shader sample of the candidate multi-channel RT returns the written non-zero values (verified via
  on-screen capture, NOT merely CPU readback).
- The exact passing RenderTextureFormat is recorded for Phase 3 to consume.
- All scratch assets are deleted; project compiles clean afterward.

## Verify

- MCP screenshot of the debug output shows the expected non-zero color (e.g. visible red+green where the RT was
  written), distinguishable from black.
- read_console clean (no shader/compile errors) after the spike and again after scratch deletion.
- git status shows no leftover _Spike files staged or on disk.

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All. Use refresh_unity + read_console after creating/deleting the
scratch shader.
