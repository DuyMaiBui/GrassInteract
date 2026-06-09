---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---
# DOTween Pro → LitMotion Migration Guide

Based on [official LitMotion migration docs](https://github.com/annulusgames/litmotion/blob/main/docs/articles/en/migrate-from-dotween.md).

## Why Migrate

| Metric | DOTween Pro | LitMotion |
|--------|-------------|-----------|
| Allocation per tween | ~200B managed | **Zero** (struct-based) |
| Performance | Baseline | **2-20x faster** |
| Burst/Jobs | No | **Yes** (internal) |
| License | Paid ($15 Pro) | Free (MIT) |
| GC per frame (100 tweens) | ~20KB | **0KB** |
| Active maintenance | Slow | Active |

## API Mapping Table

### Transform Shortcuts

| DOTween Pro | LitMotion |
|-------------|-----------|
| `transform.DOMove(to, dur)` | `LMotion.Create(transform.position, to, dur).BindToPosition(transform)` |
| `transform.DOMoveX(to, dur)` | `LMotion.Create(pos.x, to, dur).BindToPositionX(transform)` |
| `transform.DOMoveY(to, dur)` | `LMotion.Create(pos.y, to, dur).BindToPositionY(transform)` |
| `transform.DOMoveZ(to, dur)` | `LMotion.Create(pos.z, to, dur).BindToPositionZ(transform)` |
| `transform.DOLocalMove(to, dur)` | `LMotion.Create(lp, to, dur).BindToLocalPosition(transform)` |
| `transform.DOScale(to, dur)` | `LMotion.Create(scale, to, dur).BindToLocalScale(transform)` |
| `transform.DOScaleX(to, dur)` | `LMotion.Create(s.x, to, dur).BindToLocalScaleX(transform)` |
| `transform.DORotate(to, dur)` | `LMotion.Create(euler, to, dur).BindToEulerAngles(transform)` |
| `transform.DOLocalRotate(to, dur)` | `LMotion.Create(le, to, dur).BindToLocalEulerAngles(transform)` |

**Alternative using single-arg overload (reads current value):**
```csharp
// Instead of LMotion.Create(transform.position, to, dur).BindToPosition(transform)
LMotion.Create(dur).BindToPosition(transform);  // animate from current position to target
```

### UI Shortcuts

| DOTween Pro | LitMotion |
|-------------|-----------|
| `canvasGroup.DOFade(to, dur)` | `LMotion.Create(cg.alpha, to, dur).BindToAlpha(canvasGroup)` |
| `image.DOColor(to, dur)` | `LMotion.Create(img.color, to, dur).BindToColor(image)` |
| `image.DOFade(to, dur)` | `LMotion.Create(img.color.a, to, dur).BindToColorA(image)` |
| `image.DOFillAmount(to, dur)` | `LMotion.Create(img.fillAmount, to, dur).BindToFillAmount(image)` |
| `text.DOText(to, dur)` | `LMotion.Create(0, to.Length, dur).Bind(i => text.text = to[..i])` |
| `slider.DOValue(to, dur)` | `LMotion.Create(slider.value, to, dur).BindToSliderValue(slider)` |
| `rectTransform.DOAnchorPos(to, dur)` | `LMotion.Create(rt.anchoredPosition, to, dur).BindToAnchoredPosition(rt)` |
| `rectTransform.DOAnchorPosY(to, dur)` | `LMotion.Create(rt.anchoredPosition.y, to, dur).BindToAnchoredPositionY(rt)` |

### Value Tweens

```csharp
// DOTween: generic value tween
var value = 0f;
DOTween.To(() => value, x => value = x, 10f, 2f);

// LitMotion: cleaner, zero-alloc
LMotion.Create(0f, 10f, 2f)
    .Bind(x => value = x);
```

### Punch & Shake

```csharp
// DOTween
transform.DOPunchPosition(new Vector3(0, 1, 0), 0.5f);
transform.DOShakePosition(0.5f, 0.3f);

// LitMotion
LMotion.Punch.Create(new Vector3(0, 1, 0), 0.5f)
    .BindToPosition(transform);
LMotion.Shake.Create(0.3f, 0.5f)
    .BindToPosition(transform);
```

### Sequences

```csharp
// DOTween
var seq = DOTween.Sequence();
seq.Append(transform.DOMove(pos1, 1f));
seq.Join(transform.DOScale(2f, 1f));
seq.Append(transform.DOMove(pos2, 1f));
seq.AppendInterval(0.5f);

// LitMotion
LSequence.Create()
    .Append(LMotion.Create(pos0, pos1, 1f).BindToPosition(transform))
    .Join(LMotion.Create(Vector3.one, Vector3.one * 2f, 1f).BindToLocalScale(transform))
    .Append(LMotion.Create(pos1, pos2, 1f).BindToPosition(transform))
    .AppendInterval(0.5f)
    .Run();  // IMPORTANT: must call .Run()
```

**Callbacks in sequences:** Core `LSequence` has no `.AppendCallback()`. Use one of:
1. Project extension `LitMotionSequenceExtensions.AppendCallback()` (see [project-utilities.md](project-utilities.md))
2. `.WithOnComplete()` on the motion before appending
3. Async/await between motions

### Configuration

| DOTween Pro | LitMotion |
|-------------|-----------|
| `.SetEase(Ease.OutQuad)` | `.WithEase(Ease.OutQuad)` |
| `.SetDelay(0.5f)` | `.WithDelay(0.5f)` |
| `.SetLoops(3, LoopType.Yoyo)` | `.WithLoops(3, LoopType.Yoyo)` |
| `.OnComplete(() => ...)` | `.WithOnComplete(() => ...)` |
| `.OnUpdate(() => ...)` | Use `.Bind(x => { ...; })` |
| `.SetUpdate(true)` | `.WithScheduler(MotionScheduler.UnscaledUpdate)` |
| `.From()` | Swap start/end: `LMotion.Create(end, start, dur)` |
| `.Kill()` | `handle.Cancel()` |
| `.Complete()` | `handle.Complete()` |

### Lifecycle

```csharp
// DOTween: auto-kill by default, SetAutoKill(false) to keep
transform.DOMove(to, 1f).SetAutoKill(false);

// LitMotion: use .AddTo() for lifecycle management
LMotion.Create(from, to, 1f)
    .BindToPosition(transform)
    .AddTo(gameObject);  // auto-cancel on destroy

// For frequently disabled objects: manual cancellation
private MotionHandle handle;
void OnEnable() { handle = LMotion.Create(...).BindToPosition(transform); }
void OnDisable() { if (handle.IsActive()) handle.Cancel(); }
```

## Unsupported DOTween APIs — Workarounds

LitMotion intentionally omits a few DOTween APIs. Sources: [LitMotion FAQ](https://annulusgames.github.io/LitMotion/articles/en/faq.html), [official migration guide](https://annulusgames.github.io/LitMotion/articles/en/migrate-from-dotween.html).

### `DelayedCall(duration, action)` — no direct equivalent

The library author's stance: callback-based delays swallow exceptions and complicate error handling — prefer `async/await`. If you must port mechanically, use the FAQ workaround:

```csharp
// DOTween
DOVirtual.DelayedCall(0.5f, () => DoThing());

// LitMotion — workaround (callback path)
LMotion.Create(0f, 1f, 0.5f)
    .WithOnComplete(() => DoThing())
    .RunWithoutBinding();

// LitMotion — preferred (async path, exceptions propagate)
await LMotion.Create(0f, 1f, 0.5f).RunWithoutBinding();
DoThing();
```

### `SetSpeedBased()` — compute duration manually

DOTween's `SetSpeedBased()` reinterprets the duration arg as units-per-second. LitMotion has no equivalent — calculate the duration yourself before `LMotion.Create`:

```csharp
// DOTween
transform.DOMove(target, speed).SetSpeedBased();

// LitMotion
var duration = Vector3.Distance(transform.position, target) / speed;
LMotion.Create(transform.position, target, duration)
    .BindToPosition(transform);
```

### `DOPath()` — use custom `MotionPath` helper

DOTween's `DOPath()` (Pro) has no LitMotion equivalent. The project uses a custom `MotionPath` helper that wraps Catmull-Rom spline evaluation:

```csharp
using TheOne.Features.UI.Utilities;

var path = new Vector3[] { start, control, end };
var handle = MotionPath.CreatePathMotion(transform, path, duration, Ease.InOutQuad);
await handle.ToUniTask();
```

See [project-utilities.md](project-utilities.md) for the full `MotionPath` implementation. Alternatively, use Unity Splines (`com.unity.splines`) and animate a normalized `t` parameter via `Bind()`.

### `Sequence.AppendCallback(action)` — see project utilities

Use the project's `LitMotionSequenceExtensions.AppendCallback()` extension, or replace with async/await flow:

```csharp
// DOTween
DOTween.Sequence()
    .Append(transform.DOMove(p1, 1f))
    .AppendCallback(() => PlaySfx("step"))
    .Append(transform.DOMove(p2, 1f));

// LitMotion with extensions
LSequence.Create()
    .Append(LMotion.Create(transform.position, p1, 1f).BindToPosition(transform))
    .AppendCallback(() => PlaySfx("step"))
    .Append(LMotion.Create(p1, p2, 1f).BindToPosition(transform))
    .Run();

// LitMotion async (preferred)
await LMotion.Create(transform.position, p1, 1f).BindToPosition(transform);
PlaySfx("step");
await LMotion.Create(p1, p2, 1f).BindToPosition(transform);
```

## Awaiting motions

- **async/await:** `await handle;` — exceptions propagate naturally.
- **Coroutines:** `yield return handle.ToYieldInstruction();` — for legacy `IEnumerator` code.
- **UniTask:** `await handle.ToUniTask();` — when the project uses UniTask (this project does — see `code-conventions-unity.md`).

## Ease Enum Coexistence Helper

During migration, both `DG.Tweening.Ease` and `LitMotion.Ease` exist. Use an `EaseMapper` utility:

```csharp
public static class EaseMapper
{
    public static LitMotion.Ease ToLitMotion(this DG.Tweening.Ease ease)
    {
        return ease switch
        {
            DG.Tweening.Ease.Linear        => LitMotion.Ease.Linear,
            DG.Tweening.Ease.OutQuad       => LitMotion.Ease.OutQuad,
            // ... map all values
            _ => LitMotion.Ease.Linear,
        };
    }
}

// Usage
[SerializeField] private DG.Tweening.Ease ease;  // existing serialized field
LMotion.Create(0f, 1f, 1f)
    .WithEase(this.ease.ToLitMotion())
    .BindToAlpha(canvasGroup);
```

See [project-utilities.md](project-utilities.md) for the full implementation.

## Migration Checklist

1. Remove `using DG.Tweening;` → add `using LitMotion; using LitMotion.Extensions;`
2. Replace `DOTween.Init()` → (not needed, LitMotion auto-initializes)
3. Replace all `.DO*()` shortcuts with `LMotion.Create().BindTo*()` pattern
4. Replace `DOTween.Sequence()` → `LSequence.Create()...Run()`
5. Replace `.Kill()` → `handle.Cancel()`, store `MotionHandle`
6. Add `.AddTo(gameObject)` OR manual `.Cancel()` in `OnDisable()` for lifecycle management
7. Replace `.SetUpdate(true)` → `.WithScheduler(MotionScheduler.UnscaledUpdate)`
8. Remove DOTween Pro package from manifest.json
9. Delete `Resources/DOTweenSettings.asset`
10. Replace `DOVirtual.DelayedCall(...)` with the async pattern above (or the `.RunWithoutBinding()` workaround if you must keep callbacks)
11. Replace `.SetSpeedBased()` with manual `distance / speed` duration
12. Replace `DOPath(...)` with `MotionPath.CreatePathMotion(...)` (see project-utilities.md) or Unity Splines
13. Replace `Sequence.AppendCallback(...)` with `LitMotionSequenceExtensions` or async flow
14. For serialized `Ease` fields that stored `DG.Tweening.Ease`, add `EaseMapper.ToLitMotion()` conversion
15. Run tests — verify all animations behave identically

## Unity 6 first-open regeneration gotchas

These bite projects that are **still on DOTween Pro** (not yet migrated) when they are first opened in Unity 6. They are environment/setup gotchas, not migration steps — but they surface in the same DOTween territory, so they live here.

### DOTween regeneration drops TMP extensions (CS1929)

When a project with an older committed DOTween Pro is first opened in Unity 6, DOTween's setup regenerates `Assets/Plugins/Demigiant/**`:

- Module markers switch from `#if true // MODULE_MARKER` to scripting-define-gated form (`#if DOTWEEN_TEXTMESHPRO`, `#if DOTWEEN_DEAUDIO`, etc.).
- Line endings flip LF→CRLF (whole files show as modified in git).
- DLLs are re-exported.

If the project previously had TMP support enabled via `#if true`, **all TMP tween extensions silently vanish** (e.g. `TMP_Text.DOCounter`) → `CS1929` compile errors.

**Fix:**

1. Add `DOTWEEN_TEXTMESHPRO` to **Scripting Define Symbols** for every relevant platform — OR run **Tools > Demigiant > DOTween Utility Panel > Setup DOTween**, which applies the defines per `DOTweenSettings.asset` module flags (check `textMeshProEnabled: 1`).
2. Commit the regenerated `Assets/Plugins/Demigiant/**` files + the define **once** so fresh checkouts stay clean and don't re-trigger the regeneration churn.

### Related: Odin Inspector self-extraction leaves optional modules UNACTIVATED (CS0234)

On the same first open, Odin Inspector self-extracts `Assets/Plugins/Sirenix/Assemblies/` etc. — but **optional modules stay UNACTIVATED**. Code referencing, e.g., `Sirenix.OdinInspector.Modules.Addressables.Editor` fails with `CS0234` until the module is activated:

- Activate via **Tools > Odin Inspector > Preferences > Modules** (the `Unity.Addressables.data` payload is a proprietary archive only Odin's Module Manager can extract — you cannot hand-unzip it).
- After activation, commit the extracted module files + `OdinModuleConfig.asset` so the next checkout has the module already enabled.
