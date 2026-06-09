---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Response — Game Feel Metric 2

How input signals modulate game-object state over time. The most actionable metric — small ADSR tuning changes can flip a game from "twitchy" to "tight" to "sluggish". Source: Swink Ch.7 (pp.119–138).

## Definition

**Response** is the game's output to player input. It answers: "What does the game do when the player acts?" Response involves mapping an input signal to a parameter change (position, velocity, animation speed, rotation rate) and modulating that parameter over time via an envelope. The shape and timing of the envelope determines whether the game feels responsive, delayed, or organic. Response is measured by identifying which parameters change, how they change over time, and what the modulation curve looks like (pp.119–120).

## The ADSR Envelope (Core Framework)

Borrowed from audio synthesis. Maps player input (button hold) to game-state output (velocity, position, parameter). Four phases define how a parameter evolves from input onset to input release:

### Attack (A)

The initial phase during which the parameter ramps up from zero to its peak value. Measures time from input onset to peak.

- **Short attack (<100 ms):** Feels responsive and immediate; player perceives direct control (p.124).
- **Long attack (>100 ms):** Feels floaty, loose, or sluggish without immediate feedback (p.124). Example: Super Mario's horizontal acceleration over roughly 0.5 seconds during a run feels organic and weighty (p.126).
- **Instantaneous attack (0 ms):** Feels crisp and stiff; no ramp (p.124).

Attack timing is the single most impactful ADSR parameter for perceived responsiveness.

### Decay (D)

Optional phase after attack. Parameter drops from peak to a sustain level. Not all systems use decay.

- **With decay:** Parameter overshoots peak slightly, then settles toward the sustain level. Figure 7.13 (p.127) shows the decay-phase shape generically; Swink uses a Counter-Strike movement-decay observation as one of several illustrations on the same page.
- **Without decay:** Parameter jumps directly to sustain. Simpler feel, often for grid-based movement.

Decay duration and curve determine how "springy" or "damped" the response feels.

### Sustain (S)

The level at which the parameter holds while input is held. The steady-state value.

- Player holds button → parameter stays at sustain level
- Example: Pipe organ sustains at constant volume as long as key is pressed (p.123); a character moves at constant speed while direction button held.

Sustain level is independent of attack/decay timing.

### Release (R)

Phase that begins when input is released. Parameter decays from sustain to zero.

- **Short release (<150 ms):** Feels snappy, responsive, stops quickly.
- **Long release (>300 ms):** Feels slippery, on-ice, organic. Example: Super Mario doesn't stop instantly when direction button released; velocity decays over a short curve, creating a soft, controlled feel (p.126).
- **Instantaneous release (0 ms):** Feels harsh, abrupt.

Release curve and duration affect perceived weight and control during deceleration.

### ASCII ADSR Diagram

```
Parameter
    ^
    |     _____ SUSTAIN
    |    /     \
    |   /       \
PEAK|  /         \___
    | /          R   \___
    |/____________       \___
    +---A---D---S----(hold)   +---> TIME
          (Release: R)
```

Attack ramps up, decay settles to sustain (optional), parameter holds at sustain while input active, release decays to zero when input ends.

## Sub-axes / Dimensions

- **Attack time:** Duration (milliseconds) from input to peak
- **Attack curve shape:** Linear, exponential, or custom easing (determines smoothness)
- **Decay time:** Duration from peak to sustain (optional; may be zero)
- **Decay curve shape:** Steep, gentle, or sigmoid
- **Sustain level:** Normalized target value (0–1, or game-specific units)
- **Release time:** Duration from sustain to zero after input release
- **Release curve shape:** Linear, exponential, ease-out
- **Input filtering:** Dead zones, smoothing, or sensitivity mapping before envelope (pp.127–132)
- **State-based variation:** Different envelopes per game state (e.g., ground vs. air, walking vs. sprinting, pp.130–131)

## Knobs (Designer-Tunable)

- **Attack duration (ms):** 0 (instant), 50–100 (responsive), 200–500 (floaty/organic)
- **Attack ramp shape:** Linear, ease-in, ease-out, or custom curve
- **Decay duration (ms):** 0 (none), 50–200 (typical)
- **Decay slope:** Steep, shallow, or S-curve
- **Sustain plateau (0–1):** Often 1.0 (full speed), sometimes <1.0 (cap)
- **Release duration (ms):** 0 (instant stop), 100–300 (organic decel)
- **Release falloff:** Linear or ease-out curve
- **State-driven envelope swap:** Change ADSR values based on avatar state (ground/air, running/walking, p.130)
- **Input sensitivity curve:** Non-linear transformation applied before envelope (pp.131–132)

