---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Polish — Game Feel Metric 4

Artificial sensory cues that reinforce the perceived physical properties of objects and actions. Polish is *measurement* of how much feedback an interaction carries across five channels; **juice** (sibling skill) is the *tactical toolbox* for adding it (screen shake, particles, hit-stop). Source: Swink Ch.9 (pp.151-170).

## Theory vs. Tactics

- **This file (Polish metric)** = HOW MUCH game-feel reinforcement an action carries, and across which channels (animation, VFX, SFX, cinematic, tactile). Measurement and diagnosis.
- **`game-feel-juice` skill** = the TACTICAL TOOLBOX for adding that reinforcement (specific shake values, hit-stop frame counts, particle stagger timings, easing curves, audio material mappings).
- Designers use **Polish (this file)** to DIAGNOSE what's missing; they use **juice** to FIX it.

## Definition

Polish is any effect that creates artificial cues about the physical properties of objects through interaction (Swink, p.151). It is decorative and non-systemic—it does not change the simulation, only the player's perception of weight, mass, solidity, and impact. Polish is essential because a heavy simulation without polish feels cold and mechanical; Polish without coherent simulation feels fake.

Polish is measured as a designer chooses which effects to deploy, at what frequency, and with what intensity.

## Five Channels of Polish

### 1. Animation

Disney 12 principles applied to game feel:
- **Squash-stretch:** Compression on impact conveys weight; extension on rebound conveys energy
- **Anticipation:** Wind-up before motion telegraphs intent (Swink, p.156; crouch before jump example from *Principles of Physical Animation*)
- **Follow-through & overlapping action:** Body parts follow in sequence, not in unison
- **Staging, timing, spacing:** Clear presentation of motion curves and arcs

**Application:** Animation changes perceived weight without changing physics. Jak's run cycle in Jak and Daxter conveys mass through squash-stretch on landing despite identical physics to animation-less simulation (Swink, p.157).

### 2. Visual Effects (VFX)

Supplementary short-lived effects that convey motion, impact, and personality (Swink, p.158).

| Type | Example | Purpose |
|------|---------|---------|
| **Particles** | Sparks on impact, dust on land, smoke trails | Convey friction, weight, environment |
| **Trails** | Sword arcs, projectile paths | Convey speed and power (Soul Calibur example, p.158) |
| **Planes/billboards** | Star bursts, floating numbers | Convey joy, impact, feedback |
| **Deformation** | Impact mesh distortion, dent ripple | Convey force and material property |

**Principle:** VFX don't need realism. Soul Calibur's stylized clash sparks are "effective, satisfying and worthwhile" (Swink, p.159) because they convey impact, not realism.

### 3. Sound Effects (SFX) — Audio Polish is Half of Polish

Sound is the most underutilized game-feel tool. Swink argues sound can completely change perception without animation or simulation change (Swink, p.159-161).

| Audio Layer | Role | Swink Example |
|-------------|------|--------------|
| **Impact foley** | Crack/thud on hit-confirm; material-specific pitch varies (metal, wood, stone) | God of War: deep "crunch" transformed weak attack into weighty blow (p.160) |
| **Material variation** | Pitch and decay convey mass; metal clang ≠ wood thud | Two red balls: "poing!" sound changed perception from pass-through to bounce (p.159, CH09-1) |
| **Grind modulation** | Looped sound with pitch/volume modulation | Tony Hawk's grind sound conveys duration, surface, speed (p.160) |
| **Spatial reverb** | Echo and decay convey space size and material | Hammer hit ground; echo alone conveys environment (p.160) |
| **Layered stings** | Music cue over impact sound for critical hits | Reinforces importance and magnitude |

**Principle:** Audio polish is NOT optional for game feel. Swink emphasizes it as equal-weight to visual polish (p.159-161). Many designers discover they've polished animation and VFX perfectly—then audio adds the missing ingredient.

### 4. Cinematic Effects

Post-processing and camera effects that emphasize impact, speed, and drama (borrowed from film, p.161).

- **Screen shake:** High-frequency vibration (e.g., explosion impact)
- **Slow-motion:** Frame reduction (50-80% speed) for 200-400ms on major hits
- **Motion blur:** Camera-space blur to exaggerate speed
- **View angle shift:** Subtle 1-3° tilt or major >10° angle for emphasis
- **Chromatic aberration, bloom, vignette:** Post-processing for style

