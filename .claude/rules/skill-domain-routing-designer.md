---
origin: theonekit-designer
repository: The1Studio/theonekit-designer
module: null
protected: false
---
<!-- t1k-origin: kit=theonekit-designer | repo=The1Studio/theonekit-designer | module=null | protected=true -->

# Skill Domain Routing (theonekit-designer)

Intent-based discovery for game design kit skills. For core T1K skills (cook, fix, plan, etc.), see `skills/t1k-help/references/skill-domain-routing.md`.

## Mechanics & Game Systems Design

User wants to...
- Balance stat formulas, DPS curves, and difficulty tuning → `game-balance-tools`
- Design RPG progression, classes, abilities, and loot tables → `rpg-game-design`
- Design a roguelike run structure, meta-progression, and unlocks → `game-roguelike-design`
- Design puzzle mechanics (match-3, polyomino, grid logic) → `puzzle-game-design`
- Design turn-based combat pacing, action economy, and AI turns → `game-turn-based-design`

## Economy & Monetization

User wants to...
- Design a virtual economy: currencies, sinks, faucets, and inflation control → `game-economy-design`
- Design a shop: pricing tiers, rarity distribution, and pity systems → `game-shop-offering`
- Design item synergy systems and combo scoring → `game-synergy-combos`

## Feel & Pacing

User wants to...
- Add game feel: juice, hit stop, screen shake, and particle feedback → `game-feel-juice`
- Design level or arena spatial layouts → `game-level-design`
- Design procedural generation: seeds, noise, and room templates → `game-procedural-generation`

## Narrative & World

User wants to...
- Design story structure, branching choices, and player agency → `game-narrative-design`
- Design quest structure, dialogue trees, and reward hooks → `game-quest-design`
- Build world lore, factions, and setting documents → `game-worldbuilding`

## UI/UX Design

User wants to...
- Design HUD layout, touch UX, and onboarding flow → `game-ux-design`
- Wireframe menus, shop screens, and inventory layouts → `game-ui-wireframe`
- Define visual design: palettes, typography, and rarity tiers → `game-visual-design`
- Design UI transition animations and micro-interactions → `game-ui-animation`

## Documentation & Mobile

User wants to...
- Write a living Game Design Document (GDD) or wiki pages → `game-design-document`
- Apply mobile-specific design patterns (thumb reach, session length) → `game-mobile-design`

## Review & Validation (Design-Perspective, NOT Code-Reviewer)

**Rule:** Reviewing or validating design docs, wiki pages, narrative content, balance plans, or any artifact produced by a designer-kit skill MUST use **design-perspective agents** — NOT `code-reviewer`. Code-reviewer is for code-quality concerns (security, patterns, DRY/KISS); it is the wrong lens for design artifacts.

User wants to...
- Validate plan completeness, phase structure, dependency graph, success criteria → `planner` agent (audits its own discipline)
- Review design docs for **content/pillar coverage** — does the deliverable serve the documented design pillars? → `game-designer` agent
- Review design docs for **data SSOT compliance** — is narrative referencing CSV/JSON source-of-truth correctly? Activate `design-data-authoring` skill alongside → `game-designer` agent
- Review for **tone, voice, pillar fidelity, alternative-tone risks** during multi-agent fan-out → `designer-brainstormer` agent
- Review for **production readiness, ship-gate, stakeholder-decision propagation, milestone gates** → `game-producer` agent
- Review for **wiki organization, navigability, cross-link consistency** → `game-designer` agent (with `game-design-document` skill active)

### Multi-round design validation (5-round adversarial pattern)

When the user requests "validate in N rounds" or asks for adversarial review on design artifacts, prefer this distribution over generic code-reviewer chains:

| Round | Agent | Lens |
|---|---|---|
| 1 | `planner` | Plan rigor — phase structure, file ownership, success criteria coverage, risk register |
| 2 | `game-designer` | Design completeness — pillars served, content coverage, no contradictions to GDD |
| 3 | `game-designer` (with `design-data-authoring`) | Data SSOT — CSV/JSON read-before-write, no narrative-data drift |
| 4 | `designer-brainstormer` | Tone & voice — pillar fidelity under parallel fan-out, alternative-tone risks |
| 5 | `game-producer` | Production readiness — stakeholder gates, ship gates, milestone alignment |

**Anti-pattern:** spawning ≥2 `code-reviewer` agents for design-doc validation. Code-reviewer cannot judge whether lore content serves the game's pillars, whether tooltips drift from items.csv, or whether a story bible's faction conflict reads as comedy vs. melodrama. Use designer-perspective agents.

## Notes

- All skills above are designer-kit skills; invoke via the Skill tool
- For core T1K workflow skills (cook, fix, plan, test, review), see `skills/t1k-help/references/skill-domain-routing.md`
- Game design skills are advisory and documentation-focused — they produce design docs, not code
- **Validation routing:** code-reviewer is for code; designer-perspective agents are for design artifacts (see "Review & Validation" section above)
