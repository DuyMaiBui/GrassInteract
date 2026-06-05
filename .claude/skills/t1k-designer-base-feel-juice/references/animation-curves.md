---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Animation Curves & Easing

## Ease Type Selection

| Ease Type | Feel | Best Use |
|-----------|------|---------|
| Linear | Robotic, mechanical | Progress bars, health bar depletion |
| Ease-out | Snappy, responsive | UI appear, projectile launch, button press |
| Ease-in | Building weight | UI dismiss, object landing, door closing |
| Ease-in-out | Smooth, cinematic | Camera movement, character walk, panel slide |
| Spring | Bouncy, playful | Popup dialogs, inventory items, notifications |
| Back (overshoot) | Energetic, confident | Achievement icon, power-up pickup, confirm button |

**Rule**: Ease-out for player-triggered actions (feels responsive). Ease-in for automated/environmental (feels weighty).

---

## UI Animation Timing

| UI Element | Appear | Dismiss | Ease Type |
|-----------|--------|---------|-----------|
| Dialog / panel | 0.2s | 0.15s | Ease-out appear, ease-in dismiss |
| Tooltip | 0.1s | 0.08s | Ease-out |
| Notification banner | 0.25s slide in | 0.15s slide out | Ease-out both |
| Inventory item pickup | 0.15s scale 0→1 | — | Back (overshoot) |
| Damage number | Instant spawn | 0.3s fade out | — |
| Menu screen transition | 0.3s | 0.2s | Ease-in-out |
| Button press feedback | 0.08s scale down | 0.1s scale up | Ease-out |

**Tweening patterns** (see tweening library skill in engine layer):
```csharp
// Panel appear
LMotion.Create(0f, 1f, 0.2f).WithEase(Ease.OutQuart).Bind(x => canvasGroup.alpha = x);

// Spring popup
LMotion.Create(Vector3.zero, Vector3.one, 0.3f).WithEase(Ease.OutBack).BindToLocalScale(transform);
```

---

## Squash and Stretch

Classic animation principle — exaggerates weight and physicality.

**Landing impact**:
```
Frame 0 (airborne): scale (1.0, 1.0, 1.0)
Frame 0 (land):     scale (1.2, 0.8, 1.2)  ← squash (wider, shorter)
Frame 2:            scale (0.9, 1.15, 0.9)  ← stretch (rebound overshoot)
Frame 5:            scale (1.0, 1.0, 1.0)  ← settle
Total duration: 0.2s
```

**Jump launch**:
```
Wind-up:  scale (1.1, 0.85, 1.1)  ← anticipation squash
Launch:   scale (0.85, 1.2, 0.85) ← stretch upward
```

**Rule**: Squash amount = stretch amount (volume preservation). Squash X by 1.2 → stretch Y by 1/1.2 ≈ 0.83.

---

## Anticipation

Brief wind-up before action — makes motion feel intentional:

| Action | Anticipation | Duration |
|--------|-------------|----------|
| Jump | Crouch (scale down) | 0.1s |
| Melee swing | Weapon pull-back | 0.1-0.15s |
| Heavy attack | Exaggerated pull-back | 0.2-0.3s |
| Dash | Lean backward | 0.05s |
| Projectile launch | Brief weapon raise | 0.08s |

**Rule**: Anticipation duration ∝ action weight. Light attacks: minimal. Heavy attacks: visible wind-up.

---

## Follow-Through

Action overshoots target then settles — communicates momentum:

| Action | Overshoot Amount | Settle Time |
|--------|-----------------|-------------|
| UI button confirm | 5-10% scale | 0.1s |
| Door fully open | 3-5° past 90° | 0.2s |
| Character land (rotation) | 2-5° pitch forward | 0.15s |
| Sword slash end pose | 10-15° past intended end | 0.1s |
| Camera settle on target | Slight drift past, correct | 0.2s |

---

## Stagger / Cascade

Delay between sequential elements — makes groups feel alive, not robotic:

**Pattern**: each item delayed by 0.04-0.08s from previous.

| Use Case | Stagger Delay | Effect |
|----------|--------------|--------|
| Inventory grid fill (on open) | 0.05s per cell row | Wave-like population |
| Combo counter digits | 0.06s per digit | Satisfying build-up |
| Skill icons appear (level-up) | 0.08s per icon | Reveal rhythm |
| Enemy spawn wave | 0.1s per unit | Staggered entry |
| Results screen items | 0.15s per item | Dramatic reveal |

---

## Spring Physics Values

Damped harmonic oscillator for bouncy motion:

```
stiffness = 200-400  (higher = faster spring, snappier)
damping   = 10-20   (higher = less bounce, settles faster)
mass      = 1.0

Critical damping (no bounce): damping = 2 × sqrt(stiffness × mass)
Under-damped (bouncy): damping < critical
Over-damped (sluggish): damping > critical
```

**Common presets**:
| Preset | Stiffness | Damping | Feel |
|--------|-----------|---------|------|
| Snappy UI | 400 | 28 | Fast settle, slight bounce |
| Bouncy popup | 200 | 10 | Visible oscillation |
| Heavy door | 150 | 20 | Slow, weighty |
| Character lean | 300 | 25 | Responsive, controlled |
