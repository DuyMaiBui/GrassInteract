---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---
# LitMotion Core API

## LMotion.Create

Creates a `MotionBuilder` that animates a value from `start` to `end` over `duration` seconds.

```csharp
// Float
LMotion.Create(0f, 10f, 2f).Bind(x => Debug.Log(x));

// Vector3
LMotion.Create(Vector3.zero, Vector3.one, 1f).BindToPosition(transform);

// Color
LMotion.Create(Color.white, Color.red, 0.5f).BindToColor(spriteRenderer);

// Int
LMotion.Create(0, 100, 1f).Bind(x => score = x);
```

**Supported types:** `float`, `double`, `int`, `long`, `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Color`, `Rect`

### Zero-Allocation Binding (Avoid Closure Captures)

Pass the target as state to avoid closure allocations — critical in hot paths:

```csharp
// ❌ Captures transform in closure (allocates)
LMotion.Create(0f, 10f, 2f)
    .Bind(x => transform.position = new Vector3(x, 0f, 0f));

// ✅ Zero-allocation — state passed explicitly
LMotion.Create(0f, 10f, 2f)
    .Bind(transform, (x, t) => t.position = new Vector3(x, 0f, 0f));

// Multiple state arguments
LMotion.Create(0f, 10f, 2f)
    .BindWithState(text, format, (x, target, fmt) => target.SetTextFormat(fmt, x));
```

## MotionBuilder Chain

`LMotion.Create()` returns a `MotionBuilder<TValue, TOptions, TAdapter>`. Chain configuration then call `.Bind()` or `.BindTo*()`.

```csharp
LMotion.Create(0f, 1f, 0.5f)
    .WithEase(Ease.OutQuad)       // easing curve
    .WithDelay(0.2f)              // start delay
    .WithLoops(3, LoopType.Yoyo) // repeat 3x
    .WithOnComplete(() => Debug.Log("Done"))
    .Bind(x => alpha = x)
    .AddTo(gameObject);           // lifecycle binding
```

## MotionHandle

`.Bind()` and `.BindTo*()` return a `MotionHandle` struct for controlling active motions.

```csharp
var handle = LMotion.Create(0f, 1f, 1f).BindToPositionX(transform);

// Control
handle.Cancel();        // stop immediately
handle.Complete();      // jump to end value
handle.PlaybackSpeed = 2f; // double speed

// Status
bool active  = handle.IsActive();   // still running?
bool playing = handle.IsPlaying();  // active and not completed
float time   = handle.Time;         // current time

// Properties
float duration      = handle.Duration;       // duration per loop
float totalDuration = handle.TotalDuration;  // total including loops and delay
float delay         = handle.Delay;
int   loops         = handle.Loops;
int   completed     = handle.CompletedLoops;
```

**Important:** `MotionHandle` is a struct — store it if you need to cancel later. Calling `.Cancel()` on an inactive handle is safe (no-op).

## Binding Methods

### Lambda Bind
```csharp
LMotion.Create(0f, 1f, 1f).Bind(x => myField = x);
```

### BindTo Shortcuts (Transform)
```csharp
.BindToPosition(transform)        // Vector3 → position
.BindToPositionX/Y/Z(transform)       // Single axis
.BindToLocalPosition(transform)   // Vector3 → localPosition
.BindToLocalPositionX/Y/Z(transform)  // Single local axis
.BindToLocalScale(transform)      // Vector3 → localScale
.BindToLocalScaleX(transform)     // float → localScale.x
.BindToRotation(transform)        // Quaternion → rotation
.BindToEulerAngles(transform)     // Euler angles
.BindToLocalEulerAnglesZ(transform) // float → localEulerAngles.z
```

### BindTo Shortcuts (UI / Renderer)
```csharp
.BindToColor(renderer/graphic)            // Color → material.color / graphic.color
.BindToColorA(graphic)                    // float → color.a
.BindToColorR/G/B(graphic)                // float → color channel
.BindToAlpha(canvasGroup)                 // float → CanvasGroup.alpha
.BindToCanvasGroupAlpha(canvasGroup)      // alias for CanvasGroup
.BindToFillAmount(image)                  // float → Image.fillAmount
.BindToSliderValue(slider)                // float → Slider.value
.BindToSizeDelta(rectTransform)           // Vector2 → sizeDelta
.BindToAnchoredPosition(rectTf)           // Vector2 → anchoredPosition
.BindToAnchoredPositionX/Y(rectTf)        // float → anchoredPosition axis
.BindToText(tmpText)                      // string → TMP_Text.text
.BindToTMPTextColorAlpha(tmpText)         // float → TMP_Text color alpha
```

### BindWithState (Custom Interpolation)

Use `BindWithState` when you need to carry custom data into the bind callback without capturing closures (useful for zero-allocation hot paths or path animation):

```csharp
// Animate along a spline with custom state
var pathPoints = new Vector3[] { start, control, end };
LMotion.Create(0f, 1f, duration)
    .WithEase(Ease.InOutQuad)
    .BindWithState(pathPoints, (t, points) =>
    {
        transform.position = EvaluateSpline(points, t);
    });
```

**When to use:** Path animation, procedural animation, or any bind that needs auxiliary data. The state is passed as the first argument to avoid closure allocations.

## Lifecycle Management

**Auto-cancel when GameObject is destroyed:**

```csharp
LMotion.Create(0f, 1f, 1f)
    .Bind(x => val = x)
    .AddTo(gameObject);
```

**Manual cancellation (preferred for frequently disabled objects):**

```csharp
private MotionHandle handle;

void OnEnable()
{
    handle = LMotion.Create(0f, 1f, 1f).BindToAlpha(canvasGroup);
}

void OnDisable()
{
    if (handle.IsActive()) handle.Cancel();
}
```

**CancellationToken:**
```csharp
LMotion.Create(0f, 1f, 1f)
    .Bind(x => val = x)
    .AddTo(destroyCancellationToken);
```

## Gotchas

- Forgetting `.AddTo()` causes motions to leak if the target is destroyed mid-tween
- `MotionHandle` becomes invalid after the motion completes — check `.IsActive()` first
- `.Bind()` callback runs every frame — keep it lightweight (no allocations)
- `LMotion.Create` with mismatched types won't compile — use correct overload for Vector3, Color, etc.
- The single-arg `LMotion.Create(duration)` overload reads the current value at the moment of creation. If the target value changes between creation and bind, the start value will be stale.
