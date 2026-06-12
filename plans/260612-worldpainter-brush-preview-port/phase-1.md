# Phase 1 — Brush shape SSOT (enum + field + UI toggle)

Effort: **S** · Blocks: Phase 2, Phase 3 · Blocked by: none

## Goal

Introduce the `BrushShape { Circle, Square }` enum and a `shape` field on the unified
`BrushSettings` SSOT, plus a Circle/Square toggle row in the brush dock UI. This is the single
source of truth every later phase reads (`(int)brush.shape` on the GPU side, `brush.shape` at the
preview `Set` call site). Default = `Circle` so existing serialized state is unchanged.

## File ownership

- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterState.cs` (add enum + field)
- `Assets/WorldPainter/Editor/WorldPainter/WorldPainterBrushDock.cs` (add toggle row)

No other file is touched in this phase.

## Exact edits

### 1. `WorldPainterState.cs` — add `BrushShape` enum + `shape` field on `BrushSettings`

`BrushSettings` is the `[System.Serializable] public sealed class` at ~line 238. Add the enum
adjacent to it (top-level in the `WorldPainter.Editor` namespace, before or after `BrushSettings`)
and the field inside the class.

Add the enum (place it just above the `// ── BrushSettings ──` comment, ~line 231):

```csharp
/// <summary>Brush footprint shape. Drives both the editor preview AND the GPU stamp mask.</summary>
public enum BrushShape
{
    Circle = 0,
    Square = 1,
}
```

Add the field inside `BrushSettings`, after the `flow` field (~line 262, before the `Default`
property):

```csharp
[Tooltip("Brush footprint shape — circle (Euclidean falloff) or square (Chebyshev falloff).")]
public BrushShape shape = BrushShape.Circle;
```

Notes:
- Enum values are explicit (`Circle = 0`, `Square = 1`) because Phase 2 casts `(int)brush.shape`
  straight into `SetInt("_BrushShape", …)` — the integer contract must be stable.
- Keep the namespace exactly `WorldPainter.Editor` (matches the existing file). `BrushShape` is
  referenced unqualified by `WorldPainterBrushDock`, `WorldPainterSculptTool.*`, and
  `TerrainBrushPreview` (all in `WorldPainter.Editor`).

### 2. `WorldPainterBrushDock.cs` — add a Circle/Square toggle row

In `Build()` (~line 53), insert a shape-toggle row. Place it right after the mode toggle and
before the Size slider (after `dock.Add(this.BuildModeToggle());` at ~line 67):

```csharp
// Shape toggle (Circle / Square)
dock.Add(this.BuildShapeToggle());
```

Add the builder method, modelled on the existing `BuildModeToggle()` (~line 103) so it matches the
dock's button styling and live-highlight pattern:

```csharp
// ── Shape toggle (Circle / Square) ────────────────────────────────────
private VisualElement BuildShapeToggle()
{
    var row = new VisualElement();
    row.AddToClassList("wp-mode-toggle-row");
    row.style.flexDirection = FlexDirection.Row;
    row.style.marginBottom  = 4;

    var shapes = new[] { "Circle", "Square" };
    var values = new[] { BrushShape.Circle, BrushShape.Square };

    for (int i = 0; i < shapes.Length; i++)
    {
        int capturedIdx = i;
        var btn = new Button(() =>
        {
            WorldPainterState.Brush.shape = values[capturedIdx];
            WorldPainterState.RaiseBrushFalloffDirty(); // nudge repaint/preview refresh
        });
        btn.text = shapes[i];
        btn.AddToClassList("wp-mode-btn");
        btn.style.flexGrow = 1;

        if (WorldPainterState.Brush.shape == values[i])
            btn.AddToClassList("wp-mode-btn--active");

        row.Add(btn);
    }

    return row;
}
```

Notes:
- `BrushShape` is in the same `WorldPainter.Editor` namespace → unqualified reference compiles.
- Reuse existing USS classes `wp-mode-toggle-row`, `wp-mode-btn`, `wp-mode-btn--active` (already
  styled for the Height/Splat/Density row) — no new USS needed.
- `RaiseBrushFalloffDirty()` already exists and is the dock's standard "brush params changed"
  signal (used by the falloff CurveField and stamp strip). Using it here keeps the dock consistent;
  it does not re-upload the LUT incorrectly because shape is read live at dispatch time.
- The active-highlight is evaluated at build time (matches `BuildModeToggle`'s behaviour). The dock
  rebuilds on activation; a live re-highlight on click is out of scope (matches existing toggle
  behaviour — do not add new rebuild plumbing).

## Verify gate

1. Unity recompiles with zero console errors (`read_console` clean).
2. Open the WorldPainter brush dock — a "Circle | Square" toggle row is visible below the
   Height/Splat/Density row and above the Size slider.
3. Clicking Square sets `WorldPainterState.Brush.shape == BrushShape.Square`; clicking Circle sets
   it back. (Confirm via the active-class highlight after a dock rebuild, or a quick log.)
4. No behaviour change to sculpting yet (Phase 2 wires the GPU side; Phase 3 wires the preview).

## Rollback

Revert the two files. The enum + field are additive; removing them restores the prior
`BrushSettings` exactly. No serialized-data migration risk (new field defaults to Circle).
