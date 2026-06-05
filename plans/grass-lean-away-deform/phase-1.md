# Phase 1 — Lean-away deform (gradient), cache-safe, all 3 passes

Goal: replace the current "collapse-height + random splay" deform with a **lean-away** deform
whose direction = the negative 4-tap gradient of the scalar `_GrassTrampleMap`. Ship it in the
delivery form chosen by Phase 0's verdict so the include-cache class of bug cannot recur.

Depends on: Phase 0 verdict (data-path / include-mechanism / too-subtle).

## Files owned
- `Assets/GrassInteract/Shaders/GrassInteractDeform.hlsl` (new deform math)
- `Assets/GrassInteract/Shaders/GrassInteractInstanced.shader` (3 passes; inline copy IF Phase 0
  says include-mechanism-unreliable)

## New deform math (replaces `GrassInteract_ApplyDeform` trample block)

```hlsl
// Keep ambient-wind block as-is.
float2 uv  = GrassField_WorldToUv(pivotWS);
float  e   = 1.0 / _GrassTrampleTexelDensity;          // tap offset in UV (≈ 1 RT texel; expose or derive)
float  c   = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_linear_clamp, uv, 0).r;
float  xp  = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_linear_clamp, uv + float2(e,0), 0).r;
float  xm  = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_linear_clamp, uv - float2(e,0), 0).r;
float  zp  = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_linear_clamp, uv + float2(0,e), 0).r;
float  zm  = SAMPLE_TEXTURE2D_LOD(_GrassTrampleMap, sampler_linear_clamp, uv - float2(0,e), 0).r;
float2 grad = float2(xp - xm, zp - zm);                 // ∇intensity (UV space ~ world XZ, field axis-aligned)
float2 push = (length(grad) > 1e-4) ? -normalize(grad) : float2(0,0);  // away from hot
float  bend = c * heightT;                              // tip leans most, root planted
posWS.x += push.x * bend * _GrassBendStrength;
posWS.z += push.y * bend * _GrassBendStrength;
posWS.y -= (posWS.y - pivotWS.y) * bend * _GrassFlatten;// optional slight height loss (small default)
```

- `_GrassBendStrength`, `_GrassFlatten` — new loose globals OR material props (decide at cook
  time; if globals, set once via `Shader.SetGlobal*` and keep OUTSIDE `UnityPerMaterial`).
- `_GrassTrampleTexelDensity` — derive the texel size from RT resolution + field size so the tap
  offset ≈ 1 texel (gradient is meaningful, not aliased). Can be folded into `_GrassFieldRect`
  bind in `GrassFieldSpace.BindGlobals()` (single extra `SetGlobalFloat`).

## Delivery form (gated on Phase 0)
- **Verdict = too-subtle OR include-fine:** keep ONE include (`GrassInteractDeform.hlsl`), all 3
  passes call `GrassInteract_ApplyDeform`. SSOT preserved.
- **Verdict = include-mechanism-unreliable:** inline the identical deform body into forward,
  ShadowCaster, and DepthOnly vert stages, each fenced with a comment:
  `// SSOT: mirror of GrassInteractDeform.hlsl GrassInteract_ApplyDeform — inlined to defeat the
  include-cache bug (see plans/grass-lean-away-deform/phase-0.md verdict). Edit all 3 together.`
  Correctness > DRY here, documented.

## Steps
1. Implement the gradient lean-away in `GrassInteractDeform.hlsl`.
2. Wire `_GrassTrampleTexelDensity` (+ any new strength globals) from `GrassFieldSpace.BindGlobals()`.
3. Apply in all 3 passes per the chosen delivery form; force a clean recompile (targeted
   shader reimport — never Reimport All).
4. Tune `_GrassBendStrength` / `_GrassFlatten` against the demo interactor for a clear lean.

## Verification (live, on `GrassInteract@de203215`)
- Set `set_active_instance GrassInteract@de203215` first.
- Moving interactor → **visible lateral lean** trailing it (screenshot before/after); recovers
  after it passes. A/B: pin interactor at field center vs edge, confirm lean direction points
  away from it.
- Toggle `_GRASS_DEBUG` → sample hot under interactor (regression guard from Phase 0).
- Enter Play AND edit mode — both lean (edit-mode drive path already exists).
- ShadowCaster + DepthOnly: confirm the cast shadow + depth silhouette lean with the blade
  (Frame Debugger or shadowed-ground screenshot).
- `rendering_stats`: FPS / batches ~ parity with pre-change baseline at demo instance count.
- `execute_code`: assert zero per-frame GC alloc on the deform path (no managed allocs added).

## Gate (Phase complete)
All five whole-plan success criteria met (see `plan.md`); temporary tuning reverted to chosen
defaults; deform clean-source; `_GRASS_DEBUG` defaults OFF.

## Risk Assessment
| Risk | Likelihood (1-5) | Impact (1-5) | Score | Mitigation |
|------|-----------------|--------------|-------|------------|
| Gradient noisy/jittery lean | 2 | 3 | 6 | Larger tap offset (smooth) or fallback Option B (RGHalf dir RT) |
| 4 taps too costly on mobile | 2 | 3 | 6 | 2-tap forward diff, or precompute gradient in the splat |
| Axis/UV mismatch flips lean direction | 2 | 3 | 6 | Field rect is axis-aligned; verify sign with the center-vs-edge A/B |
| Inlined 3-pass copies drift | 2 | 3 | 6 | SSOT fence comment; review all 3 together in one edit |

## Timeline
| Item | Effort | Notes |
|------|--------|-------|
| Deform + wiring | S | Math + one global bind |
| 3-pass apply + recompile | S | Form gated on Phase 0 |
| Live verify + tune | S | MCP screenshots, rendering_stats |
| Total | M (~1.5d) | — |
