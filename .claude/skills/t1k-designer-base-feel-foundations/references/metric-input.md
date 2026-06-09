---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Input — Game Feel Metric 1

The player's tactile control surface—the device they hold and the signals it produces. Source: Swink, *Game Feel*, Ch.6 (pp.101-118).

## Definition

**Input** is the hardware device through which the player communicates with the game: controller, keyboard, mouse, touchscreen, paddle, etc. It is the most immutable part of the feel equation because designers rarely control the physical device itself. However, understanding input is essential: input characteristics directly shape what responsive feedback is possible. Input is measured by the type of motion it captures, the dimensions in which it moves, its physical and soft boundaries, the sensitivity (number of possible states), and the format and timing of signals it sends to the game (p.101-103).

## Sub-axes / Dimensions of Measurement

**Type of Motion (p.104-105):**
- Linear motion — movement in a straight line along one axis (e.g., D-pad button press)
- Rotational motion — movement around a center point (e.g., thumbstick, paddle)
- Position-based motion — absolute position in space (e.g., mouse, touchscreen)

**Dimensions of Motion (p.104-105):**
- Single axis — one direction of motion (e.g., button Y-axis)
- Two axes — X and Y or equivalent (e.g., mouse, thumbstick)
- Three axes — rare in consumer controllers

**Direct vs. Indirect Input (p.105):**
- Direct input — player touches the control; change happens in-game immediately (e.g., mouse movement)
- Indirect input — player presses button; something else changes (e.g., button triggers an action after processing)

**Boundaries on Motion (p.105-106):**
- Hard boundaries — physical stops preventing motion beyond them (e.g., fully pressed button, thumbstick housing)
- Soft boundaries — no physical limit but practical edge (e.g., mouse falling off desk, screen edge)

## Device Sensitivity by Type (p.106-115)

Measured by the number of distinct states the input device can represent:

| Device | States | Signal Type | Notes |
|--------|--------|-------------|-------|
| Standard Button | 2 | Binary (on/off) | Mutually exclusive with other buttons |
| Trigger Button | 4–5 | Float (0.0–1.0) | Discrete positions between fully off and fully pressed (p.110) |
| Paddle (Breakout) | ~100 | Float (–1.0 to 1.0) | Hundreds of states between extremes (p.111) |
| Thumbstick | 1,000+ | Float pairs (X, Y) | Thousands of states per axis (p.112); often –1.0 to 1.0 range |
| Mouse | Millions | Float (per axis) | Millions of possible states (p.113); can send ~60 (X, Z) pairs per second at 60 Hz |

**Key insight (p.112-113):** More states = more expressiveness but also more risk of unintended drift. Mouse provides the greatest sensitivity; button press the least.

## Signals Sent (p.107)

Devices communicate via three signal formats:

- **Binary signal** — "pressed" or "released" (e.g., standard button)
- **Float value** — continuous range from –1.0 to 1.0 (e.g., thumbstick axis, trigger)
- **Pair of floats** — two axes simultaneously (e.g., mouse X and Z, or thumbstick X and Y)

## Knobs (Designer-Tunable Parameters)

- **Sensitivity multiplier** — scale raw input values before passing to response layer
- **Dead zone size** — region around neutral position where input is ignored (e.g., thumbstick center tolerance)
- **Input filtering/smoothing** — average or blur input signals over multiple frames
- **Button mapping** — which input signals map to which actions
- **Control-display ratio** — relationship between input displacement and on-screen movement (e.g., 1 inch of mouse movement = X pixels)
- **Boundary enforcement** — placement of hard and soft boundaries

No quantitative threshold for "good" control-display ratio is given by Swink (p.113); it varies by genre and game.

## Input Space Combinatorics (p.115-116)

The overall expressiveness of an input device is greater than the sum of its parts. An NES controller with D-pad and six buttons has lower total sensitivity than if all buttons could be pressed simultaneously in all combinations, because buttons have mechanical constraints (p.115). However, the combinations that *are* possible create an expressive input space that exceeds any single button's expressiveness.

## Tactile Design (p.117-118)

Physical device qualities affect feel but are difficult to quantify:

- **Weight** — heavier controllers feel more solid and intentional
- **Materials** — plastic feels cheap; rubber or metal feels premium
- **Button quality** — spring resistance, tactile click, travel distance
- **Button spacing** — affects hand speed and precision between inputs

No quantitative thresholds given by Swink for these properties (p.120).

## Diagnostic Questions

- How many distinct states can this input device produce?
- What are the physical boundaries, and do they match the intended control feel?
- Is the input sensitive enough for the task, or is it over-sensitive (causing drift)?
- Does the device send continuous data (float) or discrete states (binary)?
- Are the axes aligned with the intended player action?
- Can multiple inputs combine to create new states, or are they mutually exclusive?

## Anti-patterns

- **Over-sensitivity:** Too many states causing unintended micro-adjustments and loss of control (e.g., mouse with 1 pixel = 1 game unit)
- **Under-sensitivity:** Too few states, causing coarse control and inflexibility (e.g., D-pad-only when fluid aiming is required)
- **Mismatched dimensions:** Input device moves in 2D but the avatar moves in 1D, or vice versa
- **Boundary surprise:** Player discovers a boundary exists only after hitting it
- **Indirect input overuse:** Too many layers of indirection between input and response, losing sense of direct control
- **Soft boundary invisibility:** No visual or tactile feedback when approaching a soft boundary

## Cross-reference

- For Response metric (what happens AFTER input registers): see `metric-response.md`
- For Polish metric (sensory feedback ON input): see `metric-polish.md`
- For Context metric (spatial constraints on input feel): see `metric-context.md`
