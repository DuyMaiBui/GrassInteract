# Phase 3 — Preview rewrite (pure Handles ring + convex-poly fill)

Effort: **M** · Blocks: Phase 4 · Blocked by: Phase 1, Phase 2

## Goal

Replace `TerrainBrushPreview`'s tessellated-disc-mesh + `BrushDecal` shader + procedural texture +
`Graphics.DrawMeshNow` rendering with the MegaWorld pure-Handles technique:
- **Ring**: build N perimeter points (circle = adaptive segments; square = sampled along 4 OBB
  edges), conform Y via the existing `HeightFn` + lift offset, close the loop, draw
  `Handles.DrawAAPolyLine` TWICE — black ~8px then tint color ~4px — with `Handles.zTest = Always`.
- **Fill**: `Handles.DrawAAConvexPolygon` over the SAME conformed perimeter points at low alpha,
  `Handles.zTest = Always`.

Keep the lift-offset, freshness-timeout, and finite/`MAX_HIT_SQR` guards from the old impl. Add a
`shape` parameter to `Set(...)` and pass `brush.shape` at the single call site. Phase 2 already made
the GPU stamp honour the shape, so this preview now visually matches the affected region.

## File ownership

- `Assets/WorldPainter/Editor/Brush/TerrainBrushPreview.cs` (full rewrite)
- `Assets/WorldPainter/Editor/Brush/WorldPainterSculptTool.cs` (`Set` call site ONLY, ~line 180)

No other file is touched. `BrushDecal.shader` is NOT deleted here (that is Phase 4) — but after
this rewrite it has zero referencers.

## Exact edits

### 1. `TerrainBrushPreview.cs` — full rewrite to Handles

Rewrite the class. Preserve these elements (do NOT drop them):

- `[InitializeOnLoad]` static ctor subscribing `SceneView.duringSceneGui += OnSceneGui` and
  `AssemblyReloadEvents.beforeAssemblyReload += Cleanup`.
- The `HeightFn` delegate (`bool (float worldX, float worldZ, out float worldY)`) — unchanged
  signature; the call site passes `s_heightFn`.
- The lift constants `Y_OFFSET_MIN = 0.15f`, `Y_OFFSET_FRACTION = 0.15f`, and the
  `lift = Mathf.Max(Y_OFFSET_MIN, brushRadius * Y_OFFSET_FRACTION)` computation.
- The `FRESH_SECONDS = 0.25` freshness timeout and `lastSetTime` gate in `OnSceneGui`.
- The finite / `MAX_HIT_SQR (1e12f)` / `brushRadius > 0` guards before drawing.
- The `EventType.Repaint`-only draw gate and the `sceneView.Repaint()` tail.

REMOVE (per locked decision): `discMesh`, `discMaterial`, `discTexture`, `unitOffsets`,
`worldVerts`, `CreateUnitDisc`, `CreateMaterial`, `EnsureDiscTexture`, `DestroyResources`'s
mesh/material/texture destroys, `DECAL_SHADER` const, `DISC_TEX_SIZE`, `RINGS`, `SEGMENTS`,
`Graphics.DrawMeshNow`, the `warnLogged` shader-missing path. No mesh, no material, no texture, no
shader lookup remain.

New `Set` signature (adds trailing `shape`):

```csharp
internal static void Set(Vector3 worldPoint, float radius, Color tint, BrushShape shape, HeightFn? height)
{
    hitPoint    = worldPoint;
    brushRadius = radius;
    tintColor   = tint;
    brushShape  = shape;
    heightAt    = height;
    lastSetTime = EditorApplication.timeSinceStartup;
}
```

Add a `private static BrushShape brushShape;` field alongside the existing brush-state fields.

New constants for the Handles look (replace the removed mesh constants):

```csharp
private const int   CIRCLE_SEGMENTS_MIN = 16;
private const int   CIRCLE_SEGMENTS_MAX = 128;
private const int   SQUARE_SAMPLES_PER_EDGE = 24; // points sampled along each of the 4 edges
private const float OUTLINE_BLACK_WIDTH = 8f;     // px, drawn first (halo)
private const float OUTLINE_COLOR_WIDTH = 4f;     // px, drawn over black
private const float FILL_ALPHA          = 0.10f;  // faint convex-poly fill
private const float OUTLINE_BLACK_ALPHA = 0.6f;
```

New `OnSceneGui` (keep the gates; replace the draw body). Build a closed conformed perimeter into a
reused `List<Vector3>` (no per-frame alloc — cache a static `List<Vector3> perimeter`):

```csharp
private static readonly System.Collections.Generic.List<Vector3> perimeter = new(160);

private static void OnSceneGui(SceneView sceneView)
{
    if (EditorApplication.timeSinceStartup - lastSetTime > FRESH_SECONDS) return;
    if (Event.current.type != EventType.Repaint) return;

    if (!IsFinite(hitPoint) || hitPoint.sqrMagnitude > MAX_HIT_SQR ||
        !IsFinite(brushRadius) || brushRadius <= 0f)
        return;

    BuildPerimeter(); // fills `perimeter` with conformed, lifted, closed-loop points
    if (perimeter.Count < 3) return;

    // Faint fill first (drawn under the ring), zTest Always so it shows through terrain.
    Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
    var fill = tintColor; fill.a = FILL_ALPHA;
    Handles.color = fill;
    Handles.DrawAAConvexPolygon(perimeter.ToArray());

    // Ring: double stroke — black halo then tint color (MegaWorld technique).
    var black = new Color(0f, 0f, 0f, OUTLINE_BLACK_ALPHA);
    Handles.color = black;
    Handles.DrawAAPolyLine(OUTLINE_BLACK_WIDTH, perimeter.ToArray());
    var ring = tintColor; ring.a = 1f;
    Handles.color = ring;
    Handles.DrawAAPolyLine(OUTLINE_COLOR_WIDTH, perimeter.ToArray());

    sceneView.Repaint();
}
```

