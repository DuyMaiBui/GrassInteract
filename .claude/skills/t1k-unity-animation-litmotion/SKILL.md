---
name: t1k:unity:animation:litmotion
description: LitMotion zero-alloc tween library for Unity — LMotion.Create, BindTo, sequences, punch/shake, TMP animation, DOTween Pro migration guide.
effort: high
context: fork
keywords: [litmotion, tween, animation, zero-allocation]
version: 2.2.0
origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---

# LitMotion — Lightning-Fast Tween Library

## Skill Purpose

Reference for [LitMotion](https://github.com/annulusgames/LitMotion) — a zero-allocation, high-performance tween library for Unity. 2-20x faster than DOTween/PrimeTween. Uses DOTS internally (Burst + Jobs) but exposes a MonoBehaviour-friendly API.

> **Related skills:** `dots-ecs-core` (if using ECS) · `dots-rpg` (if animating RPG entities)

---

## When This Skill Triggers

- Using `LMotion.Create`, `LMotion.Punch.Create`, `LMotion.Shake.Create`
- Using `MotionHandle`, `.Bind()`, `.BindToPosition()`, `.BindToLocalScale()`
- Configuring `.WithEase()`, `.WithDelay()`, `.WithLoops()`, `.WithOnComplete()`
- Creating `LSequence` for sequential/parallel motion chains
- Animating TextMeshPro characters with `.BindToTMPCharColor()`
- Writing custom `IMotionAdapter` implementations
- Any tween or animation logic in Unity C# code
- **Migrating from DOTween Pro** — replacing `DOMove`, `DOScale`, `DOFade`, `DOSequence`

---

## Quick Reference

| Task | Reference |
|------|-----------|
| Core API (Create, Bind, Handle) | [core-api-guide.md](references/core-api-guide.md) |
| Configuration (Ease, Loops, Delay, Callbacks) | [configuration-guide.md](references/configuration-guide.md) |
| Advanced (Sequence, Punch/Shake, TMP, Custom) | [advanced-guide.md](references/advanced-guide.md) |
| DOTween Pro → LitMotion Migration | [dotween-migration-guide.md](references/dotween-migration-guide.md) |
| Project Utilities (Path, EaseMapper, Callbacks) | [project-utilities.md](references/project-utilities.md) |

---

## Installation

**UPM Git URL** (add to `Packages/manifest.json`):
```json
"com.annulusgames.lit-motion": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion"
```

**Optional packages:**
```json
"com.annulusgames.lit-motion.animation": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion.Animation/Assets/LitMotion.Animation",
"com.annulusgames.lit-motion.unitask": "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion.UniTask/Assets/LitMotion.UniTask"
```

**Requirements:** Unity 2021.3+, Burst 1.6.0+, Collections 1.5.1+, Mathematics 1.0.0+

---

## Core Pattern

```csharp
// Animate a float from 0 to 10 over 2 seconds
LMotion.Create(0f, 10f, 2f)
    .WithEase(Ease.OutQuad)
    .Bind(x => value = x)
    .AddTo(gameObject);  // auto-cancel on destroy

// Animate from current value (single-arg overload)
LMotion.Create(2f)
    .WithEase(Ease.OutQuad)
    .BindToPositionX(transform);

// Get a handle for manual control
var handle = LMotion.Create(0f, 1f, 0.5f)
    .BindToPositionX(transform);

handle.Cancel();     // cancel
handle.Complete();   // jump to end
handle.IsActive();   // check if running
```

## Common BindTo Shortcuts

### Transform
| Method | Target |
|--------|--------|
| `.BindToPosition(transform)` | `Transform.position` (Vector3) |
| `.BindToPositionX/Y/Z(transform)` | Single axis (float) |
| `.BindToLocalPosition(transform)` | `Transform.localPosition` (Vector3) |
| `.BindToLocalPositionX/Y/Z(transform)` | Single local axis (float) |
| `.BindToLocalScale(transform)` | `Transform.localScale` (Vector3) |
| `.BindToLocalScaleX(transform)` | `float → localScale.x` |
| `.BindToRotation(transform)` | `Transform.rotation` (Quaternion) |
| `.BindToEulerAngles(transform)` | Euler angles (Vector3) |
| `.BindToLocalEulerAnglesZ(transform)` | `float → localEulerAngles.z` |

### UI / Renderer
| Method | Target |
|--------|--------|
| `.BindToColor(renderer/graphic)` | Material / Graphic color (Color) |
| `.BindToColorA(component)` | `float → color.a` (any Graphic) |
| `.BindToColorR/G/B(component)` | `float → color channel` |
| `.BindToAlpha(canvasGroup)` | `CanvasGroup.alpha` (float) |
| `.BindToCanvasGroupAlpha(canvasGroup)` | `CanvasGroup.alpha` (alias) |
| `.BindToFillAmount(image)` | `Image.fillAmount` (float) |
| `.BindToSliderValue(slider)` | `Slider.value` (float) |
| `.BindToSizeDelta(rectTransform)` | `RectTransform.sizeDelta` (Vector2) |
| `.BindToAnchoredPosition(rectTransform)` | `RectTransform.anchoredPosition` (Vector2) |
| `.BindToAnchoredPositionX/Y(rectTransform)` | Single axis (float) |
| `.BindToText(tmpText)` | `TMP_Text.text` (zero-alloc string) |
| `.BindToTMPTextColorAlpha(tmpText)` | `TMP_Text` color alpha (float) |

## Key Conventions

- **Namespace rule: `DG.Tweening` → `LitMotion`** — when migrating from DOTween Pro, remove `using DG.Tweening;` and add `using LitMotion;` + `using LitMotion.Extensions;`. Do NOT use both namespaces in the same file (ambiguous `Ease` type). See [dotween-migration-guide.md](references/dotween-migration-guide.md) for full migration checklist.
- **Prefer manual `.Cancel()` for frequently toggled objects** — `.AddTo(gameObject)` auto-cancels on **destroy**, not on disable. For GameObjects that disable/enable often, store the `MotionHandle` and cancel in `OnDisable()`.
- **`MotionHandle` is a struct** — store it to cancel/complete; check `.IsActive()` before operating
- **Zero allocation** — `LMotion.Create` allocates nothing on the managed heap
- **Ease functions** — `Ease.Linear`, `Ease.InOutQuad`, `Ease.OutBack`, `Ease.OutElastic`, etc. (30+ options)
- **Loop types** — `LoopType.Restart`, `LoopType.Yoyo`, `LoopType.Flip`, `LoopType.Increment`; `-1` = infinite
- **Sequences** — `LSequence.Create()` chains motions; `.Append()` = serial, `.Join()` = parallel. For callbacks in sequences, see [project-utilities.md](references/project-utilities.md).

## Gotchas
- **Leaked handles**: `MotionHandle` not disposed or `.AddTo(gameObject)` missing → motion runs after object destroyed, causing NullReferenceException. Always `.AddTo()` or manually `.Cancel()` in OnDestroy/OnDisable
- **AddTo destroys, not disables**: `.AddTo(gameObject)` cancels on **GameObject.Destroy**, not on `SetActive(false)`. Components that disable/enable frequently need manual cancellation in `OnDisable()`
- **BindTo on destroyed target**: Binding to a Transform/CanvasGroup that gets destroyed mid-tween silently fails or throws. Cancel the handle before destroying the target
- **Not Burst-compatible**: LitMotion's managed API (delegates, closures) cannot run inside Burst-compiled ISystem code. Use LitMotion from MonoBehaviours only
- **Sequence ordering**: `.Join()` runs parallel with the previous `.Append()`, not with all prior items. Misunderstanding this causes timing bugs in complex sequences
- **`BindToLocalScale` (and other `Vector3` binders) silently fail to compile when caller passes a `float` (`CS0315`)**. `BindToLocalScale`, `BindToLocalPosition`, `BindToPosition`, and similar `Vector3` shortcuts are typed-generic — they only accept an `LMotion<Vector3, ...>`. A common mistake is creating an `LMotion<float, ...>` (e.g. `LMotion.Create(1f, 1.1f, 0.2f)`) and trying to bind it to scale, intending the float as a uniform scale factor.

  ```csharp
  // ❌ WRONG — passes float scale factor to a Vector3 binder
  LMotion.Create(1f, 1.1f, 0.2f)
      .BindToLocalScale(t => transform.localScale = t * Vector3.one);   // CS0315

  // ✅ CORRECT — Vector3 throughout
  LMotion.Create(Vector3.one, Vector3.one * 1.1f, 0.2f)
      .BindToLocalScale(transform);

  // ✅ ALSO CORRECT — explicit bind callback when you need custom logic
  LMotion.Create(1f, 1.1f, 0.2f)
      .Bind(t => transform.localScale = Vector3.one * t);   // no shortcut, custom assignment
  ```

  **Rule of thumb:** If you want a `Vector3` result, START with `Vector3` values in `LMotion.Create` and use the shortcut binder. If you want a scalar (`float`) result that you'll map to a `Vector3` yourself, use the generic `.Bind(t => ...)` callback and assign manually.

  Field reports: see [references/incidents.md](references/incidents.md).
- **Unity 6 first-open DOTween regeneration drops TMP extensions (CS1929)**: when a project that still ships DOTween Pro is first opened in Unity 6, DOTween's setup regenerates `Assets/Plugins/Demigiant/**` — module markers switch from `#if true // MODULE_MARKER` to scripting-define-gated form (`#if DOTWEEN_TEXTMESHPRO`, `#if DOTWEEN_DEAUDIO`, …), line endings flip LF→CRLF (whole files show modified in git), and DLLs are re-exported. If TMP support was previously enabled via `#if true`, all TMP tween extensions (e.g. `TMP_Text.DOCounter`) silently vanish → `CS1929` compile errors. **Fix:** add `DOTWEEN_TEXTMESHPRO` to Scripting Define Symbols for every relevant platform (or run Tools > Demigiant > DOTween Utility Panel > Setup DOTween, which applies defines per `DOTweenSettings.asset` module flags — confirm `textMeshProEnabled: 1`), then commit the regenerated files + define once so fresh checkouts stay clean. Full repro + the related Odin Inspector module-activation gotcha: [references/dotween-migration-guide.md](references/dotween-migration-guide.md) § "Unity 6 first-open regeneration gotchas".
