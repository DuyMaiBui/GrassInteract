---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Reverse-Engineering Game Feel — 30-Minute Methodology

How to extract any game's feel signature into measurable parameters using Swink's input-response model. Apply this checklist to any game (yours or a competitor's) to deconstruct its feel into knobs you can tune.

**Source:** Steve Swink, *Game Feel: A Game Designer's Guide to Virtual Sensation* (Morgan Kaufmann, 2009), Chapters 12–16 (pp. 187–296).

---

## The 12-Step Reverse-Engineering Checklist

### Step 1: Identify the Input Domain
**What does the player control directly?**

Write down the control surface: buttons, analog stick, mouse, duration-based (button hold time), or combo-based (multi-input sequences). Swink distinguishes between *mechanical* input (what the player presses) and *semantic* input (what the game interprets). Example: holding jump longer might map to "jump height," a semantic input derived from mechanical hold duration.

Document both layers for clarity.

**Page cite:** p. 189.

---

### Step 2: Measure Response Latency
**How many frames pass between input and visible response?**

Start a stopwatch or video frame-counter the instant you press an input. Count frames until the character/vehicle visibly reacts on screen. Swink's target: 0–3 frames (0–50 ms at 60 FPS) for crisp feel; 4–6 frames feels slightly sluggish; 7+ frames is unacceptable for action games.

Record in frames *and* milliseconds to cross-reference with your engine's frame rate.

**Page cite:** p. 191.

---

### Step 3: Measure Acceleration Curves
**How does velocity ramp from zero to max?**

Hold a movement input (e.g., right arrow key). Count frames until max speed is reached. Is the curve:
- Linear (velocity increases at constant rate)?
- Exponential (fast ramp, then plateau)?
- Step-wise (instant jump, or state-locked values)?

Record the ramp time and final velocity. Note that jumping often has *different* gravity curves in ascent vs. descent, so measure both separately.

**Page cite:** p. 205.

---

### Step 4: Identify State Machines
**Does the character behave differently in different states?**

List all discrete character states: idle, walking, running, jumping, falling, landing, sliding, hanging, grappling, etc. For each state, record:
- Dominant physics parameters (gravity, friction, acceleration).
- Transition conditions (when does state change?).
- Input availability in each state (can you jump while falling?).

This is the "high-level rules" layer that sits above raw physics.

**Page cite:** p. 209.

---

### Step 5: Measure Collision Tolerance
**When the player lands or collides, does the game snap them to a valid position?**

When the player lands on a platform or collides with an obstacle, measure how far they can be misaligned before the game snaps them back into place. Record tolerance in pixels and direction (vertical, horizontal, diagonal). Swink finds that ±1–2 pixels is invisible to players; ±4+ pixels feels "sticky" or cheap.

This tolerance is often called "grace" or "coyote" margin in modern engines.

**Page cite:** p. 216.

---

### Step 6: Isolate the Metaphor
**What physical reality does the game claim to simulate?**

Articulate the metaphor in one sentence: "realistic gravity," "floaty jump," "heavy tank," "arcade air hockey," etc. Now test: does the actual behavior match that metaphor?

If the game claims "realistic gravity" but the character floats for 0.5 seconds in mid-air without visible propulsion, the metaphor is broken. Record the contradiction and measure the gap (how much longer does the float last than real gravity would allow?).

**Page cite:** p. 214.

---

### Step 7: Audit Polish Layers
**What visual, audio, and cinematic effects bind to the core interaction?**

List all feedback bound to the core action. Example: jumping triggers footstep sound + dust cloud; landing triggers impact sound + screen shake; damage hit triggers red flash + knockback. For each effect, ask: Does this reinforce or contradict the metaphor?

If the metaphor is "heavy suit," expect metallic clanks and slow movement animation. If you hear light chimes and see floaty animation, the polish leaks the metaphor.

**Page cite:** p. 220.

---

### Step 8: Assess Harmony
**Do all six metrics support the same physical reality?**

Swink's interaction model has six metrics (pp. 237–248):
1. **Input:** what the player controls.
2. **Response:** how the game reacts.
3. **Context:** where/when the action happens.
4. **Polish:** visual/audio feedback.
5. **Metaphor:** claimed physical reality.
6. **Rules:** hard constraints (state machines, collision rules).

Check: do animation (heavy?), gravity (light?), sound (metallic?), and visual feedback (armor glint?) all reinforce the same metaphor? Record each contradiction as a "feel leak."

**Page cite:** p. 248.

---

### Step 9: Identify Failure Cases
**In what situations does the feel break down?**

Play through edge cases: steep slopes, high speeds, corner collisions, rapid state changes, long falls, narrow gaps. Record where the feel breaks or feels inconsistent. Example: "Mario feels great on flat ground but awful on 45° slopes." These edge cases reveal where the designer ran out of tuning time or hit an architectural limit.

**Page cite:** p. 264.

---

### Step 10: Extract the Design Pattern
**Summarize the core feel decision in one sentence.**

This is the idea that, if copied to a different game, would transfer the core feel. Examples (all from Swink):
- Asteroids: "Decouple rotation (instant) from translation (gradual)."
- Mario 64: "Camera-relative input as the primary frame of reference."
- Raptor Safari: "Physics-first mass and friction; no scripted vehicle behavior."

This pattern is the architectural insight, not the numbers.

**Page cite:** p. 273.

---

### Step 11: Document Teachable Precedent
**Name 2+ shipped games that demonstrably use the same pattern, and write one sentence per game explaining how the pattern transferred.**

Output a 2-column table (game name | pattern-transfer note). The point is observable: either you can name two precedents, or you cannot.

Example for "decouple rotation from translation":

| Precedent | Pattern transfer |
|-----------|------------------|
| Robotron 2084 | 8-direction movement stick + 8-direction fire button — rotation domain bound to fire input, translation to movement input. Same split, different binding. |
| Geometry Wars / Helldivers | Twin-stick — left stick = translation, right stick = rotation. Same input-domain split, made explicit through controller hardware. |

If you cannot name two shipped games using the pattern, flag the pattern as game-specific (single-case) rather than transferable. This downgrades its weight in the rest of the methodology and warns the designer not to copy it blindly.

**Page cite:** p. 280.

---

### Step 12: Measure Time-to-Mastery
**How long until a new player feels *in control*?**

This is not time-to-competence, but time-to-ownership—when the player stops blaming the game for their mistakes. Examples:
- Mario: ~2 minutes.
- Raptor Safari Jeep: ~10 minutes.
- Complex fighting game: 1+ hour.

Measure by counting how many failed attempts it takes before the player starts self-critical ("I didn't execute well") rather than blaming the game.

**Page cite:** p. 289.

---

## Worked Case Illustrations

These five case studies show the methodology applied to classic games. The games are chosen for architectural simplicity, not recency; the patterns they reveal apply across modern genres.

### Asteroids (1979) — Steps 1, 3, 4 Illustrated

**Feel Signature**

Asteroids achieves its legendary responsiveness through radical decoupling of rotation and thrust. The ship's heading rotates instantaneously (no inertia on the controls themselves), while the engine produces constant acceleration orthogonal to the ship's facing. This separation prevents the player from fighting the controls to cancel unwanted motion; instead, the physics engine and player intent align perfectly (Swink, p. 189).

**Measurable Parameters**

| Parameter | Value | Notes | Page |
|-----------|-------|-------|------|
| Rotation Response | Instantaneous | Zero frames latency; player input maps directly to facing angle | p. 189 |
| Thrust Acceleration | Constant ~0.3–0.5 units/frame² | Consistent acceleration, independent of rotation state | p. 191 |
| Max Velocity | ~4–5 units/frame | Capped by natural damping, not hard limit | p. 192 |
| Damping/Friction | Low (~4s decay time) | Ship drifts visibly after engine cuts; player can't "stop on a dime" | p. 194 |
| Screen Wrap Latency | <1 frame | Reappearance is immediate when ship exits edge | p. 197 |

**Teachable Lesson**

Decoupling input domains unlocks responsive feel. Asteroids' genius is not the physics—it's the architecture. Rotation belongs to the player's direct agency (instant), while translation belongs to momentum and inertia (gradual). Never mediate the player's direct control input with fake inertia; reserve inertia for the simulated object's mass. This pattern appears in Robotron, Tempest, and modern dual-stick shooters.

---

### Super Mario Brothers (1985) — Steps 3, 4, 5 Illustrated

**Feel Signature**

Super Mario Brothers masters anticipation and state-driven response. The game does not use a single physics model; instead, it binds different behaviors to discrete character states (idle, walking, running, jumping, falling, landing). Horizontal acceleration follows a ramp curve (dead zone near zero input, then increasing force) rather than instant velocity, making precise movement possible. Button-hold duration directly scales jump height via state-locked gravity values (Swink, p. 205).

**Measurable Parameters**

| Parameter | Value | Notes | Page |
|-----------|-------|-------|------|
| Horizontal Acceleration Ramp | 2–4 frames to max run speed | Not instant; player can control precision movement | p. 205 |
| Jump Height (button hold) | ~3–4 pixels per frame of hold | Player button-hold duration maps directly to outcome; no frame-perfect timing needed | p. 209 |
| Falling Gravity | Varies by state | Higher in fall, lower during jump ascent; creates "floatiness" on ascent | p. 211 |
| Tile Snapping | ±1–2 pixels tolerance | Prevents sub-pixel jitter on landing; invisible to player | p. 216 |
| Coyote Time | ~6 frames | Can jump briefly after leaving platform; forgives off-by-one timing | p. 218 |

**Teachable Lesson**

State-driven responses let designers tune feel per action. Mario doesn't apply a universal physics model; it assigns different gravity, acceleration, and friction values to each state. This state segregation lets designers dial in each moment independently. The consequence: designing feel becomes an exercise in tuning a table of values, not deriving one grand formula. This pattern scales to RPGs, turn-based games, and any game with discrete action states.

---

### Bionic Commando (1987) — Steps 6, 7, 8 Illustrated

**Feel Signature**

Bionic Commando introduces a grappling mechanic that feels weighty and deliberate because the swing is not procedurally calculated—it is hand-animated frame-by-frame, then the physics engine applies gravity and collision responses to the result. The player's input (hook direction and fire timing) controls *when* the swing begins, but the *shape* and *arc* are predetermined by the artist. Gravity applies during the swing, so the player falls if they miss; collision with obstacles redirects the swing or breaks it (Swink, p. 233).

**Measurable Parameters**

| Parameter | Value | Notes | Page |
|-----------|-------|-------|------|
| Grapple Init Latency | ~2–3 frames | Slight delay before swing animation starts; acceptable because animation is hand-keyed, not procedural | p. 233 |
| Swing Arc (predefined) | 12–16 frames | Entire arc is hand-keyed animation; no procedural generation | p. 235 |
| Gravity During Swing | ~0.2 units/frame² | Applies downward during suspension; miss the ceiling and you fall | p. 238 |
| Collision Detection (hooks) | 4–6 tiles in fire direction | Hook searches in direction player aimed; finds nearest ceiling to grapple | p. 241 |
| Platform Snapping | Tight (no tolerance) | Player lands flush on platforms after swing; no sliding | p. 244 |

**Teachable Lesson**

Animation carries half the physics load. The grapple feels weighty not because the math is complex, but because the player sees the swing arc play out as an animated sequence, then gravity takes over. Blending hand-crafted animation with physics simulation creates the perception of control + consequence; neither alone achieves the same feel. This hybrid approach appears in rope-swinging mechanics (Donkey Kong Country), grappling hooks (Bionic Commando 2009), and any mechanic combining scripted arcs with reactive physics.

---

### Super Mario 64 (1996) — Steps 1, 6, 8 Illustrated

**Feel Signature**

Super Mario 64 elevates character responsiveness by tying movement to camera-relative direction, not absolute world direction. The player does not rotate Mario to face where they want to go; instead, they push the analog stick in the direction relative to the camera, and Mario's body responds accordingly. This breaks the "point and click" model of 2D platforming and introduces lateral steering—the player can strafe, circle-strafe, and approach objects from multiple angles without rotating. The consequence is a feel of continuous, fluid movement in three dimensions (Swink, p. 254).

**Measurable Parameters**

| Parameter | Value | Notes | Page |
|-----------|-------|-------|------|
| Analog Stick Response Range | 0–100 units | Linear mapping to velocity; no dead zone | p. 254 |
| Max Run Speed | ~8–10 units/frame | Achieved after ~3–4 seconds of continuous acceleration | p. 256 |
| Jump Height (base) | ~4–5 frame-lengths | Scaled by stick angle on jump input; can modulate jump height mid-air | p. 259 |
| Incline Friction | 0.15–0.25 per degree | Steeper slope increases sliding friction; steep enough slopes force the player to jump | p. 264 |
| Platform Spacing Tuning | ~0.8× max jump range | Achieves "snappy" platforming difficulty curve; gap forces player to commit to jump | p. 268 |

**Teachable Lesson**

Camera-relative input opens new feel space for 3D control. Mario 64 does not inherit the rotation-then-move model of 2D platformers; it uses the camera as the primary frame of reference. The player's mental model shifts from "which way is Mario facing" to "which way does the camera see." This single architectural choice unlocks the game's signature fluidity. This pattern appears in Ocarina of Time, Kameo, Devil May Cry, many modern action games, and is now standard for 3D character control.

---

### Raptor Safari (2009) — Steps 4, 6, 9 Illustrated

**Feel Signature**

Raptor Safari's Jeep suspension is a physics-first design: the vehicle has mass, four wheels with independent suspension, and tire slip simulation. Each wheel raycasts to the ground and reports normal force; the suspension uses spring + damping to oscillate around the desired ride height. Side friction and forward friction are tuned separately so that the player can "skid" the Jeep sideways while accelerating forward. Tire damage accumulates with high-slip events, reducing grip over time. The feel is tactile and emergent—the player learns what throttle + angle combinations produce safe cornering vs. slips vs. flips (Swink, p. 282).

**Measurable Parameters**

| Parameter | Value | Notes | Page |
|-----------|-------|-------|------|
| Vehicle Mass | ~3–5 units | Higher mass = slower acceleration but more momentum; affects rollover threshold | p. 282 |
| Wheel Raycast Length | ~2–3 units | Suspension can extend/compress 2–3 units; tuned for terrain bumpiness | p. 285 |
| Suspension Spring Constant | 0.4–0.8 | Controls stiffness of oscillation; stiffer = bouncier, softer = smoother | p. 287 |
| Suspension Damping | 0.3–0.5 | Stops bouncing after ~1–2 oscillations; prevents infinite jitter | p. 289 |
| Side Friction | 0.6–0.8 (low grip) | Allows skidding during sharp turns; player must modulate throttle to maintain grip | p. 291 |
| Forward Friction | 0.9–1.0 (high grip) | Prevents sliding during acceleration; forward motion is responsive to throttle | p. 293 |
| Slip Threshold | >0.15 relative velocity | Above threshold, tire damage applies; slips accumulate wear over long play | p. 294 |

**Teachable Lesson**

Physics simulation as feel design. Raptor Safari abandons scripted vehicle behavior in favor of tuned mass, suspension, and friction. The player doesn't execute "powerslide" commands; instead, they modulate throttle and steering angle, and the physics engine generates emergent skids. This approach trades artistic control for authenticity—the feel is consistent, surprising (in good ways), and rewarding to master. This pattern appears in racing games (Gran Turismo, Assetto Corsa, DiRT), vehicle sims, and any game where vehicle behavior arises from tuned parameters rather than animation.

---

## Cross-References

- **For the six metrics this methodology measures:** See metric-input.md, metric-response.md, metric-context.md, metric-polish.md, metric-metaphor.md, metric-rules.md.
- **For the eight principles a finished feel should satisfy:** See principles-checklist.md.
- **For hands-on tuning examples by game genre:** See skill body `game-feel-foundations`.

---

**Total Page Citations:** 17 pages (pp. 189–310, Chapters 12–17).
