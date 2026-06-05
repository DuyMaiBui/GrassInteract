# Phase 3 — Shader VS extension + `MAX_LEAN` 80°→90° lift

Effort: M. Depends on: Phase 2 (`_GrassTrailSegments` + `_GrassTrailSegmentCount` bound and populated). Blocks: Phase 4 (visual gate needs the live shader).
Goal: read the trail buffer in `GrassInteractIndirect.shader`'s vertex stage, accumulate capsule-distance plateau bend perpendicular-away from each segment, multiply by per-segment fade alpha, merge with the existing interactor lean, and clamp total to 90°.

## Scope — file ownership

MODIFIED (single file):
- `Assets/GrassInteract/Shaders/GrassInteractIndirect.shader` — add struct decl + buffer decl + VS loop block + `MAX_LEAN` constant bump.

UNCHANGED: every C# file. `GrassTrailBuffer.cs`, `GrassTrailInteractor.cs`, `GrassGpuEngine.cs` from previous phases — no edits.

## Shader edits (LOCKED — 4 isolated, well-delimited blocks)

### Block A — struct + buffer decl (top of HLSL section, near existing interactor struct)

```hlsl
// TRAIL DEFORM BEGIN -------------------------------------------------------
struct GrassTrailSegmentGpu                 // matches C# TrailSegmentGpu, 48 B
{
    float3 PosA;   float Radius;
    float3 PosB;   float Alpha;
    float  MaxBendRad;
    float  CenterPct;
    float  Strength;
    float  _Pad;
};
StructuredBuffer<GrassTrailSegmentGpu> _GrassTrailSegments;
int                                     _GrassTrailSegmentCount;
// TRAIL DEFORM END ---------------------------------------------------------
```

### Block B — `MAX_LEAN` constant lift

Existing code (Phase 5 of the GPU plan):

```hlsl
static const float MAX_LEAN = radians(80.0);
```

Change to:

```hlsl
static const float MAX_LEAN = radians(90.0);
```

R1 mitigation: existing strength=1 single-interactor lean math caps below the literal (Phase 5 of the GPU plan documents `lean *= 0.85` headroom). Diff verified in Phase 3 verification gate step 5.

### Block C — VS deform loop (vertex stage, immediately after the existing interactor lean loop)

```hlsl
// TRAIL DEFORM BEGIN -------------------------------------------------------
// 2D (XZ) capsule distance per blade root; plateau profile; fade alpha.
// Accumulate lean perpendicular AWAY from the segment line; merge later.
float3 trailLeanAccum = float3(0, 0, 0);
{
    float2 bladeXZ = posWS.xz;
    int n = _GrassTrailSegmentCount;
    [loop]
    for (int i = 0; i < n; ++i)
    {
        GrassTrailSegmentGpu s = _GrassTrailSegments[i];

        float2 ab = s.PosB.xz - s.PosA.xz;
        float  abLenSq = max(dot(ab, ab), 1e-6);
        float  t  = saturate(dot(bladeXZ - s.PosA.xz, ab) / abLenSq);
        float2 c  = s.PosA.xz + ab * t;
        float2 r  = bladeXZ - c;
        float  d  = length(r);
        if (d > s.Radius) continue;

        float dn      = d / s.Radius;                            // 0..1
        float plateau = (dn <= s.CenterPct)
                      ? 1.0
                      : 1.0 - smoothstep(s.CenterPct, 1.0, dn);
        float angle   = s.MaxBendRad * plateau * s.Alpha * s.Strength;

        float2 dir2 = (d > 1e-4) ? (r / d) : float2(1, 0);       // outward from segment
        trailLeanAccum += float3(dir2.x, 0, dir2.y) * angle;
    }
}
// TRAIL DEFORM END ---------------------------------------------------------
```

### Block D — merge with existing interactor lean + clamp to 90°

Locate where Phase 5 of the GPU plan accumulates the existing interactor lean into a vector named (per project status) `leanAccum` or similar. Immediately after that accumulation:

```hlsl
// TRAIL DEFORM BEGIN -------------------------------------------------------
leanAccum += trailLeanAccum;

// Clamp total lean magnitude to MAX_LEAN (90°). Existing interactor-only
// math kept this under the cap; trails CAN reach it.
float leanMag = length(leanAccum);
if (leanMag > MAX_LEAN)
    leanAccum *= (MAX_LEAN / leanMag);
// TRAIL DEFORM END ---------------------------------------------------------
```

Exact variable name + insertion point: confirmed during implementation by reading `GrassInteractIndirect.shader` (Phase 5 cooked it; this plan trusts it exists at HEAD).

## Verification gate (live-editor evidence)

