---
name: t1k:designer:base:feel-foundations
description: Game feel theory and measurement — Swink's definition, perception thresholds, 6-metric framework, 8 principles, reverse-engineering. Theory; for tactics see game-feel-juice.
effort: high
context: fork
keywords: [game-feel theory, virtual sensation, real-time control, simulated space, perception threshold, response time, frame rate floor, ADSR envelope, input metric, response metric, context metric, metaphor metric, rules metric, swink, game feel principles, floaty, twitchy, sluggish, responsive, tight, loose]
version: 1.13.2
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---

# Game Feel Foundations

## When This Skill Triggers

- Diagnosing *why* a game feels floaty, twitchy, sluggish, mushy, or autopilot
- Establishing perception-threshold targets (frame budget, input-to-response latency)
- Measuring an existing prototype against the 6-metric framework
- Reverse-engineering the feel of a reference game into measurable parameters
- Auditing a build against the 8 principles of good game feel
- Defining what "feels right" before tuning numbers in a tactical pass
- Picking between *theory* (this skill) and *tactics* (game-feel-juice) when unsure

## Decision Tree

| User intent | Route to |
|---|---|
| "Make it feel better" / general / no diagnosis yet | 6-metric framework + 8 principles checklist (this SKILL.md) |
| "Why is X floaty / twitchy / sluggish / mushy?" | `references/metric-response.md` (ADSR envelope diagnosis) |
| "What numbers should I target?" | `references/definition-and-perception.md` (perception thresholds) |
| "How do I reverse-engineer game X's feel?" | `references/case-study-methodology.md` (12-step checklist) |
| "Audit my build against good-feel principles" | `references/principles-checklist.md` (8 yes/no audit tests) |
| "Polish, particles, screen shake, hit-stop, easing curves" | Defer to `game-feel-juice` (sibling skill, tactical toolbox) |

## The 3-Part Definition (Swink, p.6)

> "Real-time control of virtual objects in a simulated space, with interactions
> emphasized by polish."

A game has feel only when **all three** are present:

1. **Real-time control** — input maps to state every frame, not turn-by-turn
2. **Virtual objects in a simulated space** — the controlled thing exists in a
   spatial frame with consistent physics-or-pseudo-physics rules
3. **Polish** — sensory cues (visual + audio + haptic) emphasize each interaction

Missing any one of the three → not "game feel," it's something else (turn-based
play, abstract simulation, or spreadsheet UI).

## Perception Thresholds (hard numbers)

| Threshold | Value | Source |
|---|---|---|
| Frame rate floor (motion illusion holds) | 10 fps | Swink, p.43–44 |
| Modern smoothness baseline | 30+ fps | Swink, p.43–44 |
| Response feels instant | ≤50 ms | Swink, p.45 |
| Response feels tight | 50–100 ms | Swink, p.45 |
| Response feels sluggish | 100–150 ms | Swink, p.45 |
| Response feels broken | >240 ms | Swink, p.45 |

Below 10 fps the eye loses motion illusion; above 240 ms input-to-response the
player infers the game is broken or networked-laggy. Targets in between are
genre- and verb-dependent.

→ Deep dive: `references/definition-and-perception.md`

## The 6-Metric Framework

Swink decomposes "feel" into six measurable axes. Tune any one in isolation and
you affect the whole, but each has a distinct knob set and anti-pattern.

| Metric | Definition | Key Knob | Anti-Pattern | File |
|---|---|---|---|---|
| **Input** | The player's control surface | sensitivity, dimensions, dead zone | mushy, oversensitive | `references/metric-input.md` |
| **Response** | Input → state mapping (ADSR envelope) | attack / decay / sustain / release | floaty (>100ms attack) | `references/metric-response.md` |
| **Context** | Spatial framing of the controlled object's motion | object spacing, scale, camera framing | hemmed-in, autopilot | `references/metric-context.md` |
| **Polish** | Sensory cues that emphasize each interaction | animation curves, particles, audio decay | over-/under-/muddy-polish | `references/metric-polish.md` |
| **Metaphor** | Inferred physical reality (mass, weight, friction) | mass cues consistency across senses | lightweight giant | `references/metric-metaphor.md` |
| **Rules** | Meaning, agency, and difficulty around the action | difficulty curve, reward clarity | arbitrary, opaque | `references/metric-rules.md` |

**Reading order if you only have 30 minutes:** Response → Polish → Input. These
three account for most "this feels wrong" diagnoses.

## The 8 Principles of Good Game Feel

| # | Principle | Audit lens |
|---|---|---|
| 1 | Predictable Results | Same input + same state → same outcome |
| 2 | Instantaneous Response | Avatar moves on the input frame |
| 3 | Easy but Deep | Trivial to learn one verb, lifetime to master combinations |
| 4 | Novelty | Each level/system introduces a fresh input-response pairing |
| 5 | Appealing Response | Output is sensorially pleasant on its own |
| 6 | Organic Motion | Curves, not linear ramps; secondary motion |
| 7 | Harmony | All 6 metrics reinforce one feel, not contradict |
| 8 | Ownership | Player owns the wins AND the losses |

Each principle has a yes/no audit test (observable from the shipped build, not
designer introspection).

→ Audit tests: `references/principles-checklist.md`

## 30-Min Reverse-Engineering Methodology

When asked "why does game X feel so good — can we copy that?", apply the
12-step methodology to extract X's feel signature into measurable parameters
you can re-implement.

→ Checklist + 5 worked cases: `references/case-study-methodology.md`

## Polish-vs-Theory Decision Rule

Both `game-feel-foundations` and `game-feel-juice` cover "feel." They are
complementary, not redundant:

- Asking *why* a hit doesn't feel meaty / *how much* polish is enough →
  **this skill** (Polish is one of the 6 metrics; framework lets you measure it)
- Asking *how* to add the meaty hit (screen-shake values, hit-stop frames,
  particle counts, easing curves) → **`game-feel-juice`** (tactical toolbox)
- Both skills MAY co-fire when the request needs theory + tactics together
  (e.g., "diagnose why my hits feel wrong AND tune the screen shake")

## Cross-References

- `game-feel-juice` — tactical polish toolbox (screen shake, hit-stop, particles, easing curves) — sibling skill in this module
- `game-ux-design` — UI/UX feedback budget vs HUD readability
- engine camera skills (auto-activated via registry) — camera-feel implementation
- engine animation skills (auto-activated via registry) — curve and tween implementation

## Reference Files

| File | Coverage |
|---|---|
| `references/definition-and-perception.md` | 3-part definition, perception thresholds (frame rate, response time), interactivity loop, Fitts' Law |
| `references/metric-input.md` | Player control surface, sensitivity, dimensions, dead zone, mushy/oversensitive anti-patterns |
| `references/metric-response.md` | ADSR envelope (attack/decay/sustain/release), floaty/twitchy diagnosis, value ranges per genre |
| `references/metric-context.md` | Spatial framing, object spacing, scale, camera context, hemmed-in/autopilot anti-patterns |
| `references/metric-polish.md` | Polish as a metric (theory) vs juice as toolbox (tactics); cross-link to game-feel-juice; audio-polish dimensions |
| `references/metric-metaphor.md` | Inferred physical reality, mass cues, consistency across senses, lightweight-giant anti-pattern |
| `references/metric-rules.md` | Meaning + agency, difficulty curve, reward clarity, arbitrary/opaque anti-patterns |
| `references/principles-checklist.md` | 8 principles, each with an observable yes/no audit test |
| `references/case-study-methodology.md` | 12-step reverse-engineering checklist + 5 worked summaries |