## Tuning Ranges (From Swink, Page-Cited)

| Property | Range | Feel | Swink Reference |
|----------|-------|------|-----------------|
| Attack time | <100 ms | Responsive, crisp | p.124 |
| Attack time | >100 ms | Floaty, loose, organic | p.124 |
| Attack time (Mario example) | ~500 ms | Organic, smooth acceleration | p.126 |
| Attack curve | Linear, instant on/off | Twitchy, harsh (Figure 7.12) | p.127 |
| Attack + Decay combo | Short + short | Responsive, tight feel (Figure 7.11) | p.125 |
| Attack + Decay combo | Short + long | Responsive but loose (Figure 7.10) | p.125 |
| Release duration | <150 ms | Snappy deceleration | Implied, p.126 |
| Release duration | >300 ms | On-ice, slippery, organic | p.126 |
| Asteroids ship | Long attack, no decay | Feels organic and floaty; players enjoy it (p.124, Figure 7.7) | p.124 |

No universal thresholds apply across all games; tuning depends on game feel intent (arcade vs. sim vs. platformer).

## Diagnostic — Feel Labels Mapped to ADSR Shape

| Player Feel | ADSR Signature | Swink Page |
|-------------|----------------|-----------|
| Tight & responsive | Short attack + hard (instant) release | p.125 (Fig 7.11) |
| Responsive but loose | Short attack + long decay; soft release | p.125 (Fig 7.10) |
| Floaty, unresponsive | Long attack (>100 ms), no immediate visual feedback | p.124 |
| Organic, weighty | Gradual attack ~500 ms + soft decay; natural decel curve | p.126 (Mario) |
| Twitchy, harsh | Very short/instant attack, flat on/off, no curve | p.127 (Fig 7.12) |
| Sluggish | Long attack + slow decay + long release | p.124 |
| On-ice, slippery | Short attack + release >300 ms | Implied, p.126 |
| Crisp, stiff | Instant attack + instant release | p.124 |

## Anti-Patterns

- **Attack >100 ms without immediate visual feedback:** Creates floaty, unresponsive feel when player expects snappiness (p.124).
- **No attack curve, instant on/off:** Feels twitchy and harsh; breaks immersion (p.127, Figure 7.12).
- **Sustain overshoot:** Parameter exceeds intended max; object jolts uncomfortably.
- **Long decay without proper sustain:** Parameter oscillates or drifts unpredictably; loses control feel.
- **Release >300 ms on snappy controls:** Feels like playing on ice; contradicts responsive design intent.
- **Linear easing throughout:** No ease-out at attack/release; feels mechanical and artificial (p.126, Mario contrast).
- **State-based envelope switches without signal:** Player unaware input meaning changed; causes confusion (p.130).
- **Filtering hidden from player:** Input smoothing, dead zones, or delay applied but no visual/audio feedback why input "lags" (p.132).

## Cross-Reference

- **Input (Metric 1):** What produces the signal entering the envelope. See `metric-input.md` for device sensitivity, boundaries, signal formats.
- **Polish (Metric 4):** Visual/audio cues (animation, particles, sound) that mask or enhance ADSR perception. A slightly-too-long attack can be hidden by snappy animation + impact sound. See `metric-polish.md`.
- **Rules (Metric 6):** State machines that trigger envelope changes (ground/air, walking/sprinting). See `metric-rules.md` for state-based modulation.
- **Definition & Perception:** Why >240 ms is a hard ceiling for perceived responsiveness. See `definition-and-perception.md`.

## Summary

Response is how the game translates player intent (input signal) into avatar behavior (parameter change over time). The ADSR envelope is the primary tool for shaping that response. Small tuning changes—shortening attack by 50 ms, softening release curve, adding decay—produce dramatic shifts in feel without changing the underlying simulation. Response is the bridge between input (what the player does) and the world state (what the game shows). Mastering ADSR timing is the fastest path to tight, responsive game feel.
