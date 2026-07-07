---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---
# DOTweenAnimation (component) → LitMotionAnimation (component) Migration

> **Scope:** the **Inspector component** case — converting the DOTween Pro `DOTweenAnimation` MonoBehaviour (configured on a prefab/GameObject, zero code) into LitMotion's `LitMotionAnimation` component (`com.annulusgames.lit-motion.animation`). For **code** tweens (`transform.DOMove()` → `LMotion.Create()`) see [dotween-migration-guide.md](dotween-migration-guide.md) instead. This guide is the missing component↔component half.

Grounded against LitMotion.Animation **2.0.2** package source (`LitMotionAnimation.cs`, `PropertyAnimationComponent.cs`, `MotionSettings.cs`).

---

## 1. Architecture: monolithic component vs component array

| Aspect | `DOTweenAnimation` (DOTween Pro) | `LitMotionAnimation` (LitMotion) |
|--------|----------------------------------|----------------------------------|
| Shape | ONE component = ONE tween, 24-value `animationType` enum + a bag of optional fields | ONE component holds an **array** of typed animation components (`[SerializeReference] LitMotionAnimationComponent[]`) |
| Multiple anims on one object | multiple `DOTweenAnimation` components (or `id`-grouped) | ONE `LitMotionAnimation`, multiple entries in its `Components` list |
| Play timing | `autoPlay` bool | `autoPlayMode` enum: `None` / `OnStart` / `OnEnable` |
| Parallel vs sequential | each component independent; `id` grouping | `animationMode` enum: `Parallel` / `Sequential` (one switch for the whole list) |
| Namespace | `DG.Tweening` | `LitMotion.Animation` (components in `LitMotion.Animation.Components`) |
| "From" mode | `isFrom` bool | **none** — emulate by swapping start/end (see §4) |
| Relative | `isRelative` bool | `relative` bool (per component) |
| Speed-based | `isSpeedBased` | **none** — compute `duration = distance/speed` |
| ID ops (`DOPlayById`) | extensive | **none** — index into `Components` or split components |
| Per-tween callbacks | UnityEvents (onComplete, onStepComplete…) | `EventComponent` entry in a `Sequential` list; no per-tween UnityEvent |

**Key consequence:** a GameObject with N `DOTweenAnimation` components → ONE `LitMotionAnimation` with N entries in `Components`.

## 2. `LitMotionAnimation` C# API (play-control)

```csharp
using LitMotion.Animation;

var anim = GetComponent<LitMotionAnimation>();
anim.Play();      // play all enabled components (Parallel or Sequential per animationMode)
anim.Pause();     // PlaybackSpeed = 0 on active components
anim.Stop();      // cancel all + restore each component's captured start value
anim.Restart();   // Stop() then Play()
bool a = anim.IsActive;    // any component active/queued
bool p = anim.IsPlaying;   // any component actually playing (not paused)
IReadOnlyList<LitMotionAnimationComponent> list = anim.Components;
```

### Play-control code mapping (for runtime code that drove DOTweenAnimation)

| DOTween Pro (`DOTweenAnimation`) | LitMotion (`LitMotionAnimation`) |
|----------------------------------|----------------------------------|
| `GetComponent<DOTweenAnimation>()` | `GetComponent<LitMotionAnimation>()` |
| `.DOPlay()` / `.DOPlayForward()` | `.Play()` |
| `.DORestart()` | `.Restart()` |
| `.DOPause()` / `.DOTogglePause()` | `.Pause()` |
| `.DOKill()` | `.Stop()` |
| `.DOComplete()` | **no direct equivalent** — there is no "jump to end" on the component; drive the underlying `MotionHandle` (`component.TrackedHandle.Complete()`) or redesign |
| `.DOPlayById(id)` / `*ById` | **unsupported** — split into separate `LitMotionAnimation` components and call `.Play()` on the right one |
| `.DORewind()` | **no direct equivalent** — `Stop()` restores start values; for reverse playback redesign with `LoopType.Yoyo` or two motions |

