# Brainstorm — Grass Trail Deform (Persistent Capsule-Trail Bending)

**Date:** 2026-06-04
**Project:** GrassInteract (interactive instanced grass — GPU tier)
**Author:** /t1k-brainstorm session
**Next step:** /t1k:plan

---

## Problem Statement

The current `GrassInteractor` is a circular, instant-recovery footprint:
grass leans away from the centre while inside the footprint and springs back
to upright the moment the interactor leaves. This is correct for a stationary
or slow-orbit effector but produces no trail when a moving interactor
(car wheel, running character, projectile slime) sweeps across the field.

User wants:

1. A moving interactor leaves a **persistent bent trail** behind it.
2. Trail **auto-fades segment-by-segment** after a configurable duration
   (TrailRenderer-like — older parts of the trail recover to upright while
   freshly bent parts stay flat).
3. Bend angle at the **centre of the trail can reach 90°** (full flatten),
   with a **configurable plateau / falloff profile** across the trail width
   (inner *X*% = full bend, smooth ramp out to upright at the trail-width edge).
4. Connect samples with **capsule segments** (no visible gaps at high speed).
5. **GPU tier only** (CPU tier documented as no-op).
6. **Stroke breaks** — when the character jumps (foot leaves ground), no new
   trail samples are emitted and no capsule bridges the airborne gap. On
   landing, a new stroke starts. Eliminates the "grass bent mid-air" confusion.

---

## Requirements

| # | Requirement | Source |
|---|---|---|
| R1 | Persistent bent trail behind a moving `GrassTrailInteractor` | User |
| R2 | Per-sample TrailRenderer-style auto-fade controlled by `trailDuration` | User |
| R3 | Plateau bend profile: inner `centerZonePercent` of capsule width = full `maxBendDegrees` (clamp 0–90°); smooth falloff to 0 at edge | User |
| R4 | Capsule-segment connectivity (no gaps even at high speed) | User |
| R5 | GPU tier only (CPU tier = no-op + one-time runtime warning) | User |
| R6 | Stroke-break support — `Emitting=false` skips sample emission AND prevents bridging across the gap on re-emit | User |
| R7 | Coexists with existing `GrassInteractor` (instant-circle component) — same field can host both | Design |
| R8 | 90° lean must work in the existing GPU deform shader (current cap is 80° via `MAX_LEAN`) | Existing code |
| R9 | Byte-stable to existing scatter / cull pipeline (no regression in `ChunkBakeVerify`, `CullHarness`, `BladeCullHarness`) | Project discipline |

---

## Approaches Evaluated

### Approach A — Discrete circle stamps (FIFO ring of `GrassInteractor`-clones)

Each frame, if interactor moved >`stampSpacing`, spawn a virtual circular
footprint into a FIFO ring; each stamp fades over `duration`; ring size caps
the total trail length.

**Pros:** zero shader change — reuses existing per-interactor lean code.
**Cons:** visible **gaps at high speed** unless `stampSpacing` is tiny
(which inflates stamp count & GPU buffer pressure); user explicitly chose
capsules over discrete stamps. **Rejected.**

### Approach B — Capsule-segment trail (chosen)

FIFO sample list; pairs of consecutive samples form capsule segments uploaded
to a new `GrassTrailBuffer` (sibling of `GrassInteractorBuffer`). New VS loop
in `GrassInteractIndirect.shader` computes blade-to-capsule distance, plateau
profile, fade alpha, and accumulates lean perpendicular-away from the segment.

**Pros:** continuous trail at any speed; clean lifetime semantics; reuses
existing buffer/upload pattern; well-scoped shader change; matches user's
explicit choice.
**Cons:** new GPU buffer + new shader loop (~30 HLSL lines). Manageable.

### Approach C — GPU R8 splat map (top-down RT covering field)

Splat brush each frame onto a render-target sized to field bounds; shader
samples the RT in VS by `worldXZ → UV`.

**Pros:** unbounded trail length, single VS tex2D fetch per blade.
**Cons:** new RT + ping-pong fade pass; resolution trade-off (low-res = blocky
edges, high-res = VRAM cost); CPU tier can't share it; doesn't match the
clean "capsule segments" model the user picked; harder to do stroke-breaks
(would need a separate "active" mask per splat). **Rejected.**

**Decision: Approach B.**

---

## Recommended Solution

### Components

1. **`Runtime/GrassTrailInteractor.cs`** — new MonoBehaviour (sibling to
   `GrassInteractor`). Owns the sampler, registry, fields, gizmos.
2. **`Runtime/GrassTrailBuffer.cs`** — new GraphicsBuffer wrapper (sibling
   of `GrassInteractorBuffer`).