**Swink's example:** Off-Road Velociraptors (p.161) — zooming in + slow-motion on hit without changing collision response emphasizes impact.

### 5. Tactile / Haptic Feedback

Weight-loaded motor feedback on modern controllers. Subtle and easily over-used (Swink, p.162).

| Rumble Type | Use | Principle |
|-------------|-----|-----------|
| **Impact rumble** | Brief, high-amplitude burst on collision | Metaphor: gun recoil, impact shock |
| **Constant rumble** | Low-frequency vibration (engine running, sustained effect) | Grounding and presence |
| **Grinding rumble** | Modulated intensity (grating, friction effects) | Tony Hawk-style duration and surface feedback |

**Subtlety:** Swink notes every action triggering rumble becomes noise. Best applications use rumble sparingly and meaningfully.

## Tunable Knobs per Channel

| Channel | Tunable Parameter | Range |
|---------|-------------------|-------|
| **Animation** | Frame duration, ease curve (linear, ease-out, spring), squash ratio | Anticipation: 60-120ms; land compress: 0.8-1.2 scale (p.156-157) |
| **VFX** | Particle count, lifetime, scatter angle, color/emission, spawn shape | Light hit: 5-8 sparks, 0.2s; heavy hit: 12-20 shards, 0.5s |
| **SFX** | Material selection, pitch (relative to object mass), decay duration, foley variation | Impact: 30-80ms; grind loop: 0.1-0.5s modulation |
| **Cinematic** | Shake amplitude, frequency, duration; slow-motion ratio, duration | Light hit: 0.05-0.10 amplitude, 0.15s decay; boss: 0.8-1.0 amplitude, 0.6s (p.161) |
| **Tactile** | Rumble intensity (light, medium, heavy), duration, pattern | Light: 50ms; heavy: 100-150ms; impact: high freq + decay |

## Diagnostic Questions

- Does collision **feel heavy or light** relative to object appearance?
- Do **sound and animation reinforce** each other, or contradict?
- Are **channels synchronized**? (animation leads, particles follow 1-2 frames, sound arrives by frame 3)
- Is the player receiving **feedback about properties** they don't explicitly control (mass, material, environment)?
- Is **polish frequency appropriate**? (Too much = noise; too little = cold)
- Do effects **feel integrated** with the game's metaphor, or tacked-on?

## Anti-patterns

| Anti-pattern | Problem | Fix |
|---|---|---|
| Over-polished | Effects exaggerate past metaphor fidelity | Scale back; use restraint; A/B test with player perception |
| Under-polished | Collision feels weightless despite solid simulation | Add all five channels; prioritize audio + animation |
| Muddy effects | Too many overlapping particles/sounds | Reduce particle count; layer SFX distinctly; timings stagger |
| Mismatched channels | Heavy object with light-sounding impact | Vary SFX pitch/decay by object mass; match animation squash to sound duration |
| Lag between stimulus and effect | Animation finishes before sound plays; shake ends before particles fade | Synchronize frame-by-frame; particle lifetime ≥ animation end ≥ audio sustain onset |
| "Polish rabbit hole" | Development costs spiral with diminishing ROI (Swink, p.151) | Set polish budget (2-3 effects per action, 30% of animation pass); ship on time |
| Broken metaphor in polish | Realistic character with cartoonish sound design, or vice versa | Choose metaphor (realistic, stylized, fantasy); keep polish consistent within it |

## Cross-reference

### For tactical polish implementation
For **concrete parameter values, animation easing curves, particle stagger timings, hit-stop frame ranges, material-based SFX mappings, and rumble pattern tables**—see the `game-feel-juice` skill. **Polish (this file) tells you WHAT to add and across which channels; juice tells you the SPECIFIC VALUES and implementation patterns.**

### For other metrics
- **Response (underlying motion):** see `metric-response.md` — Polish reinforces response through feedback
- **Metaphor (consistency):** see `metric-metaphor.md` — Polish must respect the game's chosen metaphor
- **Context (spatial framing of the action — same polish reads differently in tight halls vs. open fields):** see `metric-context.md`
- **Input/Rules:** see their metric files — Polish operates at the perceptual layer above these

## Notes

- All five channels contribute to single "heaviness" perception — designer groups them
- Polish is soft metric: no single "good" value, only diagnosis via questions above
- Swink devotes Ch.9 entirely to Polish because designers often overlook it in favor of simulation
- Audio polish is the most frequently missing channel; prioritize sound in polish pass
