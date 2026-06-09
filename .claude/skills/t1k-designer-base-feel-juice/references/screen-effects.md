---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Screen-Level Juice Effects

## Camera Shake Formula

Damped harmonic shake — most natural feel:

```csharp
// Per-frame update:
Vector3 offset = magnitude * Random.insideUnitSphere * Mathf.Exp(-dampening * elapsedTime);
camera.transform.localPosition = basePosition + offset;

// Typical values:
// magnitude = 0.1-0.5 (hit = 0.1-0.3, explosion = 0.3-0.5)
// dampening = 8-12 (higher = faster decay)
// duration = 0.2-0.5s
```

**Shake types**:
| Type | Direction | Best For |
|------|-----------|---------|
| Random (all axes) | Random sphere | Explosion, impact |
| Camera punch | Single direction | Recoil, jump land |
| Rotational | Z-axis roll | Earthquake, disorientation |
| Vertical only | Y-axis | Ground slam, stomp |

**Gotcha**: Additive shakes stack — cap total magnitude at 0.8 units max. Multiple simultaneous effects without cap = nauseating.

---

## Camera Punch

Single-direction impulse (not oscillating — one push and spring back):

```
Direction: → attacker's aim direction (recoil) OR ↓ (landing impact)
Magnitude: 0.2-0.4 units
Spring back: overdamped spring — overshoot 10-15%, settle in 0.2-0.3s
```

**Use cases**:
- Gun recoil: punch back opposite of aim
- Jump land: punch downward
- Dash: punch in dash direction
- Melee attack: punch toward target

---

## Chromatic Aberration

RGB channel separation — brief and subtle for impact:

| Event | Intensity | Duration | Style |
|-------|-----------|----------|-------|
| Heavy hit received | 0.8-1.5 | 0.1s | Sharp onset, fast decay |
| Near-miss | 0.5-0.8 | 0.08s | Barely perceptible |
| Boss phase transition | 1.0-2.0 | 0.3s | Dramatic, slow decay |
| Death sequence | 2.0-3.0 | 0.5s | Full distortion before fade |

**Rule**: Always optional in settings — motion-sensitive players find it nauseating. Map to "Reduce Motion" toggle.

**URP implementation**: `ChromaticAberration` volume component → animate `intensity.value` via `LMotion` or coroutine.

---

## Time Slow (Bullet Time)

| Trigger | Timescale | Duration | Use |
|---------|-----------|----------|-----|
| Kill blow | 0.1-0.2× | 0.15-0.3s | "Kill highlight" |
| Critical hit | 0.3× | 0.1s | Impact emphasis |
| Dodge success / near-miss | 0.2× | 0.2s | "Matrix dodge" |
| Player death | 0.2× | 0.5-1.0s | Dramatic moment |
| Boss phase break | 0.1× | 0.5s | Cinematic phase transition |

**DOTS note**: `Time.timeScale` pauses all `FixedTimestepSimulationSystemGroup`. Use `SystemState.WorldUnmanaged.Time.Scale` or a custom `SlowMoSystem` that applies a multiplier to simulation delta time.

**Gotcha**: Time slow + hit stop together = double freeze. Sequence them: hit stop first (2-4 frames), then time slow (if still needed).

---

## Vignette (Low Health Warning)

- **Trigger**: player HP < 25%
- **Intensity**: lerp from 0.0 → 0.5 as HP drops from 25% → 0%
- **Pulse**: at HP < 10%, pulse intensity 0.3↔0.6 at 1Hz (matches heartbeat rate)
- **Color**: dark red tint preferred over pure black
- **Rule**: Always pair with audio (heartbeat SFX, low-HP music sting)

**URP**: `Vignette` volume component, animate `intensity.value`.

---

## Flash Effects

| Event | Flash Color | Duration | Timing |
|-------|------------|----------|--------|
| Level up | White → transparent | 0.3s | Full screen, centered |
| Achievement unlock | Gold → transparent | 0.4s | Top-right vignette flash |
| Critical damage taken | Red → transparent | 0.2s | Edge vignette flash |
| Heal | Green → transparent | 0.2s | Edge vignette flash |
| Teleport / warp | White full-screen | 0.1s in + 0.2s out | Covers cut |
| Death fade | Black fade in | 1.0-2.0s | Slow, mournful |

**Implementation**: Fullscreen `Image` Canvas (renderMode = ScreenSpaceOverlay) at top layer, alpha animated.

---

## Post-Processing Timeline (Per Impact)

```
Frame 0:    Input registered / collision detected
Frame 0:    Sound plays, hit flash ON, particle spawn
Frame 0-1:  Hit stop begins (timescale = 0)
Frame 1-2:  Hit stop ends (timescale = 1), damage number spawns
Frame 2-4:  Screen shake begins, chromatic aberration ON
Frame 4-8:  Shake decays, aberration fades
Frame 8-12: All effects settled — baseline restored
```

Total effect duration: ~0.2s (12 frames at 60fps) for standard hit.
