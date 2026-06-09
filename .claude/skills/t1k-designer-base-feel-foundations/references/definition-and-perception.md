---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Game Feel — Definition & Perception

Foundation reference for the `game-feel-foundations` skill. Sourced from Steve Swink, *Game Feel: A Game Designer's Guide to Virtual Sensation* (Morgan Kaufmann, 2009), Section I (chapters 1–4, pp. 1–61).

---

## 1. The Three-Part Definition

### Core Definition

> "Real-time control of virtual objects in a simulated space, with interactions emphasized by polish." (p. 6)

A game has feel only when all three building blocks are present and well-crafted. Each part has measurable sub-conditions.

### 1.1 Real-Time Control

**Definition (p. 2–3):** The uninterrupted flow of command from player to game, resulting in precise, continuous control over a virtual avatar.

**Sub-conditions:**

- **Interactivity loop (Figure 1.1, p. 2):** Player intent → input expression → computer processing → output (visual/audio/haptic) → perception → formulation of new intent. The cycle repeats continuously, not in turn-based fashion.
- **Instantaneous response (p. 44):** Computer must respond ≤240ms for control to feel unbroken. Above 240ms, control feels "sluggish" and the correction cycle breaks.
- **Tight response (p. 44):** 50–100ms feels "responsive." Below 50ms feels instantaneous; 50–100ms is within one perceptual cycle.
- **Consistent update rate (p. 45):** Computer's half of the loop must update at ≤100ms intervals to mask lag. If updates exceed 150ms, delay becomes noticeable even before motor action completes.

### 1.2 Simulated Space

**Definition (p. 4):** Simulated physical interactions in virtual space, perceived actively by the player.

**Sub-conditions:**

- **Active perception (p. 4–5):** Player must interact with space and perceive results. Passive observation (like film) is insufficient for game feel.
- **Collision detection and response (p. 5):** Without simulated interaction, space is abstract (grids, tiles, turn-based movement). With collision systems, space gains tactile, physical sense.
- **Level design relative to avatar speed (p. 4):** Obstacles must be proportioned to avatar size and movement speed; spacing must feel explorable, not arbitrary.
- **Physics consistency (p. 31):** The game world is a fictional physical reality with designer-created laws. Breaking these laws mid-game shatters the perceptual field.

### 1.3 Polish

**Definition (p. 5):** Any effect that enhances interaction without changing underlying simulation. Animations, sounds, particles, camera behavior, haptic feedback.

**Sub-conditions:**

- **Enhancement, not simulation (p. 5):** Polish provides sensory cues (visual, aural, haptic) that emphasize physicality but do not alter core mechanics.
- **Examples (p. 5–7):** Squash-and-stretch animation on impact, dust particles at avatar feet, crashing sound when objects collide, camera shake, rumble patterns. Each signals weight, material, texture.

### 1.4 Intersection Diagram (Figure 1.6, p. 8)

The three building blocks form a Venn diagram. Game feel exists ONLY in the central region where all three overlap — real-time control AND simulated space AND polish. A game missing any one of the three lives outside this central region and "lacks game feel entirely" (p. 6–9).

```
                   REAL-TIME CONTROL
                          |
                       overlap
                       /     \
                      /  GAME \
                     /   FEEL  \
              SIMULATED ── overlap ── POLISH
                  SPACE
```

**Classification rule (p. 6–9):** Examples Swink uses for the central "all three present" region include Sonic, Half-Life, Super Mario 64, and Asteroids. Examples that fall outside (lack at least one block, therefore lack game feel) include Civilization 4 (lacks real-time control on the avatar) and pure puzzle games like Bejeweled in some senses (lacks simulated physical space). Designers should not over-classify; the fine-grained labels for the 6 non-central regions are not Swink's framing — only the central "game feel" zone has a stable, named meaning in his model.

---

## 2. Perception Thresholds

### 2.1 The Human Perceptual Pipeline (p. 36, Figure 2.1)

Three processors run in parallel:

| Processor | Cycle | Range | Role |
|-----------|-------|-------|------|
| Perceptual | ~100ms | 50–200ms | Detects & interprets sensory input |
| Cognitive | ~70ms | 30–100ms | Plans action & intention |
| Motor | ~70ms | 25–170ms | Translates thought to muscle movement |

**Total Model Human Processor cycle: ~240ms** (Card, Moran, Newell; p. 36) for one complete perception-cognition-action loop.

### 2.2 Frame Rate & Motion Illusion (p. 43–44)

