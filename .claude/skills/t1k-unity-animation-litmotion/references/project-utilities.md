---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---
# LitMotion Project Utilities

Reusable patterns and helper classes invented during DOTween → LitMotion migration.

---

## MotionPath — DOPath Replacement

LitMotion has no built-in path animation. This helper provides Catmull-Rom spline evaluation as a drop-in replacement for `DOPath`.

```csharp
using TheOne.Features.UI.Utilities;

// World-space path
var path = new Vector3[] { start, control, end };
var handle = MotionPath.CreatePathMotion(transform, path, duration, LitMotion.Ease.InOutQuad);
await handle.ToUniTask();

// Local-space path
var handle = MotionPath.CreateLocalPathMotion(transform, path, duration, LitMotion.Ease.InOutQuad);
```

### Full Implementation

```csharp
namespace TheOne.Features.UI.Utilities
{
    using System;
    using LitMotion;
    using UnityEngine;

    public static class MotionPath
    {
        public static MotionHandle CreatePathMotion(Transform transform, Vector3[] path, float duration, LitMotion.Ease ease = LitMotion.Ease.InOutQuad)
        {
            if (path == null || path.Length < 2)
                throw new ArgumentException("Path must contain at least 2 points.", nameof(path));

            return LMotion.Create(0f, 1f, duration)
                .WithEase(ease)
                .Bind(t => transform.position = EvaluateCatmullRom(path, t));
        }

        public static MotionHandle CreateLocalPathMotion(Transform transform, Vector3[] path, float duration, LitMotion.Ease ease = LitMotion.Ease.InOutQuad)
        {
            if (path == null || path.Length < 2)
                throw new ArgumentException("Path must contain at least 2 points.", nameof(path));

            return LMotion.Create(0f, 1f, duration)
                .WithEase(ease)
                .Bind(t => transform.localPosition = EvaluateCatmullRom(path, t));
        }

        private static Vector3 EvaluateCatmullRom(Vector3[] points, float t)
        {
            var numSegments = points.Length - 1;
            var clampedT    = Mathf.Clamp01(t);
            var segmentT    = clampedT * numSegments;
            var index       = Mathf.FloorToInt(segmentT);
            var localT      = segmentT - index;

            index = Mathf.Clamp(index, 0, numSegments - 1);

            var p0 = points[Mathf.Max(0, index - 1)];
            var p1 = points[index];
            var p2 = points[Mathf.Min(points.Length - 1, index + 1)];
            var p3 = points[Mathf.Min(points.Length - 1, index + 2)];

            var tt  = localT * localT;
            var ttt = tt * localT;

            var q0 = -ttt + 2f * tt - localT;
            var q1 = 3f * ttt - 5f * tt + 2f;
            var q2 = -3f * ttt + 4f * tt + localT;
            var q3 = ttt - tt;

            return 0.5f * (p0 * q0 + p1 * q1 + p2 * q2 + p3 * q3);
        }
    }
}
```

---

## EaseMapper — DOTween → LitMotion Enum Bridge

During migration, serialized fields and shared config may still reference `DG.Tweening.Ease`. This mapper converts between the two enums.

