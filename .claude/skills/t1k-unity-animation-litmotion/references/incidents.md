---

origin: theonekit-unity
repository: The1Studio/theonekit-unity
module: animation
protected: false
---
# LitMotion — Incident Log

Dated reproductions of gotchas surfaced in real cooks. Body rules live in `SKILL.md`; this file holds the field reports that validated them.

## `CS0315` on `BindToLocalScale` with `float` source — recurring across teammates

- **Date:** 2026-05-23
- **Project / session:** DOTS-AI ChaosForge cook, phase10r5-pillar-fidelity review
- **Commit:** `34799dc1`
- **Independent hits in same session:** ≥3 teammates — R3 `UITapPunch`, phase7b `RealmCardView.PunchUnlock`, plus one more
- **Pattern:** caller created `LMotion<float, ...>` and tried to bind to `BindToLocalScale`, expecting the float to be treated as a uniform scale factor. Compiler error `CS0315: float cannot be used as type parameter T in generic LMotion<T,A>.BindToLocalScale(Action<Vector3>)`
- **Root cause:** `BindToLocalScale` / `BindToLocalPosition` / `BindToPosition` are typed-generic; they accept only `LMotion<Vector3, ...>`
- **Resolution:** start with `Vector3` values in `LMotion.Create`, OR use the generic `.Bind(t => ...)` callback for scalar-to-vector mapping
- **Why surfaced now:** existing per-call patterns on `master` pre-date the R5 review; the cluster of independent failures showed the rule was discoverable only after failing the compile

## Punch/Shake signature confusion — 2-arg vs 3-arg

- **Date:** 2026-06-08
- **Project / session:** LitMotion skill update
- **Pattern:** Early skill drafts documented `LMotion.Punch.Create(strength, duration)` (2 args). Official API is `LMotion.Punch.Create(startValue, strength, duration)` (3 args).
- **Root cause:** Confusion between DOTween `DOPunchPosition(punch, duration)` and LitMotion's different signature
- **Resolution:** Skill updated to document the correct 3-arg signature with `startValue` as first parameter. Punch/Shake animate **from startValue** with oscillation, not from current value.
- **Impact:** Code using the wrong signature would fail to compile; no runtime regression risk.

## `AddTo(gameObject)` does NOT cancel on disable

- **Date:** 2026-06-08
- **Project / session:** LitMotion migration planning
- **Pattern:** Migrated code used `.AddTo(gameObject)` expecting DOTween `SetLink(KillOnDisable)` behavior. GameObjects disabled/re-enabled caused duplicate motions.
- **Root cause:** `.AddTo()` cancels on **Destroy**, not on `SetActive(false)`. DOTween's `LinkBehaviour.KillOnDisable` is stronger.
- **Resolution:** For frequently toggled objects, store `MotionHandle` and manually `.Cancel()` in `OnDisable()`. Documented in skill § Key Conventions.
- **Example fix:**
  ```csharp
  // Before (leaked on disable)
  LMotion.Create(0f, 1f, 1f).BindToAlpha(canvasGroup).AddTo(gameObject);

  // After (properly cancelled on disable)
  private MotionHandle handle;
  void OnEnable() { handle = LMotion.Create(0f, 1f, 1f).BindToAlpha(canvasGroup); }
  void OnDisable() { if (handle.IsActive()) handle.Cancel(); }
  ```

## `MotionIsInSequence` — a handle inside an `LSequence` CANNOT be cancelled individually

- **Date:** 2026-06-09
- **Project / session:** TheOneFeature RewardAnimation currency-flyout linger fix
- **Symptom:** `InvalidOperationException: Cannot access the motion in sequence.` thrown from `LitMotion.Error.MotionIsInSequence` at runtime, inside an `AppendCallback` that called `moveHandle.Cancel()` on a handle previously added to the sequence via `.Append(moveHandle)`.
- **Pattern (the trap):**
  ```csharp
  var moveHandle = LMotion.Create(a, b, d).BindToLocalPositionY(t);   // bound, now also...
  var fadeHandle = LMotion.Create(1f, 0f, d).BindToColorA(text);
  LSequence.Create()
      .Append(moveHandle)        // ...adopted by the sequence
      .Join(fadeHandle)
      .AppendCallback(() => {
          if (moveHandle.IsActive()) moveHandle.Cancel();   // ❌ THROWS MotionIsInSequence
          Recycle(obj);
      })
      .Run();
  ```
- **Root cause:** once a `MotionHandle` is appended/joined into an `LSequence`, the sequence OWNS its lifecycle. Calling `.Cancel()` (or `.Complete()`) on the child handle throws `MotionIsInSequence`. **`handle.IsActive()` returns `true` for a sequenced handle**, so an `if (handle.IsActive())` guard does NOT protect you — it lets the illegal `Cancel()` through.
- **Resolution:**
  1. **Don't cancel children.** A sequence drives its children to completion; on natural completion they end with the sequence — there is no leak to defend against. Just do the post-work (e.g. `Recycle`) in `AppendCallback`.
  2. **To stop a sequence early, cancel the SEQUENCE handle** returned by `.Run()`, never the child handles:
     ```csharp
     var seq = LSequence.Create().Append(moveHandle).Join(fadeHandle).Run();
     // later, to abort: if (seq.IsActive()) seq.Cancel();
     ```
- **Corollary — the cancel-before-Recycle rule (see pooling incident below) applies only to STANDALONE handles** (`.BindTo…()` / `MotionPath.CreatePathMotion` not appended to any sequence). Standalone handles MUST be cancelled before recycling a pooled object; sequenced handles MUST NOT.

## Pooled objects: cancel STANDALONE handles before `Recycle()` (LitMotion ignores `SetActive(false)`)

- **Date:** 2026-06-09
- **Project / session:** TheOneFeature RewardAnimation currency-flyout linger fix
- **Symptom:** after a currency icon flew into the top bar it did not vanish — it reappeared/drifted near the spawn origin (≈ screen center) for a short time before disappearing. Introduced by a DOTween → LitMotion migration.
- **Root cause:** LitMotion runs on a central PlayerLoop and does **not** cancel motions when a GameObject is deactivated. `IObjectPoolManager.Recycle()` deactivates the object, so any still-live STANDALONE motion (path, scale, or an infinite `WithLoops(-1, Yoyo)` idle bob) keeps driving the transform after it returns to the pool and bleeds into its next spawn. The DOTween version guarded this with `transform.DOKill()` at spawn and before recycle; the migration dropped those with no equivalent.
- **Resolution:** store every standalone `MotionHandle` bound to a pooled object and cancel them in a `try/finally` immediately before `Recycle`, so even an interrupted `await` (scene teardown) cleans up:
  ```csharp
  var scaleHandle = LMotion.Create(...).BindToLocalScale(t);
  var pathHandle  = MotionPath.CreatePathMotion(t, path, d, ease);
  try { await pathHandle.ToUniTask(); /* snap + meet-target + vfx */ }
  finally {
      if (scaleHandle.IsActive()) scaleHandle.Cancel();   // standalone → safe to cancel
      if (pathHandle.IsActive())  pathHandle.Cancel();
      pool.Recycle(obj);
  }
  ```
- **Gotcha:** do NOT apply this to handles that live inside an `LSequence` — see the `MotionIsInSequence` incident above. The reference implementation that got it right first was `EntryButtonItemCurrencyAnimation` (`activeMotions` dict + `KillAllMotions` over standalone handles only).