| FPS | Perception | Notes |
|-----|-----------|-------|
| <10 | Static images, no motion | Just flickering; perceptual fusion fails |
| 10–20 | Poor motion illusion | Visible jitter; motion apparent but unpleasant |
| 20–30 | Smooth motion | Acceptable floor; beginning of smooth feel |
| 30+ | Safe modern standard | Most contemporary games target 60 fps |

**Why 10 fps is the floor (p. 44):** Perceptual fusion. Two events in the same ~100ms perceptual cycle are fused as motion by the brain. At 10 fps, each frame appears within one perceptual cycle of the previous → motion is perceived.

### 2.3 Response Time Perception (Figure 2.10, p. 45)

Player perception of "tightness" based on response lag:

| Lag (ms) | Sensation | Label |
|----------|-----------|-------|
| 0–50 | Instantaneous | Player has no perception of delay |
| 50–100 | Within one perceptual cycle | Tight & responsive |
| 100–150 | Noticeable | Sluggish; control feels disconnected |
| >240 | Interruption | Past continuity ceiling; real-time control broken |

**Critical ceiling: 240ms (p. 45).** Beyond this, player perceives state, thinks, and acts before computer responds. Correction cycle is broken; control feels unresponsive.

### 2.4 Fitts' Law (p. 42–43)

**Formula:** MT = a + b·log₂(D/W + 1)

Where:
- MT = movement time
- a = device start/stop time
- b = device speed
- D = distance to target
- W = target width

**Application:** macOS menu bar at screen edge has effectively infinite width → trivially easy to access. Tiny checkboxes or nested submenus require multiple correction cycles. Target size directly affects perceived difficulty.

### 2.5 Continuity & Concurrence (p. 45–46, Figures 2.11–2.12)

**Zero-lag perception occurs when action and response happen in the same perceptual cycle (~100ms).** When they span different cycles, response lag becomes noticeable.

**Design implication (p. 47):** Game must update faster than 100ms to mask lag. If response takes 150ms+ consistently, interruption is unavoidable. Input animation and polish can hide *short* interruptions, but 100–150ms+ lag is always perceived.

### 2.6 Proprioception & Amplified Proprioception (p. 26–28)

**Proprioception (body position sense):** Normally detects muscle tension, joint angle, and pressure.

**In games (p. 28):** Amplified proprioceptive feedback via screen, speaker, and controller feedback. Thumbstick movement is tiny; brain receives amplified feedback from screen showing large avatar motion.

**Effect:** Players experience game feel as amplified proprioceptive sensation — an illusion, but experienced as real by the senses and integrated into body-position sense.

---

## 3. The Interactivity Model

### 3.1 The Conversation Loop (Figure 1.3, p. 3)

```
PLAYER                          COMPUTER
(senses) ←→ (brain) ←→ (muscles) ←→ [Listen-Think-Speak]
                                    ← Display (output)
```

**Important note (p. 3):** This is NOT a conversation in the human sense. Conversation implies turn-taking; real-time control is continuous, like driving a car. Player wants to turn left; turns wheel; feedback comes moment-to-moment from sight, sound, and feel.

### 3.2 The Six-Stage Loop (Figure 1.1, p. 2)

1. Player has intent
2. Player expresses intent via input
3. Computer processes input
4. Computer outputs results
5. Player perceives changes
6. Player formulates new intent → repeat

Faster loop cycle = tighter feel.

### 3.3 Five Game-Feel Experiences (Figure 1.7, p. 13)

The three building blocks feed into five distinct experiences:

1. **Aesthetic Sensation of Control:** Joy of controlling something; feeling body respond instantly to impulses.
2. **Pleasure of Learning, Practicing, Mastering Skill:** Progression from clumsy to intuitive; frustration to mastery.
3. **Extension of the Senses:** Avatar becomes surrogate body; game world becomes real via sensory feedback.
4. **Extension of Identity:** "I am awesome!" Identity flows into avatar; player becomes the character.
5. **Interaction with a Unique Physical Reality:** Fictional physics (gravity, friction, mass) create impression of physicality distinct from real world.

---

## 4. Mechanics — Designer-Changeable Parameters

### 4.1 Real-Time Control Mechanics (p. 14–20)

**Input-to-motion mapping (p. 14–15):**
- Is response gradual or immediate?
- Does avatar move screen-relative or world-relative?
- What motions are possible with this mapping?
- Aesthetic quality: smooth Porsche feel vs. stiff, rigid Jeep. Same input → different response curve → different feel.

