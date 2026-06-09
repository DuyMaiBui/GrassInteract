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
