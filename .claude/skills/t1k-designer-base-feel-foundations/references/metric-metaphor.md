---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Metaphor — Game Feel Metric 5

The conceptual frame through which the player understands the game world. Metaphor is the player's *preconception* about how an object should behave, built from real-world experience and reinforced (or contradicted) by the game's other five metrics. Source: Swink Ch.10 (pp.171-178).

## Definition

Metaphor is the application of real-world analogies and physical metaphors to the game's input and response mechanics. It is how the player understands and expects the game to behave based on real-world experience. A gun in a game is expected to have recoil; a heavy object is expected to move slowly; a spring-loaded button should pop back when released (Swink, p.172). Metaphor is a soft metric because it measures the coherence and fidelity of the player's mental model relative to the real world or a stylized alternative world.

## What the Player Infers from the Metaphor

Metaphor shapes player expectations through real-world analogy:

- **Inferred mass** — Weight inferred from animation (squash-stretch), particles (violent vs. gentle), sound (pitch + decay), and size relative to context
- **Inferred material** — Texture cues (fuzzy, sticky, crumbly, shiny, dull) from VFX and SFX
- **Inferred friction** — Slideability inferred from collision behavior and sound (squeal = low friction; grind = high friction)
- **Inferred rigidity** — Bounciness, deformation, and structural integrity inferred from response envelope
- **Inferred agency** — Alive/threatening vs. passive/decorative inferred from animation and interaction pattern

When a game is consistent with the player's real-world mental model, mechanics feel intuitive. When it breaks the model, the interaction feels broken or unintended (Swink, pp.171-172).

## Types of Metaphor (Swink's Classification)

**Direct Metaphor:** Game mechanic maps directly to real-world physics. Example: a firearm uses real-world gun physics as the metaphor (recoil, bullet drop, spread). (p.172)

**Stylized Metaphor:** Game exaggerates a real-world property for game feel. Example: Tony Hawk's skateboarding applies infinite flip momentum and unrealistic terrain compared to real skateboarding. (p.173)

**Alternative Metaphor:** Game invents a new metaphor without real-world grounding. Example: fantasy games with magic systems not mappable to real physics. (p.172)

## Metaphor Fidelity vs. Game Feel

Swink does not prescribe a single definition of "good" metaphor fidelity. Rather, fidelity is a design choice: simulation games prioritize high fidelity to real-world physics; arcade games prioritize game feel and gameplay over realism. Both are valid, provided the metaphor remains consistent (p.173).

**Example:** Real-world gravity is ~9.8 m/s². A platformer might use 2x real-world gravity to make jumping feel punchier and platforming more challenging. This stylized metaphor breaks real-world fidelity but serves intended game feel (Swink, p.173-174).

## Sub-axes / Dimensions

- **Realistic vs. stylized vs. abstract** — Fidelity to real-world analogs
- **Single-metaphor coherence vs. mixed-metaphor risk** — Consistency across related systems
- **Narrative metaphor vs. mechanical metaphor** — Alignment between story world and mechanics
- **Visual-audio-mechanic coherence** — Polish channels support inferred properties

## Knobs (Designer-Tunable)

- **Metaphor selection** — Which real-world property to use as the basis
- **Metaphor fidelity** — How closely to match real-world behavior
- **Stylization level** — How much to exaggerate or deviate for game feel
- **Consistency** — Whether to apply the same metaphor across systems
- Animation style and timing (weight inferred from motion curve)
- Particle scale, count, and scatter pattern
- Sound pitch, timbre, and decay
- Size relative to avatar and scene
- Collision behavior (friction, elasticity)

## Diagnostic Questions

- What real-world property does this mechanic represent?
- How faithfully does the game implement that property?
- Is the metaphor consistent across all related mechanics?
- Does the object feel like it weighs what it looks like?
- Do particles and sound reinforce or contradict the visual form?
- Is behavior consistent with player preconception?
- Are there any contradictions between metaphors in different systems?
- Is the metaphor clear to the player, or obscure?

## Anti-patterns (Page-Cited from Swink Ch.10)

**Broken Metaphor** — Game sets up a metaphor and then violates it without explanation. Example: a gun with realistic recoil is suddenly given homing bullets with no in-game explanation. (p.174-175)

**Mixed Metaphors** — Different systems use incompatible metaphors. Example: a gun with realistic recoil physics but unrealistic bullets that curve mid-flight, mixing firearm and magic metaphors. (p.174-175)

**Lazy Metaphor** — A metaphor is stated but weakly executed. Example: a "heavy" object is just a slower-moving square with no animation, sound, or visual feedback to support weight. (p.174)

**Metaphor Contradiction** — Two systems use incompatible metaphors. Example: a platformer with realistic gravity but unrealistic infinite mid-air movement in 8 directions creates cognitive dissonance. (p.174-175)

**Lightweight Giant** — A massive-looking object that moves with light, twitchy animation or sounds light.

**Massive Squirrel** — A tiny object with heavy animation and deep sound (uncanny and breaks expectations).

**No Metaphor** — Mechanics feel arbitrary with no grounding in real-world analogy.

## How Metaphor Interacts with Other Metrics

- **→ Polish** — Polish channels (animation, VFX, SFX, cinematic, tactile) build the metaphor over time. Consistency reinforces expectation; contradiction breaks it.
- **→ Response** — The ADSR envelope shape must match inferred mass. Light objects attack fast; heavy objects attack slow.
- **→ Context** — The spatial frame must support the metaphor. Huge open space suits a colossus; tight halls suit a stealth game.
- **→ Rules** — Rules must enforce metaphor consequences. Heavy objects should take more hits; slippery surfaces should have low friction.

**Establishing Expectations:** Metaphor reduces cognitive load by letting players extrapolate from real-world knowledge rather than learn arbitrary rules. Consistent metaphors reinforce immersion and enable flow (Swink, p.175-176).

## Cross-Reference

- For Polish (channels that build metaphor): see metric-polish.md
- For Context (spatial framing of metaphor): see metric-context.md
- For Rules (enforcing metaphor consequences): see metric-rules.md
- For Response (ADSR shape matching inferred mass): see metric-response.md
- For perception thresholds (why 100ms is the upper bound for any response metaphor): see definition-and-perception.md
