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
