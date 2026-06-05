# Phase 2 - Trample-Map Vector Bake (ROUTE B)

Route: B | Effort: M | Blocked by: Phase 1 PASS + Phase 3 (format must carry the channels first)
No-op under Route A.

## Objective

Change the trample update so each texel stores a push VECTOR + magnitude instead of a scalar mask. The vector
encodes the direction a blade at that texel should lean (away from the interactor, optionally blended with the
interactor motion heading) and the magnitude encodes how hard. This removes the gradient-reconstruction step
entirely, killing both DEFECT 1 (zero-gradient core) and DEFECT 2 (size-independent overshoot) at the source.

## File ownership

- Assets/GrassInteract/Shaders/TrampleUpdate.shader - the fade+splat fragment now outputs float4(pushDir.xy,
  magnitude, 0) instead of a scalar max().
- Assets/GrassInteract/Runtime/GrassTrampleMap.cs - feed any EXTRA per-interactor data the bake needs
  (interactor world XZ already passed; add motion heading + a previous-position cache ONLY if motion blend is
  used). The CreateRT format change is owned by Phase 3 (do not duplicate it here).
- Assets/GrassInteract/Runtime/GrassInteractor.cs - READ ONLY for this phase (WorldPosition/Radius/Strength).
  Add a motion-heading source ONLY if the directional mat-down is implemented (see step 4); if added, expose it
  as a public read-only property, no new MonoBehaviour responsibilities.

## Concrete steps

1. Decide the channel packing (consume Phase 1 result): RG = pushDir.xy (unit-ish world XZ direction), B =
   magnitude in [0,1], A = unused/0. If Phase 1 selected RGHalf (2-channel), pack magnitude as length(pushDir)
   and store a UNNORMALIZED direction whose length IS the magnitude (read side normalizes). Document the exact
   packing in a shader comment so the deform read side (Phase 4) matches byte-for-byte.
2. Rewrite the splat loop accumulation. Per interactor k at world XZ ip, radius r, strength s, for texel world
   XZ wpos: dirAway = normalize(wpos - ip); falloff = s * saturate(1 - distance(wpos, ip) / r). Accumulate so
   the STRONGEST contributor wins direction (e.g. keep the dir of the max-falloff interactor) and magnitude =
   max over interactors (mirrors the current max() recovery semantics). Avoid averaging directions of opposing
   interactors into a zero vector - pick the dominant contributor.
3. Fade/recovery: fade the MAGNITUDE channel toward 0 over time exactly as the scalar map faded (recoveryPerSec
   * dt). Direction can persist or be re-derived; the magnitude fade is what makes blades stand back up.
4. (Optional, data-driven) Motion-heading blend: if a directional mat-down is desired (grass lies down ALONG
   the travel direction, like tyre tracks), blend dirAway with the interactor heading by a config-owned weight
   (NEW GrassLODConfig field, e.g. trampleHeadingBlend in [0,1], default 0 = pure radial). Heading = normalized
   (currentPos - previousPos).xz cached in GrassTrampleMap.Tick. NO magic literal - the blend weight is a
   SerializeField. If skipped this release, leave a one-line note; do NOT hardcode a non-zero blend.
5. Keep MAX_TRAMPLE_INTERACTORS, the field-rect UV math, and the off-field warning UNCHANGED.

## Success criteria

- The trample RT texels hold a meaningful (dir, magnitude) at and AROUND the footprint - including a non-zero,
  well-defined direction at the footprint CENTER (the old gradient had zero there). Confirm via the diagnostic
  radial readback in Phase 6.
- Magnitude is bounded in [0,1] and falls to 0 by r, so the read side cannot overshoot the footprint.
- No new magic numbers; any blend weight is a GrassLODConfig field.

## Verify

- refresh_unity + read_console clean after the shader/script edits.
- Phase-6 diagnostic radial readback shows non-zero direction at r=0 (the DEFECT-1 fix at the data level) and
  magnitude reaching ~0 at r = worldRadius (the DEFECT-2 bound at the data level).
- Debug overlay (debugDrawOverlay) shows a coherent directional field around the interactor, not a ring with a
  hole in the middle.

## Unity safety

NEVER kill/quit the Editor; NEVER Reimport All. refresh_unity + read_console after each edit.
