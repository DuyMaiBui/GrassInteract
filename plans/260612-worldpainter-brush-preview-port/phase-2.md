# Phase 2 — Square stamp CPU/GPU parity (full brush)

Effort: **M** · Blocks: Phase 3 · Blocked by: Phase 1

## Goal

Make Square a **full** brush: the GPU stamp mask honours the shape. Add a `_BrushShape` uniform to
`BrushMask.hlsl` that switches the per-texel distance from Euclidean `length()` (circle) to
Chebyshev `max(|du.x|,|du.y|)` (square) BEFORE the falloff-LUT sample — same falloff curve, square
iso-contours. Declare the uniform in `TerrainBrush.compute`, bind it on EVERY dispatch in both the
sculpt path (`Kernels.cs`) and the biome path (`WorldPainterBiomeStamp.cs`) so it cannot leak.
Mirror the Chebyshev branch in the CPU reference and add square parity tests — the CPU↔GPU parity
invariant is the gate for this phase.

This phase is deliberately ISOLATED from the Phase 3 preview rewrite so a parity failure here
cannot be masked by a visual change.

## File ownership

- `Assets/WorldPainter/Shaders/BrushMask.hlsl`
- `Assets/WorldPainter/Shaders/TerrainBrush.compute`
- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.Kernels.cs`
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterBiomeStamp.cs`
- `Assets/WorldPainter/Tests/Editor/TerrainBrushMathTests.cs`

## Exact edits

### 1. `BrushMask.hlsl` — add `_BrushShape` uniform + Chebyshev branch

Add the uniform near the LUT declaration (after the `_FalloffLUT` declaration, ~line 26):

```hlsl
/// Brush footprint shape: 0 = Circle (Euclidean), 1 = Square (Chebyshev). Set every dispatch.
int _BrushShape;
```

Add a shared distance helper (place above `BrushFalloffInline`, ~line 38):

```hlsl
// Per-texel normalized-distance metric. Circle = Euclidean length; Square = Chebyshev
// (max of |du.x|,|du.y|), which yields square iso-contours through the SAME falloff curve.
float BrushDist_BM(float2 du)
{
    if (_BrushShape == 1)
        return max(abs(du.x), abs(du.y)); // Chebyshev → square
    return length(du);                    // Euclidean → circle
}
```

Replace the distance computation in BOTH falloff functions so they route through the helper:

`BrushFalloffInline` (~line 40-46) — change `float dist = length(texelUV - centerUV);` to:

```hlsl
    float dist = BrushDist_BM(texelUV - centerUV);
```

`BrushFalloffLUT` (~line 50-58) — change `float dist = length(texelUV - centerUV);` to:

```hlsl
    float dist = BrushDist_BM(texelUV - centerUV);
```

Leave the rest of both functions (`t = saturate(dist/radius)`, LUT load, smoothstep) untouched —
only the distance metric changes, so the falloff CURVE is identical for both shapes; only the
iso-contour shape differs. This is the locked design.

### 2. `TerrainBrush.compute` — declare `_BrushShape`

`BrushMask.hlsl` declares `_BrushShape`, and it is `#include`d at line 16. HLSL allows the include
to own the declaration; no separate declaration is required in `TerrainBrush.compute`. **Do NOT
re-declare `int _BrushShape;` in the .compute file** — a duplicate declaration is a redefinition
error. Verify after edit: the compute compiles with the single declaration living in the included
`BrushMask.hlsl` (mirrors how `_FalloffLUT`, `TexelToUV_BM`, etc. are owned by the include and used
by the kernels). No edit to `TerrainBrush.compute` is expected; this section exists to document the
explicit decision and the compile check.

### 3. `WorldPainterSculptTool.Kernels.cs` — bind `_BrushShape` every dispatch

In `BindAndDispatch` (~line 23), where the shared uniforms are set (after
`this.brushCompute.SetInt("_RTRes", rtRes);` at ~line 45), add:

```csharp
this.brushCompute.SetInt("_BrushShape", (int)brush.shape);
```

`brush` is the local `var brush = WorldPainterState.Brush;` already at ~line 33. Because this is set
in the shared-uniform block of `BindAndDispatch`, it covers the Height, Splat, and Density sculpt
paths in one place. Setting it here EVERY dispatch is the leak guard — never rely on a prior set.

### 4. `WorldPainterBiomeStamp.cs` — bind `_BrushShape` every dispatch

`WorldPainterBiomeStamp.Stamp` (~line 53) shares the same `brushCompute` and sets its own shared
uniforms (~lines 76-78). Add the shape bind there, after
`brushCompute.SetInt("_RTRes", rtRes);` (~line 78):

```csharp
brushCompute.SetInt("_BrushShape", (int)WorldPainterState.Brush.shape);
```