1. `set_active_instance GrassInteract` FIRST. Force GPU tier on demo (`forceTier = ForceGpu`).
2. **Compile gate**: 0 shader errors, 0 warnings on `manage_editor.read_console`. `manage_shader` reports `GrassInteractIndirect` compiled with both `_LOD2_BILLBOARD` keyword off and on.
3. **No-trail regression (R1)**: with `GrassTrailInteractor.Active.Count == 0`, top-down screenshot of the existing orbit effector lean MUST match pre-Phase-3 screenshot pixel-comparable (single-interactor math unchanged; `MAX_LEAN` lift is a no-op because existing math caps below 80°). Save the pre-Phase-3 reference screenshot BEFORE editing the shader.
4. **Bent trail visible**:
   - Place a moving cube + `GrassTrailInteractor` (`trailDuration=5`, `radius=2`, `maxBendDegrees=90`, `centerZonePercent=0.4`, `strength=1`).
   - Sweep linearly across the field over 4 s.
   - Mid-sweep screenshot: visible bent trail in the path. Centre stripe = flat (90°); edges = upright. Profile must look like a flat-top, not a smooth dome.
5. **Plateau profile shape**:
   - Set `centerZonePercent = 1.0` → entire trail width = flat (no falloff edge).
   - Set `centerZonePercent = 0.0` → smooth dome (no flat centre).
   - Set `centerZonePercent = 0.4` → flat-top with smooth shoulders.
   - One screenshot per setting confirms the plateau is interactive.
6. **Fade**:
   - Stop the cube at t=4 s. Capture screenshots at t=4.5 / 6 / 9 s (i.e. 0.5 / 2 / 5 s post-sweep).
   - At 5 s post-sweep (= one full `trailDuration` past stop), grass should be fully upright.
   - At 0.5 s post-sweep, oldest end of trail visibly recovered, newest end still flat.
7. **90° clamp**: place TWO trails overlapping perpendicular. At the intersection blade-lean magnitude must NOT exceed 90° (no NaN, no inversion). `execute_code` screenshot check: lean direction sane (no shooting-through-ground artefacts).
8. **Existing interactor coexistence**: orbit effector + trail both present. Both lean simultaneously. Trail does not erase interactor lean and vice versa (additive then clamped).

Pass = compile + no regression on no-trail render + bent trail visible + plateau shape interactive + fade timing matches `trailDuration` + 90° clamp behaves + coexistence works.

## Risk table

| Risk | L | I | Score | Mitigation |
|---|:--:|:--:|:--:|---|
| HLSL struct field order/types drift from C# `TrailSegmentGpu` | 2 | 4 | 8 | Block A is field-order-mirror of Phase 2's C# struct. Reviewer signs off. `_GrassTrailSegmentCount` round-trip test (verification step 4) catches stride bugs immediately. |
| `MAX_LEAN` lift changes existing visuals | 2 | 3 | 6 | Verification step 3 = pre/post identical screenshot. Math review in commit message: cite Phase 5's headroom factor. |
| Insertion point for Block D wrong (lean variable misnamed) | 2 | 3 | 6 | Read `GrassInteractIndirect.shader` at HEAD before editing; locate the actual accumulator name; do NOT guess. |
| Numerical issues (`d > 1e-4` guard, smoothstep edge case) | 2 | 2 | 4 | Guard codified in Block C. `smoothstep(centerPct, 1.0, dn)` with `centerPct < 1.0` is well-defined; centerPct=1.0 path explicit (`dn <= centerPct` branch handles it). |
| Mobile shader cost regression — 128-seg loop blows VS time | 2 | 3 | 6 | Phase 4 perf gate: 20k blades + 50-seg trail ≥ 60 fps on dev machine. Cheap early-out `if (d > radius) continue;` rejects ~95% of (blade, segment) pairs. Documented chunk-prefilter fallback for v2 if needed. |
| Two overlapping trails create lean > 90° → ground-clipping or NaN | 2 | 3 | 6 | Block D clamp `leanAccum *= MAX_LEAN / leanMag` is unconditional. Verification step 7 explicitly tests this. |
| Forgot `[loop]` attribute → unrollment failure / huge shader | 2 | 2 | 4 | Block C explicitly `[loop]`. Variant count check in verification step 2 (no new variants beyond LOD2 keyword). |
| Edit-mode Scene-view render doesn't pick up trail buffer | 2 | 2 | 4 | Phase 8 of the GPU plan already proved beginCameraRendering works for indirect (per project status). Trail buffer binds globally; available to all cameras. Verify in Scene view + Game view both. |

## Rollback

In `GrassInteractIndirect.shader`:
1. Search for `// TRAIL DEFORM BEGIN` and `// TRAIL DEFORM END` — delete each block in full (3 blocks total: A, C, D).
2. Revert Block B: `MAX_LEAN = radians(80.0)`.

Shader recompiles to pre-Phase-3 behaviour. C# layers (Phase 1, Phase 2) become dead writes — buffer is populated but never read. Field renders identically to pre-feature baseline.