3. **`Runtime/GrassGpuEngine.cs`** — extended: gather all active trail
   interactors → flatten to segments → upload + set `_TrailSegmentCount`.
4. **`Shaders/GrassInteractIndirect.shader`** — new VS deform loop over
   `_TrailSegments`; lift `MAX_LEAN` cap from 80° to 90°.
5. **Demo scene** — add a moving cube + `GrassTrailInteractor` next to the
   existing orbit effector.

### Component API

```csharp
[ExecuteAlways] [DisallowMultipleComponent]
public sealed class GrassTrailInteractor : MonoBehaviour {
  [SerializeField] float trailDuration        = 5f;     // seconds → fade
  [SerializeField] float minVertexDistance    = 0.25f;  // sample spacing (m)
  [SerializeField] float worldRadius          = 2f;     // capsule half-width
  [SerializeField, Range(0,90)] float maxBendDegrees     = 90f;
  [SerializeField, Range(0,1)]  float centerZonePercent  = 0.4f;
  [SerializeField, Range(0,1)]  float strength           = 1f;
  [SerializeField] TrailRenderer? linkedTrailRenderer;          // optional: WYSIWYG defaults on Reset()

  /// <summary>Set false when off-ground; existing samples age normally,
  /// no new samples emit, and the next post-resume sample is a stroke-start
  /// so no capsule bridges the gap.</summary>
  public bool Emitting { get; set; } = true;

  public static IReadOnlyList<GrassTrailInteractor> Active { get; }
}
```

### GPU types

```hlsl
struct TrailSegmentGpu {   // 48 B, 16-byte aligned
  float3 posA;   float radius;
  float3 posB;   float alpha;
  float  maxBendRad;
  float  centerPct;
  float  strength;
  float  _pad;
};

#define MAX_TRAIL_SEGMENTS 128
StructuredBuffer<TrailSegmentGpu> _TrailSegments;
int _TrailSegmentCount;
```

128 segs × 48 B = 6 KB. Overflow = drop oldest + warn-once (mirrors
`GrassInteractorBuffer` discipline).

### Sampler algorithm (LateUpdate)

1. Tick `age += dt` on all existing samples (regardless of `Emitting`).
2. Evict samples whose `age > trailDuration`.
3. If `Emitting` was true last frame and false now → set `pendingStrokeBreak = true`.
4. If `!Emitting` → skip emission steps 5–6.
5. If `samples.Count == 0` OR `distance(pos, lastSample.pos) > minVertexDistance`:
   append `{posWS, age:0, strokeStart: pendingStrokeBreak || samples.Count == 0}`,
   clear `pendingStrokeBreak`.
6. Cache `Emitting` for next frame's edge detection.

### Segment build (GPU upload, in `GrassGpuEngine.Step`)

```csharp
foreach (interactor in GrassTrailInteractor.Active) {
  var s = interactor.Samples;
  for (int i = 1; i < s.Count; i++) {
    if (s[i].strokeStart) continue;            // skip — pen lifted here
    segments.Add(new TrailSegmentGpu {
      posA = s[i-1].posWS, posB = s[i].posWS,
      radius = interactor.WorldRadius,
      alpha  = 0.5f * ((1 - s[i-1].age/duration) + (1 - s[i].age/duration)),
      maxBendRad = Mathf.Deg2Rad * interactor.MaxBendDegrees,
      centerPct  = interactor.CenterZonePercent,
      strength   = interactor.Strength,
    });
    if (segments.Count >= MAX_TRAIL_SEGMENTS) goto upload;  // overflow guard
  }
}
upload: trailBuffer.SetData(segments); Shader.SetGlobalInteger(_TrailSegmentCount, segments.Count);
```

### Shader VS extension (after existing interactor loop)

```hlsl
float3 trailLeanAccum = 0;
float2 bladeXZ = posWS.xz;
[loop] for (int i = 0; i < _TrailSegmentCount; ++i) {
  TrailSegmentGpu s = _TrailSegments[i];
  float2 ab = s.posB.xz - s.posA.xz;
  float  t  = saturate(dot(bladeXZ - s.posA.xz, ab) / max(dot(ab,ab), 1e-6));
  float2 c  = s.posA.xz + ab * t;
  float2 r  = bladeXZ - c;
  float  d  = length(r);
  if (d > s.radius) continue;

  float dn      = d / s.radius;
  float plateau = dn <= s.centerPct
                ? 1.0
                : 1.0 - smoothstep(s.centerPct, 1.0, dn);
  float angle   = s.maxBendRad * plateau * s.alpha * s.strength;

  float2 dir2 = (d > 1e-4) ? (r / d) : float2(1,0);
  trailLeanAccum += float3(dir2.x, 0, dir2.y) * angle;
}
// merge with existing interactor lean, clamp total magnitude to MAX_LEAN_RAD (lift to 90°)
```