`BuildPerimeter` — circle = adaptive segments by world perimeter length; square = 4 OBB edges
sampled. Both conform Y via `HeightFn` + lift, and CLOSE the loop (append first point last so the
polyline/fill is closed):

```csharp
private static void BuildPerimeter()
{
    perimeter.Clear();
    float radius   = brushRadius;
    float lift     = Mathf.Max(Y_OFFSET_MIN, radius * Y_OFFSET_FRACTION);

    if (brushShape == BrushShape.Square)
    {
        // Square OBB in the XZ plane, half-extent = radius (matches the GPU Chebyshev half-extent).
        // Corners CCW: (-r,-r) (+r,-r) (+r,+r) (-r,+r), each edge sampled SQUARE_SAMPLES_PER_EDGE times.
        Vector2[] corners =
        {
            new(-radius, -radius), new(radius, -radius),
            new(radius,  radius),  new(-radius, radius),
        };
        for (int e = 0; e < 4; ++e)
        {
            Vector2 a = corners[e];
            Vector2 b = corners[(e + 1) % 4];
            for (int s = 0; s < SQUARE_SAMPLES_PER_EDGE; ++s)
            {
                float u  = s / (float)SQUARE_SAMPLES_PER_EDGE;
                Vector2 o = Vector2.Lerp(a, b, u);
                AppendConformed(o.x, o.y, lift);
            }
        }
    }
    else
    {
        // Circle: adaptive segment count from world-space circumference (MegaWorld-style).
        float circumference = 2f * Mathf.PI * radius;
        int segments = Mathf.Clamp(
            Mathf.CeilToInt(circumference / 1.5f), // ~1 point per 1.5 m
            CIRCLE_SEGMENTS_MIN, CIRCLE_SEGMENTS_MAX);
        for (int i = 0; i < segments; ++i)
        {
            float ang = (i / (float)segments) * Mathf.PI * 2f;
            AppendConformed(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, lift);
        }
    }

    // Close the loop.
    if (perimeter.Count > 0)
        perimeter.Add(perimeter[0]);
}

private static void AppendConformed(float offX, float offZ, float lift)
{
    float wx = hitPoint.x + offX;
    float wz = hitPoint.z + offZ;
    float wy = heightAt != null && heightAt(wx, wz, out float sampled)
        ? sampled
        : hitPoint.y; // off-tile / no sampler → flat fallback at hit height
    perimeter.Add(new Vector3(wx, wy + lift, wz));
}
```

`Cleanup`/`DestroyResources` — simplify to just unsubscribe events and `perimeter.Clear()` (no
mesh/material/texture to destroy). Keep the `IsFinite(float)` / `IsFinite(Vector3)` helpers.

Notes:
- `BrushShape` is in `WorldPainter.Editor` (same namespace) → no extra using.
- The `.ToArray()` calls allocate per draw; acceptable for an editor-only hover gizmo. If perf is a
  concern, cache a `Vector3[]` sized to max points — but that is an optimization, not required by
  the verify gate. Keep it simple (KISS) unless profiling shows a problem.
- Half-extent = `radius` for the square so the preview edge lands exactly where the GPU Chebyshev
  mask reaches `radiusUV` along an axis — preview == affected region.

### 2. `WorldPainterSculptTool.cs` — update the `Set` call site

At ~line 180 the current call is:

```csharp
TerrainBrushPreview.Set(worldPoint, brush.size, previewColor, s_heightFn);
```

Change to pass `brush.shape` (the new arg goes before `s_heightFn`, matching the new signature):

```csharp
TerrainBrushPreview.Set(worldPoint, brush.size, previewColor, brush.shape, s_heightFn);
```

`brush` is the local `var brush = WorldPainterState.Brush;` already at ~line 178. No other call site
exists (confirmed: `TerrainBrushPreview.Set` is referenced only here).

## Verify gate

1. Unity recompiles with zero console errors.
2. Hover the brush over terrain with **Circle** selected: a double-stroke ring (black halo + blue)
   hugs the surface with a faint fill, visible through ridges (zTest Always), at varied zoom and
   brush size, with no float/clip and no "abnormal mesh bounds" warnings.
3. Switch to **Square**: the cursor is a square ring whose footprint matches the square region the
   GPU stamp edits (paint a stroke and confirm cursor == affected area).
4. Moving the mouse away for >0.25 s hides the cursor (freshness timeout preserved).

## Rollback

Revert both files. `git checkout` `TerrainBrushPreview.cs` restores the mesh/shader path; reverting
the one-line call site restores the 4-arg `Set`. `BrushDecal.shader` still exists (Phase 4 not yet
run) so the reverted mesh path still finds its shader.
