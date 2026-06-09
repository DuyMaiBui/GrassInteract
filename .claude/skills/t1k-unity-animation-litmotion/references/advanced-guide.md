---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---
# LitMotion Advanced Features

## Sequences (LSequence)

```csharp
// Sequential
LSequence.Create()
    .Append(LMotion.Create(Vector3.zero, Vector3.right * 5, 0.5f).BindToPosition(transform))
    .Append(LMotion.Create(Vector3.one, Vector3.one * 1.5f, 0.3f).BindToLocalScale(transform))
    .Run().AddTo(gameObject);

// Parallel
LSequence.Create()
    .Join(LMotion.Create(0f, 1f, 0.3f).BindToAlpha(canvasGroup))
    .Join(LMotion.Create(Vector3.zero, Vector3.one, 0.3f).BindToLocalScale(transform))
    .Run().AddTo(gameObject);

// Insert at absolute time
LSequence.Create()
    .Append(LMotion.Create(0f, 1f, 1f).BindToPositionX(transform))
    .Insert(0.5f, LMotion.Create(0f, 1f, 1f).BindToPositionY(transform))  // starts at absolute 0.5s
    .Run();
```

Methods: `.Append(handle)` sequential, `.Join(handle)` parallel (with previous Append), `.Insert(float time, handle)` at absolute time, `.AppendInterval(s)` pause.

**Note on callbacks:** Core `LSequence` does not have built-in `.AppendCallback()`. The project provides extension methods in `LitMotionSequenceExtensions` (see [project-utilities.md](project-utilities.md)). If those extensions are not available in your project, use one of these patterns:

```csharp
// Pattern A: end a motion with OnComplete, then Append the next
LSequence.Create()
    .Append(LMotion.Create(0f, 1f, 0.5f)
        .WithOnComplete(() => PlaySfx("step"))
        .BindToPositionX(transform))
    .Append(LMotion.Create(1f, 2f, 0.5f).BindToPositionX(transform))
    .Run();

// Pattern B: async/await between motions (preferred for complex flows)
await LMotion.Create(from, p1, 1f).BindToPosition(transform);
PlaySfx("step");
await LMotion.Create(p1, p2, 1f).BindToPosition(transform);
```

## Punch & Shake

```csharp
// Punch: regular damping oscillation from a start value
LMotion.Punch.Create(0f, 1.5f, 0.6f)
    .WithFrequency(15)
    .WithDampingRatio(0.8f)
    .BindToLocalScaleX(target);

// Shake: randomized oscillations
LMotion.Shake.Create(Vector3.zero, Vector3.one * 0.3f, 0.5f)
    .WithFrequency(20)
    .WithDampingRatio(0f)
    .WithRandomSeed(42)
    .BindToPosition(target);
```

**Signature:** `LMotion.Punch.Create(startValue, strength, duration)` / `LMotion.Shake.Create(startValue, strength, duration)`

**Configuration:**
| Method | Description |
|--------|-------------|
| `.WithFrequency(int)` | Oscillation count |
| `.WithDampingRatio(float)` | Damping — `0f` = no damping, `1f` = heavy damping |
| `.WithRandomSeed(int)` | Reproducible random (Shake only) |

Note: Punch/Shake animate **from the start value** with oscillation. The final value returns toward the start value as damping takes effect.

## TextMeshPro Animation

```csharp
// Fade in each character sequentially
for (int i = 0; i < text.textInfo.characterCount; i++)
    LMotion.Create(0f, 1f, 0.3f).WithDelay(i * 0.05f).WithEase(Ease.OutQuad)
        .BindToTMPCharColor(text, i).AddTo(gameObject);

// Wave effect
for (int i = 0; i < text.textInfo.characterCount; i++)
    LMotion.Create(Vector3.zero, Vector3.up * 10f, 0.5f)
        .WithDelay(i * 0.08f).WithLoops(-1, LoopType.Yoyo)
        .BindToTMPCharPosition(text, i).AddTo(gameObject);
```

Requires `text.ForceMeshUpdate()` if text changes dynamically before animating.

## String Animation

```csharp
// Zero-allocation text interpolation (128-byte buffer)
LMotion.String.Create128Bytes("", "Hello World!", 1f).BindToText(tmpText).AddTo(gameObject);
```

## Async/Await (UniTask)

```csharp
await LMotion.Create(0f, 1f, 0.5f).BindToAlpha(canvasGroup).ToUniTask(cancellationToken);
```

Requires separate package: `com.annulusgames.lit-motion.unitask`

## SerializableMotionSettings — Reusable Configurations

Convert a builder to reusable settings, or create from Inspector:

```csharp
[SerializeField] private SerializableMotionSettings<float, NoOptions> fadeSettings;

// Use directly from serialized field
LMotion.Create(this.fadeSettings).BindToAlpha(canvasGroup);

// Convert builder to settings for reuse
var baseMove = LMotion.Create(Vector3.zero, Vector3.up * 5f, 1f)
    .WithEase(Ease.OutBack)
    .ToMotionSettings();

// Clone with modifications (C# 9 record-like syntax)
var fastMove = baseMove with { Duration = 0.3f };
LMotion.Create(fastMove).BindToPosition(transform);
```

Requires `LitMotion.Animation` package for `SerializableMotionSettings`.

## LitMotion.Animation — Inspector Workflow

The `LitMotion.Animation` package lets designers author tweens directly in the Inspector without writing code.

```csharp
[SerializeField] private SerializableMotionSettings<float, NoOptions> fadeSettings;
```

Create a `MotionBehaviour` component in the Inspector, configure start/end values, duration, easing, and binding target. Useful for UI transitions and simple object animations that don't need runtime logic.

**Installation:** Add `com.annulusgames.lit-motion.animation` to manifest.

## Custom Adapter

```csharp
// Required for Burst-compiled custom adapters
[assembly: RegisterGenericJobType(typeof(MotionUpdateJob<Vector3, NoOptions, Vector3MotionAdapter>))]

public readonly struct Vector3MotionAdapter : IMotionAdapter<Vector3, NoOptions>
{
    public Vector3 Evaluate(ref Vector3 startValue, ref Vector3 endValue,
        ref NoOptions options, in MotionEvaluationContext context)
    {
        return Vector3.LerpUnclamped(startValue, endValue, context.Progress);
    }
}

// Usage
LMotion.Create<Vector3, NoOptions, Vector3MotionAdapter>(from, to, duration)
    .BindToPosition(transform);
```

**Requirements:**
- Adapter must be `readonly struct`
- Add `[assembly: RegisterGenericJobType(...)]` for Burst compatibility
- Use `Vector3.LerpUnclamped` (not `Lerp`) so overshoot eases like `OutBack` work correctly

## Debugging

```csharp
MotionTracker.EnableTracking = true;
// Window → LitMotion → Motion Debugger to view active motions
```

## Gotchas

- Don't reuse MotionBuilders after `.Append()`/`.Join()` in sequences
- UniTask integration is a separate package install
- Sequences run already-built motions — build the motion handle first, then pass to sequence
- `SerializableMotionSettings` requires the `LitMotion.Animation` package