`MAX_LEAN` literal bumped 80° → 90° (single line; existing interactor lean
unchanged in behaviour because it was already clamped well below 80°).

### Gizmo (Editor)

- Polyline through all samples (alpha-tinted by `1 - age/duration`).
- Wire disc at every sample (radius = `worldRadius`).
- Tick mark on `strokeStart` samples (distinct colour) so designers can see
  pen-lift moments in the scene view.

### Demo & verification

- Demo scene: cube on a 4-second linear sweep + `GrassTrailInteractor`
  (`trailDuration=5`, `radius=2`, `maxBend=90`, `centerPct=0.4`).
- Test script: toggles `Emitting=false` for 0.5 s mid-sweep to verify the gap.
- Screenshot gates:
  - Before sweep — full field upright (regression check).
  - Mid-sweep — bent trail visible behind cube.
  - Post-emit-toggle — gap in trail at the toggle window.
  - 6 s post-sweep — entire trail recovered to upright.
- Harness gates: `ChunkBakeVerify`, `CullHarness`, `BladeCullHarness`,
  `ScatterInstanceCullHarness`, `SamplerVerify` all PASS unchanged
  (trail is a deform-only feature; placement/cull math untouched).

---

## Implementation Considerations & Risks

| # | Risk | Mitigation |
|---|---|---|
| K1 | Lifting `MAX_LEAN` 80°→90° may visually change existing single-interactor lean | Existing strength=1 lean math caps below the literal; verify against pre-change screenshot. |
| K2 | `_TrailSegmentCount` global collision if another system uses the name | Prefix `GRASS_TRAIL_` or namespace via `_GrassTrailSegmentCount`. |
| K3 | Per-frame segment flattening cost on CPU (e.g. 10 active interactors × ~50 samples) | Pre-allocated `List<TrailSegmentGpu>` with capacity = MAX; flatten in `Step()` once per frame. |
| K4 | Shader VS loop cost: every blade × every segment | Early-out `if (d > s.radius)`; capsule rejection is cheap (one dot+saturate). At 128 segs, target devices handle 64k blades fine — verify in Phase 4 perf screenshot. |
| K5 | Stroke-start sample on landing might be far from the airborne takeoff sample if `Emitting` was false longer than `trailDuration` | Self-resolves — takeoff sample evicted by then; landing sample is a natural first-of-list. |
| K6 | TrailRenderer `linkedTrailRenderer` field tempting users to think it drives the deform | Document clearly: it's a `Reset()`-time defaults copy ONLY; runtime drive is independent. |
| K7 | Y-height changes (interactor flying or going underground) | XZ-only distance; trail rides ground. Acceptable for v1; surface in docs. |
| K8 | CPU tier silently does nothing → user confusion | One-time runtime warn when `GrassTrailInteractor.Active.Count > 0` and any field is CPU tier. |

---

## Success Metrics

- **Visual**: screenshots show bent trail following a moving interactor;
  trail fades after `trailDuration`; stroke breaks produce clean gaps.
- **Regression**: all 5 existing harnesses PASS unchanged.
- **Perf**: GPU tier @ 20k blades + 1 interactor + 50-segment trail holds
  ≥ 60 fps in editor on the dev machine (current baseline ~140 fps with 100k
  blades; budget for 128 segments × 64k blades is comfortable).
- **Compile**: clean compile, 0 C# errors, 0 shader errors / warnings on
  GrassInteract@HEAD.

---

## Next Steps

1. /t1k:plan — produce phased implementation plan from this report.
2. Phase outline (rough sizing, plan owns the final cut):
   - **P1** — `GrassTrailInteractor` + sampler + stroke-break logic + gizmo.
   - **P2** — `GrassTrailBuffer` + `TrailSegmentGpu` + `GrassGpuEngine.Step` upload.
   - **P3** — Shader VS extension + `MAX_LEAN` 80→90° lift.
   - **P4** — Demo wiring + visual + harness verification.
3. Sequential one-agent-per-phase with approval gates (consistent with
   prior GrassInteract cooks per project status).

---

## Dependencies

- Existing GPU tier (Phases 1–9 of `grass-gpu-driven-indirect` plan,
  shipped and verified per project status).
- `GrassGpuEngine.Step(dt)` upload point (Phase 6 of that plan).
- `GrassInteractIndirect.shader` VS deform stage (Phase 5).
- `GrassInteractorBuffer.cs` pattern reused for `GrassTrailBuffer.cs`.
- No new external packages.