> **This project (TheOneFeature) finding (2026-06-09 scout):** there is **no** `DOTweenAnimation` play-control code today — the only `DO*` calls are raw `Transform.DOKill()` (covered by the code guide). This §2 table is for portability / future code, not a current migration burden here.

## 3. Type mapping — `DOTweenAnimation.AnimationType` (24) → LitMotion component classes

> **Code gotcha:** the DOTween enum is the **nested** type `DG.Tweening.DOTweenAnimation.AnimationType` (field `animationType`), NOT a top-level `DG.Tweening.DOTweenAnimationType` — the latter does not exist and `using DGType = DG.Tweening.DOTweenAnimationType;` fails with `CS0234`. Alias the nested type: `using DGType = DG.Tweening.DOTweenAnimation.AnimationType;`.

All component classes live in `LitMotion.Animation.Components`. UGUI/TMP/Camera/Audio/Physics variants auto-compile via the package's asmdef `versionDefines` when the matching Unity package is present (this project has UGUI 2.x + TMP + URP, so all of the below are live — no manual scripting define needed).

| `DOTweenAnimationType` | LitMotion component | Notes |
|------------------------|---------------------|-------|
| `Move` | `TransformPositionAnimation` (`useWorldSpace = true`) | world position |
| `LocalMove` | `TransformPositionAnimation` (`useWorldSpace = false`) | local position |
| `Rotate` | `TransformRotationAnimation` (`useWorldSpace = true`) | euler |
| `LocalRotate` | `TransformRotationAnimation` (`useWorldSpace = false`) | local euler |
| `Scale` | `TransformScaleAnimation` | localScale; no worldspace flag |
| `Color` | by target: `ImageColorAnimation` / `GraphicColorAnimation` / `SpriteRendererColorAnimation` / `TextColorAnimation` / `TMPTextColorAnimation` / `MaterialColorAnimation` | pick by the bound component type |
| `Fade` | by target: `CanvasGroupAlphaAnimation` / `ImageColorAlphaAnimation` / `SpriteRendererColorAlpha…` / `TMPTextColorAlphaAnimation` | alpha-only; `Graphic` has no alpha-only component → use the color-alpha variant of the concrete type |
| `Text` | `TextAnimation` (UGUI `Text`) / `TMPTextAnimation` (`TMP_Text`) | string tween |
| `PunchPosition` | `TransformPositionPunchAnimation` | `options` = `PunchOptions` |
| `PunchRotation` | `TransformRotationPunchAnimation` | |
| `PunchScale` | `TransformScalePunchAnimation` | |
| `ShakePosition` | `TransformPositionShakeAnimation` | `options` = `ShakeOptions` |
| `ShakeRotation` | `TransformRotationShakeAnimation` | |
| `ShakeScale` | `TransformScaleShakeAnimation` | |
| `CameraAspect` | `CameraAspectAnimation` | |
| `CameraBackgroundColor` | `CameraBackgroundColorAnimation` | |
| `CameraFieldOfView` | `CameraFieldOfViewAnimation` | |
| `CameraOrthoSize` | `CameraOrthographicSizeAnimation` | name differs (Orthographic, not Ortho) |
| `CameraPixelRect` | `CameraPixelRectAnimation` | |
| `CameraRect` | `CameraRectAnimation` | |
| `UIWidthHeight` | `RectTransformSizeDeltaAnimation` | |
| `FillAmount` | `ImageFillAmountAnimation` | |
| `None` | — | skip |

## 4. Field mapping (per component)

Every `PropertyAnimationComponent` serializes: `target` (the animated object), `relative` (bool), and `settings` (`SerializableMotionSettings<TValue,TOptions>`). `settings` exposes (exact serialized names): `startValue`, `endValue`, `duration`, `ease`, `customEaseCurve`, `delay`, `delayType`, `loops` (default `1`), `loopType`, `options`, `schedulerType`.