```csharp
namespace TheOne.Features.UI.Utilities
{
    public static class EaseMapper
    {
        public static LitMotion.Ease ToLitMotion(this DG.Tweening.Ease ease)
        {
            return ease switch
            {
                DG.Tweening.Ease.Linear        => LitMotion.Ease.Linear,
                DG.Tweening.Ease.InSine        => LitMotion.Ease.InSine,
                DG.Tweening.Ease.OutSine       => LitMotion.Ease.OutSine,
                DG.Tweening.Ease.InOutSine     => LitMotion.Ease.InOutSine,
                DG.Tweening.Ease.InQuad        => LitMotion.Ease.InQuad,
                DG.Tweening.Ease.OutQuad       => LitMotion.Ease.OutQuad,
                DG.Tweening.Ease.InOutQuad     => LitMotion.Ease.InOutQuad,
                DG.Tweening.Ease.InCubic       => LitMotion.Ease.InCubic,
                DG.Tweening.Ease.OutCubic      => LitMotion.Ease.OutCubic,
                DG.Tweening.Ease.InOutCubic    => LitMotion.Ease.InOutCubic,
                DG.Tweening.Ease.InQuart       => LitMotion.Ease.InQuart,
                DG.Tweening.Ease.OutQuart      => LitMotion.Ease.OutQuart,
                DG.Tweening.Ease.InOutQuart    => LitMotion.Ease.InOutQuart,
                DG.Tweening.Ease.InQuint       => LitMotion.Ease.InQuint,
                DG.Tweening.Ease.OutQuint      => LitMotion.Ease.OutQuint,
                DG.Tweening.Ease.InOutQuint    => LitMotion.Ease.InOutQuint,
                DG.Tweening.Ease.InExpo        => LitMotion.Ease.InExpo,
                DG.Tweening.Ease.OutExpo       => LitMotion.Ease.OutExpo,
                DG.Tweening.Ease.InOutExpo     => LitMotion.Ease.InOutExpo,
                DG.Tweening.Ease.InCirc        => LitMotion.Ease.InCirc,
                DG.Tweening.Ease.OutCirc       => LitMotion.Ease.OutCirc,
                DG.Tweening.Ease.InOutCirc     => LitMotion.Ease.InOutCirc,
                DG.Tweening.Ease.InElastic     => LitMotion.Ease.InElastic,
                DG.Tweening.Ease.OutElastic    => LitMotion.Ease.OutElastic,
                DG.Tweening.Ease.InOutElastic  => LitMotion.Ease.InOutElastic,
                DG.Tweening.Ease.InBack        => LitMotion.Ease.InBack,
                DG.Tweening.Ease.OutBack       => LitMotion.Ease.OutBack,
                DG.Tweening.Ease.InOutBack     => LitMotion.Ease.InOutBack,
                DG.Tweening.Ease.InBounce      => LitMotion.Ease.InBounce,
                DG.Tweening.Ease.OutBounce     => LitMotion.Ease.OutBounce,
                DG.Tweening.Ease.InOutBounce   => LitMotion.Ease.InOutBounce,
                _                              => LitMotion.Ease.Linear,
            };
        }
    }
}
```

**Usage:**
```csharp
[SerializeField] private DG.Tweening.Ease legacyEase;  // existing serialized field

LMotion.Create(0f, 1f, 1f)
    .WithEase(this.legacyEase.ToLitMotion())
    .BindToAlpha(canvasGroup);
```

---

## LitMotionSequenceExtensions — Callbacks for LSequence

Core `LSequence` has no `.AppendCallback()`. These extensions add callback support using zero-duration motions.

```csharp
namespace TheOne.Features.UI.Utilities
{
    using System;
    using LitMotion;

    public static class LitMotionSequenceExtensions
    {
        public static MotionSequenceBuilder AppendCallback(this MotionSequenceBuilder sequence, Action callback)
        {
            return sequence.Append(LMotion.Create(0f, 1f, 0.0001f)
                .WithOnComplete(callback)
                .Bind(_ => { }));
        }

        public static MotionSequenceBuilder JoinCallback(this MotionSequenceBuilder sequence, Action callback)
        {
            return sequence.Join(LMotion.Create(0f, 1f, 0.0001f)
                .WithOnComplete(callback)
                .Bind(_ => { }));
        }

        public static MotionSequenceBuilder InsertCallback(this MotionSequenceBuilder sequence, float position, Action callback)
        {
            return sequence.Insert(position, LMotion.Create(0f, 1f, 0.0001f)
                .WithOnComplete(callback)
                .Bind(_ => { }));
        }
    }
}
```

**Usage:**
```csharp
LSequence.Create()
    .Append(LMotion.Create(from, to, 1f).BindToPosition(transform))
    .AppendCallback(() => PlaySfx("step"))
    .Append(LMotion.Create(to, final, 1f).BindToPosition(transform))
    .Run();
```

**Caveat:** Callbacks use a near-zero-duration motion (`0.0001f`). In extreme edge cases with very high timeScale, this may complete on the same frame. For guaranteed next-frame callback, prefer async/await pattern instead.

---

## ManualMotionDispatcher — Editor / Manual Updates

For editor tooling or custom update loops, use the manual dispatcher:

```csharp
void Awake()
{
    // Reset to prevent unexpected behavior with domain reloads
    ManualMotionDispatcher.Default.Reset();
}

void Update()
{
    // Drive motion updates manually (e.g. in editor mode)
    ManualMotionDispatcher.Default.Update(Time.deltaTime);
}
```

**v2 breaking change:** In v1, `ManualMotionDispatcher.Update(0.1)` was a static method. In v2, use `ManualMotionDispatcher.Default.Update(0.1)`.