**Challenge alters sensation of control (p. 15–23):**
- Flow state (Csikszentmihalyi, Figure 1.15, p. 23): Challenge and skill must be balanced for maximum engagement.
- Player skill changes felt tightness: novice feels clumsy and disoriented; expert feels smooth and responsive. Same controls; different skill = different feel.
- Designer controls: challenge level, challenge scaling speed, skill threshold for flow entry.

**Goals and constraints shape exploration (p. 18–20):**
- Goals focus player on specific motions within the possibility space. Example: fly mechanic in Super Mario World only unlocks when goal is present; without goal, controls feel "bloated."
- Constraints explicitly remove motions from possibility space. Example: sidelines in football eliminate backward/off-field directions.
- Same control mapping feels different when goals and constraints change available motion space.

### 4.2 Simulated Space Mechanics (p. 4–5)

Designer controls:
1. Collision detection system (what blocks what)
2. Response behavior (bounce, stick, slide)
3. Level design (obstacle spacing relative to avatar speed/size)
4. Physics parameters (gravity, friction, mass)
5. Spatial layout (room for exploration vs. linear)

### 4.3 Polish Mechanics (p. 5–7, Figures 1.4–1.5)

Designer controls:
1. **Animation frames & timing:** Squash-and-stretch (Figure 1.5, p. 6); key frames suggesting weight and momentum.
2. **Particle effects:** Dust, sparks, splashes. Cue physical properties (weight, density, material).
3. **Sound design:** Pitch, timbre, loudness. Subtle but powerful effect on perception of physicality (p. 7).
4. **Camera behavior (p. 25–26):** Smooth vs. jittery; defines player's impression of speed.
5. **Haptic feedback:** Rumble motor strength, pattern, timing. Amplifies proprioceptive feedback (p. 28).

**Example (p. 6–7):** De Blob without animation looks like "weird fighting boxes." With squash-stretch animation, it feels like collision of physical objects.

---

## 5. Designer Quick-Reference Checklist

Observable, binary checks organized by category:

**Real-Time Control:**
- [ ] Is response ≤240ms? (Hard ceiling)
- [ ] Is response ≤100ms? (Tight)
- [ ] Does game update ≥10 fps? (Motion illusion floor)
- [ ] Is input-to-motion mapping consistent across states?
- [ ] Does camera behavior support, not fight, control feel?
- [ ] Does control feel like extension of player intent?

**Simulated Space:**
- [ ] Is there meaningful collision detection?
- [ ] Do objects respond consistently to avatar interaction?
- [ ] Is spatial layout proportioned to avatar speed/size?
- [ ] Do physics feel internally consistent?
- [ ] Can player feel weight and materiality through movement?

**Polish:**
- [ ] Do animations telegraph impact/weight (squash, stretch)?
- [ ] Do sounds correspond to interaction type?
- [ ] Are particle effects used to show material properties?
- [ ] Does haptic feedback reinforce visual/audio cues?
- [ ] Is camera movement smooth and supportive?

**Challenge & Flow:**
- [ ] Is challenge appropriate for current player skill?
- [ ] Can player see clear goals within the possibility space?
- [ ] Are constraints placed to focus exploration?
- [ ] Does control feel clumsy at low skill, intuitive at high skill?

**Perception & Identity:**
- [ ] Does avatar feel like extension of player body?
- [ ] Can player feel ownership of avatar actions?
- [ ] Is game world physics consistent enough to build reliable mental model?
- [ ] Do sensory cues (visual + audio + haptic) combine into coherent impression?

---

## 6. Key Insights for Implementation

1. **Game feel is measurable, not mystical.** Concrete thresholds (240ms, 100ms, 10 fps) define where feel breaks down.

2. **Game feel is deliberate design.** All three blocks must be present and well-crafted. Omitting any one (real-time control, simulated space, or polish) loses feel entirely.

3. **Perception shapes feel.** Designers must engineer feel knowing how human perception works: parallel processors, 240ms feedback loop, perceptual fusion at 10 fps.

4. **Skill is central to feel.** Game feel is tied to player skill progression. The arc from clumsy to intuitive IS the game feel, not a side effect.

5. **Consistency is everything.** A game world is a fictional perceptual reality. Breaking physics, unexpectedly delaying response, or conflicting sensory cues shatters immersion.

---

**Document sourced from:** Steve Swink, *Game Feel: A Game Designer's Guide to Virtual Sensation* (Morgan Kaufmann, 2009), Section I, Chapters 1–4, pages 1–61.
