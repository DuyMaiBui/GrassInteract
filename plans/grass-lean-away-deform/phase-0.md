# Phase 0 — Verify & root-cause "still nothing"

Goal: before writing any new deform math, determine definitively WHY grass shows zero bend on
the clean (post-restart) editor. Classify it as **data-path** (sample reads 0), **include-
mechanism** (sample hot but include deform doesn't move geometry), or **too-subtle** (deform
runs but height-collapse is invisible). The verdict picks Phase 1's delivery form.

## Files owned
- `Assets/GrassInteract/Shaders/GrassInteractInstanced.shader` (add `_GRASS_DEBUG` keyword + debug frag/vert paths)
- `Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl` (read-only this phase; temporary inline A/B copy lives in the `.shader`)

## Steps

1. **Add a `_GRASS_DEBUG` shader keyword** to `GrassInteractInstanced.shader`:
   `#pragma multi_compile _ _GRASS_DEBUG`. In the forward frag, when `_GRASS_DEBUG` is on,
   output the per-vertex sampled trample as color (`half4(trample, 1-trample, 0, 1)` — red=hot,
   green=cold). Pass the sampled value through a Varyings field.
   → verify: enable the keyword on the demo material via `manage_material`/`execute_code`;
     screenshot under a moving interactor. Whole-field red near the interactor ⇒ **GPU sample
     through the include is hot** (data path + include-sample both fine).

2. **Inline-vs-include A/B** (the test a prior session flagged as "inline folds, include
   doesn't"). Temporarily add, directly in the `.shader` vert (NOT via the include), an
   unconditional test deform: `posWS.xz += pushTestDir * trample * heightT * 0.5;` using the
   sampled trample from step 1.
   → verify: if the INLINE test leans the field but the included `GrassInteract_ApplyDeform`
     does NOT (with the same sampled `trample`), the include mechanism is the culprit →
     Phase 1 inlines into all 3 passes. If BOTH inline and include move geometry now (clean
     editor), the prior "still nothing" was the cache after all and the include path is fine →
     Phase 1 keeps a single include.

3. **Confirm the data path with GPU readback** (belt-and-suspenders, independent of the shader):
   via `execute_code`, `ReadPixels` the live `_GrassTrampleMap` global and report max R + hot
   pixel count under a moving interactor. (Use `execute_code` return values, NOT `read_console`.)
   → verify: max R > 0.5 hot region tracks the interactor.

4. **Record the verdict** in `phase-0.md` (append a `## Verdict` section) and in the cook notes:
   one of {data-path-broken, include-mechanism-unreliable, too-subtle}. This is the Phase 1
   gate input.

5. **Revert all temporary debug/inline-A/B edits** EXCEPT keep the `_GRASS_DEBUG` keyword + its
   debug-viz path (useful permanently; default OFF / keyword stripped). Grep-verify the deform
   `.hlsl`/`.shader` are back to clean source before Phase 1.

## Gate (must pass to enter Phase 1)
- Step 1 produces a clear hot/cold debug image (sample-through-include status known).
- Step 2 yields an unambiguous inline-vs-include verdict.
- Verdict recorded; temporary edits reverted; `_GRASS_DEBUG` retained.

## Gotchas
- `read_console` unreliable → use `execute_code` returns + `ReadPixels` + the debug-viz image.
- Force shader recompile via targeted reimport of the shader asset or touch a `.cs`; clear
  `Library/ShaderCache/` folder only if needed. NEVER Reimport All / restart the editor.
- `execute_code` is C# 6 — build quoted literals in separate vars, don't nest in `$"{...}"`.
- Keep loose globals outside `UnityPerMaterial`.

## Verdict

**too-subtle / data-path-broken (sampler name)**

Root cause: `SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_linear_clamp, ...)` silently returned 0
in BOTH vert and frag stages. `sampler_linear_clamp` (all lowercase) is NOT declared anywhere in
Unity's URP ShaderLibrary — DX11 treats an undeclared `SamplerState` reference as zero/black.

The correct Unity built-in name is `sampler_LinearClamp` (PascalCase), declared in
`Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl`, which URP Core.hlsl
already includes. Switching to `sampler_LinearClamp` (and adding an explicit include of
`GlobalSamplers.hlsl` for clarity) fixed the sample — GPU readback maxR=0.98, debug viz showed
the field hot red under the interactor, geometry lean became visible immediately.

Evidence chain:
- GPU readback (CPU-side ReadPixels): maxR=0.9922, hot=35557/262144 → RT had real data all along.
- _GRASS_DEBUG frag viz with sampler_linear_clamp: all green (sample=0) despite hot RT.
- _GRASS_DEBUG frag viz with sampler_LinearClamp: red under interactor (sample=~1.0). Confirmed.
- Geometry lean visible in play mode after fix: radial parting pattern around the moving interactor.

**Include mechanism: FINE.** Both include and inline paths returned the same wrong value (0) before
the fix and the same correct value after — the include mechanism was never the problem. Phase 1
delivers a single include (GrassInteractDeform.hlsl), all 3 passes call GrassInteract_ApplyDeform.

Additional finding: Shader.SetGlobalTexture for _GrassTrampleMap IS accessible to
Graphics.RenderMeshInstanced under URP RenderGraph without a MaterialPropertyBlock — confirmed
working with sampler_LinearClamp. (matProps on RenderParams breaks instanced draws entirely under
RenderGraph — already documented in GrassRenderer.cs from a prior session.)
