---

origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: base
protected: false
---
# Rules — Game Feel Metric 6

The underlying systems logic that defines valid actions, constraints, state transitions, and the cause-effect relationships the player learns. Rules give meaning to the other 5 metrics — they're WHY a perfectly-tuned ADSR matters. Source: Swink Ch.11 (pp.179-186).

## Definition

**Rules** are the system of constraints and mechanical laws that govern how the game world responds to player input and interaction (p.179). They transform challenge into agency by making cause-effect predictable: the player can predict outcomes based on understanding the rules, enabling flow and mastery. Rules are both deterministic (measurable: a rule is followed or violated) and soft (subjective impact on feel: opaque rules cause frustration, clear rules enable mastery).

## Components of the Rules Metric (Swink's breakdown)

- **Simulation layer** (p.179-181) — the physics simulation calculating motion, collision, and forces; mostly invisible but outputs (position, velocity) shape perception
- **State machines** (p.180-182) — explicit state transitions that change input meaning (e.g., ground vs. air state in Super Mario changes how left/right input responds)
- **Constraints** (p.182-183) — rules preventing or limiting actions (e.g., maximum jump height capping vertical velocity)
- **Thresholds** (p.183-184) — invisible or visible boundaries triggering behavior changes (e.g., damage threshold entering "critical health" state with different visual/audio feedback)

## Sub-axes / Dimensions

- **Rule clarity** — explicit vs. discovered through trial-and-error (p.180-184)
- **Rule consistency** — applied uniformly or with hidden exceptions (p.184-185)
- **Player mental-model alignment** — does player's assumption match actual rules? (p.179)
- **Predictability** — given input + state, can outcome be predicted? (p.185-186)
- **Visibility** — observable rule operation vs. hidden mechanics
- **Feedback loops** — actions feeding into subsequent challenges, enabling progression
- **Progression pacing** — rate of introducing new rules to player (related to cognitive load)
- **Player agency** — degree of control over outcome (deterministic vs. random/luck-based)

## Knobs (Designer-Tunable)

- Difficulty curve (enemy stats, scaling formulas)
- Reward schedule (frequency and magnitude of rewards)
- Mechanic introduction pacing (how many new rules per play session)
- Feedback latency (delay between action and consequence; see Response chapter for perception thresholds)
- Rule clarity (how obvious cause-effect mapping is)
- Randomness level (pure deterministic vs. probabilistic outcomes)
- Resource scarcity (ammo, health pickups, respawn delays)
- State visibility (how obvious current game state is to player)
- Constraint tightness (forgiving vs. punishing rule enforcement)

## Diagnostic Questions

- Does the player understand WHY they succeeded or failed? (p.179)
- Do rewards feel earned or arbitrary?
- Is cause-effect clear between action and consequence? (p.180-184)
- Does difficulty scale with player skill, or is it fixed? (p.185)
- Are new mechanics introduced at a pace the player can absorb? (p.180)
- Do rules create meaningful choices, or is the optimal path forced? (p.185-186)
- Are rule violations communicated with clear feedback, or are they silent? (p.181-183)
- Do rules contradict the established metaphor? (p.184)

## Tuning Ranges from Swink

- **Feedback latency:** Rules must provide clear, timely feedback when triggered; visibility helps players predict rule operation (p.181-184)
- **Consistency:** Rules must apply uniformly or with clear, communicated exceptions (p.184-185, Metroid example: beam combinations)
- **State transitions:** Fewer states = simpler, more predictable (p.185)
- **Constraint visibility:** Higher percentage of visible constraints = easier learning (p.181-183)
- **Difficulty curve:** Should ramp smoothly; spikes frustrate, plateaus bore (p.185)

## Anti-patterns (Page-Cited)

- **Opaque rules** (p.180-184) — player must read manual or infer from failure; core mechanic unclear
- **Rule inconsistency** (p.184-185) — enemy avoids fire in one scene, walks through fire in another; no logical pattern
- **Arbitrary rules** (p.179-180) — player blames design not skill; instant-fail with no warning, hidden stat checks
- **Unclear cause-effect** (p.180-184) — player doesn't know why they died or what to do next time
- **Meaningless rewards** (p.185) — XP/points that don't affect gameplay or future challenge
- **Slow feedback** (p.184) — consequence appears minutes later (lose health silently, only visible at level end)
- **Silent rule violations** (p.181-183) — threshold crossed with no indication (damage taken invisibly)
- **Railroading** (p.185-186) — no meaningful choices; optimal path forced by rule design
- **Cascading rule changes** — too many state-dependent rules at once, causing cognitive overload
- **Invisible thresholds** (p.183-184) — boundaries crossed with no visual or audio signal

## How Rules Interact with Other Metrics

- **← Input + Response** (p.184) — clear rules let player use input deliberately; opaque rules turn input into guessing
- **← Metaphor** (p.184, 179) — rules must enforce metaphor consequences (heavy object obeys gravity; gun has recoil when rule-consistent)
- **← Context** (p.179-180) — rules + spatial framing combine into environmental meaning (this room is safe, that one hostile)
- **← Polish** (p.181-183) — polish telegraphs rule operation (threshold crossed → flash + sound signals feedback)
- **→ Player Agency** — deterministic rules with clear feedback enable mastery; random rules undermine agency

## Cross-Reference

- For perception thresholds (why feedback latency is ~200ms): see definition-and-perception.md
- For Metaphor (rules enforce metaphor consequences): see metric-metaphor.md
- For Polish (telegraphing rule operation): see metric-polish.md
- For 8 principles audit (rules-related: Predictable Results, Ownership): see principles-checklist.md