| DOTweenAnimation field | LitMotion target | Rule |
|------------------------|------------------|------|
| `duration` | `settings.duration` | 1:1 |
| `delay` | `settings.delay` | 1:1 |
| `easeType` (`DG.Tweening.Ease`) | `settings.ease` (`LitMotion.Ease`) | use project `EaseMapper.ToLitMotion()` ([project-utilities.md](project-utilities.md)) |
| `easeCurve` (AnimationCurve, when `easeType == INTERNAL_Custom`) | `settings.customEaseCurve` + `settings.ease = Ease.CustomAnimationCurve` | curve copied verbatim |
| `loops` | `settings.loops` | DOTween `-1` (infinite) → LitMotion `-1` |
| `loopType` (`DG.Tweening.LoopType`) | `settings.loopType` (`LitMotion.LoopType`) | `Restart/Yoyo/Incremental` map 1:1 |
| `isRelative` | `relative = true` | see start/end rule below |
| `isFrom` | — | **swap start/end** (see below) |
| `isIndependentUpdate` | `settings.schedulerType` | `true` → an `*IgnoreTimeScale`/`Realtime` scheduler (default `Update` otherwise) |
| `autoPlay` | `LitMotionAnimation.autoPlayMode` | `true` → `OnStart` (or `OnEnable` if it must replay on re-enable); `false` → `None` |
| `endValueV3/V2/Float/Color/String` | `settings.endValue` (+ `startValue`) | see start/end rule |

### start/end value rule (the `isFrom` / `isRelative` trap)

LitMotion's component animates `settings.startValue → settings.endValue` and assigns the value directly (it does **not** auto-start from the runtime-current value the way DOTween's `DOMove(end)` does). So you must seed BOTH ends from the authored value:

- `current` = the target's authored property value in the prefab (e.g. `transform.localPosition`).
- `end` = the DOTweenAnimation `endValueV3` (etc.).

| DOTween config | `settings.startValue` | `settings.endValue` | `relative` |
|----------------|----------------------|---------------------|-----------|
| absolute, normal | `current` | `end` | `false` |
| absolute, `isFrom = true` | `end` | `current` | `false` |
| `isRelative = true` (normal) | `zero` | `end` (delta) | `true` |
| `isRelative = true` + `isFrom` | `end` (delta) | `zero` | `true` |

> **Caveat:** because LitMotion uses the *authored* `startValue` (not runtime-current), an absolute conversion is faithful only when the object sits at its authored value when `Play()` fires (true for the common case — UI intro/idle anims that play on enable from rest). If gameplay moves the object before the anim plays, prefer a code tween (`LMotion.Create(transform.position, end, dur)`) over the component. Flag such cases during conversion.

### Unsupported → workaround

- **`isSpeedBased`** — no equivalent. Pre-compute `duration = distance / speed` and write `settings.duration`.
- **per-tween UnityEvents** (onComplete/onStepComplete) — set `animationMode = Sequential` and append an `EventComponent` after the motion entry, OR move the callback to code via `anim.Components[i].TrackedHandle`.
- **`id`-based grouping** — split into separate `LitMotionAnimation` components, one per group.

## 5. Manual conversion process (one component)