`Stamp` does not currently receive a `BrushSettings`/shape argument, so read the SSOT directly via
`WorldPainterState.Brush.shape` (consistent with how the dock and sculpt tool read the same SSOT).
This guarantees the biome path can never run a stale Square left over from a sculpt stroke (or
vice-versa) on the shared compute shader.

### 5. `TerrainBrushMathTests.cs` — CPU Chebyshev mirror + square parity tests

The CPU reference `BrushFalloff` (~line 25) currently uses `Vector2.Distance` (Euclidean). Add a
shape-aware overload mirroring the GPU `BrushDist_BM`, and add square coverage. Do NOT change the
existing circle tests — they remain the circle-shape contract.

Add a shape enum mirror local to the test (or reuse `WorldPainter.Editor.BrushShape` — it is
`public`, so reference it directly) and a shape-aware falloff:

```csharp
// CPU mirror of BrushMask.hlsl BrushDist_BM + falloff, shape-aware.
// Circle = Euclidean; Square = Chebyshev max(|dx|,|dy|). Same falloff curve.
private static float BrushFalloffShaped(
    Vector2 texelUV, Vector2 centerUV, float radiusUV, BrushShape shape)
{
    Vector2 du = texelUV - centerUV;
    float dist = shape == BrushShape.Square
        ? Mathf.Max(Mathf.Abs(du.x), Mathf.Abs(du.y)) // Chebyshev → square
        : du.magnitude;                                // Euclidean → circle
    float t   = Mathf.Clamp01(dist / Mathf.Max(radiusUV, 0.0001f));
    float inv = 1f - t;
    return inv * inv * (3f - 2f * inv);
}
```

Add `using WorldPainter.Editor;` if not already present (the file already has
`using WorldPainter.Editor;` per its header — confirm; `BrushShape` lives there).

Add square parity tests (the falloff CURVE must be identical to circle along an axis where the two
metrics coincide, and the corner behaviour must differ — that difference IS the square contract):

```csharp
[Test]
public void Falloff_Square_OnAxis_MatchesCircle()
{
    // Along a cardinal axis, |du| == max(|dx|,|dy|), so Square and Circle agree exactly.
    var center = new Vector2(0.5f, 0.5f);
    float radius = 0.2f;
    var p = new Vector2(0.5f + 0.1f, 0.5f); // on the +X axis, dy = 0
    float circle = BrushFalloffShaped(p, center, radius, BrushShape.Circle);
    float square = BrushFalloffShaped(p, center, radius, BrushShape.Square);
    Assert.AreEqual(circle, square, 0.0001f, "On-axis, square must equal circle.");
}

[Test]
public void Falloff_Square_Diagonal_StaysInsideWhereCircleFallsOff()
{
    // At a diagonal point just outside the circle radius but inside the square half-extent,
    // Chebyshev keeps weight > 0 while Euclidean has already reached 0.
    var center = new Vector2(0.5f, 0.5f);
    float radius = 0.2f;
    // dx = dy = 0.18 → Euclidean dist ≈ 0.2546 (> radius → 0); Chebyshev dist = 0.18 (< radius).
    var p = new Vector2(0.5f + 0.18f, 0.5f + 0.18f);
    float circle = BrushFalloffShaped(p, center, radius, BrushShape.Circle);
    float square = BrushFalloffShaped(p, center, radius, BrushShape.Square);
    Assert.AreEqual(0f, circle, 0.0001f, "Diagonal corner is outside the circle.");
    Assert.Greater(square, 0f, "Diagonal corner is inside the square half-extent.");
}

[Test]
public void Falloff_Square_AtCorner_IsZeroAtHalfExtent()
{
    // Exactly at the square edge along an axis → t=1 → falloff 0 (same boundary rule as circle).
    var center = new Vector2(0.5f, 0.5f);
    float radius = 0.2f;
    var edge = new Vector2(0.5f + radius, 0.5f);
    float f = BrushFalloffShaped(edge, center, radius, BrushShape.Square);
    Assert.AreEqual(0f, f, 0.0001f);
}
```

These three tests pin: (a) on-axis circle≡square (curve identity), (b) the diagonal-corner
difference that defines "square", (c) boundary correctness. Together they enforce the CPU↔GPU
parity invariant the report mandates.

## Verify gate

1. `BrushMask.hlsl` + `TerrainBrush.compute` compile with no shader console errors (single
   `_BrushShape` declaration; no redefinition).
2. `TerrainBrushMathTests` runs GREEN for BOTH circle (existing tests unchanged) AND square (3 new
   tests). This is the hard gate — do not proceed to Phase 3 until it passes.
3. Manual sanity (optional, non-gating): paint with Square selected → the affected region is a
   square footprint, not a circle.

## Rollback

Revert all five files. The `_BrushShape` uniform defaults to 0 (Circle) if any consumer is missed,
so a partial revert degrades to circle behaviour rather than crashing — but revert as a set to keep
parity intact.