1. Add a `LitMotionAnimation` component (Add Component ▸ "LitMotion Animation").
2. Set `autoPlayMode` from `autoPlay` (`OnStart` by default).
3. If the GameObject has 2+ DOTweenAnimations that should run together, set `animationMode = Parallel`; if they were chained, `Sequential`.
4. For each DOTweenAnimation: add a list entry of the mapped type (§3), set its `target` (the DOTweenAnimation's resolved target — `targetGO`/`target` or self), set `settings.duration/delay/ease/loops/loopType`, set `startValue`/`endValue`/`relative` per §4.
5. **Preview** in the Inspector (Edit Mode preview is supported) — confirm it matches the old tween.
6. Remove the `DOTweenAnimation` component **only after** the preview matches.

## 6. Automated converter (Editor tool)

For bulk work this is automated by the Editor tool **`Tools ▸ Library ▸ LitMotion ▸ Convert DOTweenAnimation…`** (`Packages/TheOneFeature/Editor/Migration/LitMotion/`, opt-in behind the `THEONE_LITMOTION_MIGRATION_FROM_DOTWEEN` scripting define). It locates prefabs referencing the `DOTweenAnimation` script GUID `4d0390bd8b8ffd640b34fe25065ff1df`, reads each component's serialized fields, and writes a mapped `LitMotionAnimation` via `SerializedProperty.managedReferenceValue` (the `[SerializeReference]` array) + the `settings.*` field paths from §4.

- **Report-first:** default mode only scans + prints a per-prefab table (type, target, mappable?). It does **not** mutate.
- **Apply** is explicit, runs under `Undo`/`PrefabUtility.SavePrefabAsset`, and **does not delete** the source `DOTweenAnimation` (manual verify, then a second explicit cleanup pass).
- **Unmappable types are reported, never guessed** (e.g. ambiguous `Color`/`Fade` target type, Camera edge cases).

See [project-utilities.md](project-utilities.md) § "DOTweenAnimation converter" for the tool's coverage table and limitations.

> ⚠️ **Validate before trusting Apply.** Auto-writing the generic `SerializableMotionSettings<TValue,TOptions>` + `[SerializeReference]` array is fragile. Round-trip one hand-authored prefab and confirm it previews identically BEFORE running Apply across many prefabs (LitMotion skill risk #1).

## 7. Sequence & callback gotchas — code fallbacks driven from `TrackedHandle`

When §2/§4's code-fallback path is used (driving a component's underlying `MotionHandle` directly, or hand-rolling an `LSequence` for a case the component model can't express — e.g. per-tween callbacks or `id`-grouped chains), two DOTween habits carry over wrong:

### 7.1 `MotionHandle` has no `WithOnComplete` — the callback lives on the builder, not the handle

`DOTween.Sequence()...OnComplete(cb)` has no equivalent method on `MotionHandle`. `WithOnComplete` is a **builder** method — configure it on the builder passed into `Run()`, not on the handle `Run()` returns:

```csharp
// WRONG — MotionHandle has no WithOnComplete; this does not compile (CS1061)
MotionHandle handle = sequence.Run();
handle.WithOnComplete(() => Debug.Log("done"));

// RIGHT — configure the callback on the builder inside Run()
MotionHandle handle = sequence.Run(b => b.WithOnComplete(() => Debug.Log("done")));
```

### 7.2 `Join` inserts at `lastTail` and does NOT advance it — only `Append`/`AppendInterval` do

`LSequence` tracks a single `lastTail` cursor. `Append`/`AppendInterval` schedule at `lastTail` and THEN advance it. `Join` schedules at the *current* `lastTail` and leaves it unchanged. So a `Join / Join / Append` chain schedules **all three at `t = 0`** — the trailing `Append` does not wait for the joined legs; it runs in parallel with them, which silently breaks a DOTween sequence that assumed "append after the parallel group finishes."

```csharp
// WRONG — moveOutMotion starts at t=0 too; it does NOT wait for the joined pair
LSequence.Create()
    .Join(punchMotion)        // starts at t=0
    .Join(shakeMotion)        // starts at t=0 — does NOT push lastTail forward
    .Append(moveOutMotion)    // ALSO starts at t=0 — not "after" the joined pair!
    .Run();

// RIGHT — insert the longest joined leg's duration as an explicit gap first
var maxLegDuration = Mathf.Max(punchDuration, shakeDuration);
LSequence.Create()
    .Join(punchMotion)
    .Join(shakeMotion)
    .AppendInterval(maxLegDuration)   // advances lastTail past the parallel group
    .Append(moveOutMotion)            // now genuinely starts after the group
    .Run();
```

**Rule of thumb:** after any `Join`-group, insert `AppendInterval(maxLegDuration)` (the longest leg's duration) before the next `Append` when that motion must run *after* the group rather than *with* it. For the full code-tween API see [dotween-migration-guide.md](dotween-migration-guide.md); for full `LSequence` coverage see [advanced-guide.md](advanced-guide.md).
